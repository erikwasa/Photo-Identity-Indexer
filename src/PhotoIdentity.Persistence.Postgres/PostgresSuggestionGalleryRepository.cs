using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresSuggestionGalleryRepository : ISuggestionGalleryRepository
{
    private const string Ctes = """
        WITH latest_action AS (
            SELECT
                review_actions.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY id DESC) AS row_number
            FROM review_actions
            WHERE action_kind IN ('assign', 'unknown', 'reject')
              AND reversed_at_utc IS NULL
        ),
        latest_crop AS (
            SELECT
                face_crops.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY created_at_utc DESC, id DESC) AS row_number
            FROM face_crops
        ),
        latest_observation AS (
            SELECT
                face_observations.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY observed_at_utc DESC, detector_model_id, detector_model_hash) AS row_number
            FROM face_observations
        ),
        top_suggestion AS (
            SELECT
                rankings.face_occurrence_id,
                suggestions.id AS suggestion_id,
                suggestions.suggested_person_id,
                suggested_people.display_name,
                rankings.model_id,
                rankings.model_hash,
                rankings.rank,
                suggestions.score,
                rankings.score_margin,
                suggestions.status,
                rankings.generated_at_utc
            FROM identity_suggestion_rankings AS rankings
            INNER JOIN identity_suggestions AS suggestions
                ON suggestions.id = rankings.suggestion_id
            INNER JOIN people AS suggested_people
                ON suggested_people.id = suggestions.suggested_person_id
               AND suggested_people.merged_into_person_id IS NULL
            WHERE rankings.rank = 1
              AND suggestions.status = 'pending'
              AND rankings.model_id = @model_id
              AND rankings.model_hash = @model_hash
        )
        """;

    private const string Columns = """
        face_occurrences.id,
        face_occurrences.ordinal,
        face_occurrences.created_at_utc,
        assets.source_key,
        COALESCE(asset_revisions.media_type, 'application/octet-stream'),
        asset_revisions.width,
        asset_revisions.height,
        asset_revisions.content_sha256,
        latest_crop.storage_path,
        latest_observation.confidence,
        latest_action.id,
        latest_action.action_kind,
        latest_action.person_id,
        assigned_people.display_name,
        top_suggestion.suggestion_id,
        top_suggestion.suggested_person_id,
        top_suggestion.display_name,
        top_suggestion.model_id,
        top_suggestion.model_hash,
        top_suggestion.rank,
        top_suggestion.score,
        top_suggestion.score_margin,
        top_suggestion.status,
        top_suggestion.generated_at_utc,
        latest_observation.bounding_box_json,
        face_occurrences.asset_revision_id
        """;

    private const string From = """
        FROM face_occurrences
        INNER JOIN asset_revisions
            ON asset_revisions.id = face_occurrences.asset_revision_id
        INNER JOIN assets
            ON assets.id = asset_revisions.asset_id
        LEFT JOIN latest_crop
            ON latest_crop.face_occurrence_id = face_occurrences.id
           AND latest_crop.row_number = 1
        LEFT JOIN latest_observation
            ON latest_observation.face_occurrence_id = face_occurrences.id
           AND latest_observation.row_number = 1
        LEFT JOIN latest_action
            ON latest_action.face_occurrence_id = face_occurrences.id
           AND latest_action.row_number = 1
        LEFT JOIN people AS assigned_people
            ON assigned_people.id = latest_action.person_id
        LEFT JOIN top_suggestion
            ON top_suggestion.face_occurrence_id = face_occurrences.id
        """;

    private readonly PostgresCatalogueDatabase _database;
    private readonly IIdentitySuggestionPolicyRepository _policyRepository;

    public PostgresSuggestionGalleryRepository(
        PostgresCatalogueDatabase database,
        IIdentitySuggestionPolicyRepository? policyRepository = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _policyRepository = policyRepository ?? new PostgresIdentitySuggestionPolicyRepository(database);
    }

    public async Task<ReviewSuggestionGalleryPage> GetFacesAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int offset,
        int limit,
        string state,
        ProcessingRunId? processingRunId,
        string sort,
        string confidenceGroup,
        PersonId? suggestedPersonId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Suggestion gallery page size must be between 1 and 200.");
        }

        ReviewIdentitySuggestionPolicy policy = await _policyRepository.GetAsync(
            modelId,
            modelHash,
            cancellationToken);
        string predicate = BuildPredicate(state, processingRunId, confidenceGroup, suggestedPersonId);
        string orderBy = SortExpression(sort);

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {Ctes}
            SELECT
                {Columns}
            {From}
            WHERE {predicate}
            ORDER BY {orderBy}
            LIMIT @limit OFFSET @offset;
            """;
        AddParameters(command, modelId, modelHash, processingRunId, policy, suggestedPersonId);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

        List<ReviewSuggestionGalleryFace> items = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(ReadFace(reader, policy));
            }
        }

        await using NpgsqlCommand countCommand = connection.CreateCommand();
        countCommand.CommandText = $"""
            {Ctes}
            SELECT COUNT(*)
            {From}
            WHERE {predicate};
            """;
        AddParameters(countCommand, modelId, modelHash, processingRunId, policy, suggestedPersonId);
        object? count = await countCommand.ExecuteScalarAsync(cancellationToken);

        return new ReviewSuggestionGalleryPage(
            items,
            offset,
            limit,
            checked(Convert.ToInt32(count)));
    }

    public async Task<ReviewSuggestionGalleryNavigation?> GetNavigationAsync(
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash,
        string state,
        ProcessingRunId? processingRunId,
        string sort,
        string confidenceGroup,
        PersonId? suggestedPersonId,
        CancellationToken cancellationToken = default)
    {
        string normalizedSort = NormalizeSort(sort);
        ReviewIdentitySuggestionPolicy policy = await _policyRepository.GetAsync(
            modelId,
            modelHash,
            cancellationToken);
        string predicate = BuildPredicate(state, processingRunId, confidenceGroup, suggestedPersonId);
        string orderBy = SortExpression(normalizedSort);

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {Ctes},
            scoped_faces AS (
                SELECT
                    face_occurrences.id,
                    LAG(face_occurrences.id) OVER (ORDER BY {orderBy}) AS previous_face_id,
                    LEAD(face_occurrences.id) OVER (ORDER BY {orderBy}) AS next_face_id,
                    ROW_NUMBER() OVER (ORDER BY {orderBy}) AS position,
                    COUNT(*) OVER () AS total
                {From}
                WHERE {predicate}
            )
            SELECT previous_face_id, next_face_id, position, total
            FROM scoped_faces
            WHERE id = @face_occurrence_id;
            """;
        AddParameters(command, modelId, modelHash, processingRunId, policy, suggestedPersonId);
        command.Parameters.AddWithValue("face_occurrence_id", Guid.Parse(faceOccurrenceId.ToString()));

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReviewSuggestionGalleryNavigation(
            reader.IsDBNull(0) ? null : FaceOccurrenceId.From(reader.GetGuid(0)),
            reader.IsDBNull(1) ? null : FaceOccurrenceId.From(reader.GetGuid(1)),
            checked((int)reader.GetInt64(2)),
            checked((int)reader.GetInt64(3)),
            normalizedSort);
    }

    private static string BuildPredicate(
        string state,
        ProcessingRunId? processingRunId,
        string confidenceGroup,
        PersonId? suggestedPersonId)
    {
        List<string> predicates = [StatePredicate(state), ConfidencePredicate(confidenceGroup)];
        if (processingRunId is not null)
        {
            predicates.Add("""
                EXISTS (
                    SELECT 1
                    FROM processing_jobs
                    WHERE processing_jobs.asset_revision_id = face_occurrences.asset_revision_id
                      AND processing_jobs.processing_run_id = @processing_run_id)
                """);
        }

        if (suggestedPersonId is not null)
        {
            predicates.Add("top_suggestion.suggested_person_id = @suggested_person_id");
        }

        return string.Join(" AND ", predicates.Select(value => $"({value})"));
    }

    private static void AddParameters(
        NpgsqlCommand command,
        ModelId modelId,
        Sha256Digest modelHash,
        ProcessingRunId? processingRunId,
        ReviewIdentitySuggestionPolicy policy,
        PersonId? suggestedPersonId)
    {
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("high_score_threshold", policy.HighScoreThreshold);
        command.Parameters.AddWithValue("high_margin_threshold", policy.HighMarginThreshold);
        command.Parameters.AddWithValue("medium_score_threshold", policy.MediumScoreThreshold);
        if (processingRunId is ProcessingRunId runId)
        {
            command.Parameters.AddWithValue("processing_run_id", Guid.Parse(runId.ToString()));
        }
        if (suggestedPersonId is PersonId personId)
        {
            command.Parameters.AddWithValue("suggested_person_id", Guid.Parse(personId.ToString()));
        }
    }

    private static string StatePredicate(string state)
    {
        string normalized = string.IsNullOrWhiteSpace(state)
            ? "unreviewed"
            : state.Trim().ToLowerInvariant();
        return normalized switch
        {
            "unreviewed" => "latest_action.id IS NULL",
            "assigned" => "latest_action.action_kind = 'assign'",
            "unknown" => "latest_action.action_kind = 'unknown'",
            "rejected" => "latest_action.action_kind = 'reject'",
            "all" => "1 = 1",
            _ => throw new ArgumentException($"Unsupported review state '{state}'.", nameof(state)),
        };
    }

    private static string ConfidencePredicate(string confidenceGroup) => NormalizeConfidenceGroup(confidenceGroup) switch
    {
        "all" => "1 = 1",
        "high" => """
            top_suggestion.suggestion_id IS NOT NULL
            AND top_suggestion.score >= @high_score_threshold
            AND top_suggestion.score_margin IS NOT NULL
            AND top_suggestion.score_margin >= @high_margin_threshold
            """,
        "medium" => """
            top_suggestion.suggestion_id IS NOT NULL
            AND top_suggestion.score >= @medium_score_threshold
            AND NOT (
                top_suggestion.score >= @high_score_threshold
                AND top_suggestion.score_margin IS NOT NULL
                AND top_suggestion.score_margin >= @high_margin_threshold)
            """,
        "low" => """
            top_suggestion.suggestion_id IS NOT NULL
            AND top_suggestion.score < @medium_score_threshold
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(confidenceGroup)),
    };

    private static string NormalizeConfidenceGroup(string confidenceGroup)
    {
        string normalized = string.IsNullOrWhiteSpace(confidenceGroup)
            ? "all"
            : confidenceGroup.Trim().ToLowerInvariant();
        return normalized switch
        {
            "all" or "high" or "medium" or "low" => normalized,
            _ => throw new ArgumentException(
                $"Unsupported suggestion confidence group '{confidenceGroup}'.",
                nameof(confidenceGroup)),
        };
    }

    private static string NormalizeSort(string sort)
    {
        string normalized = string.IsNullOrWhiteSpace(sort)
            ? "created-desc"
            : sort.Trim().ToLowerInvariant();
        return normalized switch
        {
            "created-desc" or
            "suggested-person" or
            "confidence-group" or
            "margin-desc" or
            "margin-asc" or
            "score-desc" or
            "no-suggestion-first" => normalized,
            _ => throw new ArgumentException($"Unsupported suggestion gallery sort '{sort}'.", nameof(sort)),
        };
    }

    private static string SortExpression(string sort) => NormalizeSort(sort) switch
    {
        "created-desc" =>
            "face_occurrences.created_at_utc DESC, face_occurrences.id",
        "suggested-person" =>
            "CASE WHEN top_suggestion.suggestion_id IS NULL THEN 1 ELSE 0 END, " +
            "lower(top_suggestion.display_name), top_suggestion.suggested_person_id, " +
            "top_suggestion.score_margin DESC, top_suggestion.score DESC, " +
            "face_occurrences.created_at_utc DESC, face_occurrences.id",
        "confidence-group" =>
            "CASE " +
            "WHEN top_suggestion.suggestion_id IS NULL THEN 3 " +
            "WHEN top_suggestion.score >= @high_score_threshold " +
            "AND top_suggestion.score_margin IS NOT NULL " +
            "AND top_suggestion.score_margin >= @high_margin_threshold THEN 0 " +
            "WHEN top_suggestion.score >= @medium_score_threshold THEN 1 " +
            "ELSE 2 END, " +
            "top_suggestion.score DESC, top_suggestion.score_margin DESC, " +
            "face_occurrences.created_at_utc DESC, face_occurrences.id",
        "margin-desc" =>
            "CASE WHEN top_suggestion.suggestion_id IS NULL THEN 1 ELSE 0 END, " +
            "CASE WHEN top_suggestion.score_margin IS NULL THEN 1 ELSE 0 END, " +
            "top_suggestion.score_margin DESC, top_suggestion.score DESC, " +
            "face_occurrences.created_at_utc DESC, face_occurrences.id",
        "margin-asc" =>
            "CASE WHEN top_suggestion.suggestion_id IS NULL THEN 1 ELSE 0 END, " +
            "CASE WHEN top_suggestion.score_margin IS NULL THEN 1 ELSE 0 END, " +
            "top_suggestion.score_margin, top_suggestion.score DESC, " +
            "face_occurrences.created_at_utc DESC, face_occurrences.id",
        "score-desc" =>
            "CASE WHEN top_suggestion.suggestion_id IS NULL THEN 1 ELSE 0 END, " +
            "top_suggestion.score DESC, top_suggestion.score_margin DESC, " +
            "face_occurrences.created_at_utc DESC, face_occurrences.id",
        "no-suggestion-first" =>
            "CASE WHEN top_suggestion.suggestion_id IS NULL THEN 0 ELSE 1 END, " +
            "face_occurrences.created_at_utc DESC, face_occurrences.id",
        _ => throw new ArgumentOutOfRangeException(nameof(sort)),
    };

    private static ReviewSuggestionGalleryFace ReadFace(
        NpgsqlDataReader reader,
        ReviewIdentitySuggestionPolicy policy)
    {
        string sourceKey = reader.GetString(3).Replace('\\', '/');
        string photoName = Path.GetFileName(sourceKey);
        string? actionKind = reader.IsDBNull(11) ? null : reader.GetString(11);
        PersonId? assignedPersonId = reader.IsDBNull(12)
            ? null
            : PersonId.From(reader.GetGuid(12));
        string? assignedPersonName = reader.IsDBNull(13) ? null : reader.GetString(13);
        ReviewSuggestionGalleryPerson? assignedPerson =
            assignedPersonId is PersonId personId && assignedPersonName is not null
                ? new ReviewSuggestionGalleryPerson(personId, assignedPersonName)
                : null;

        ReviewSuggestionGalleryTopSuggestion? topSuggestion = null;
        if (!reader.IsDBNull(14))
        {
            double score = reader.GetDouble(20);
            double? scoreMargin = reader.IsDBNull(21) ? null : reader.GetDouble(21);
            topSuggestion = new ReviewSuggestionGalleryTopSuggestion(
                reader.GetInt64(14),
                new ReviewSuggestionGalleryPerson(
                    PersonId.From(reader.GetGuid(15)),
                    reader.GetString(16)),
                new ModelId(reader.GetString(17)),
                new Sha256Digest(reader.GetString(18)),
                reader.GetInt32(19),
                score,
                scoreMargin,
                reader.GetString(22),
                reader.GetFieldValue<DateTimeOffset>(23),
                policy.Classify(score, scoreMargin));
        }

        string reviewState = actionKind switch
        {
            "assign" => "assigned",
            "unknown" => "unknown",
            "reject" => "rejected",
            _ => "unreviewed",
        };

        return new ReviewSuggestionGalleryFace(
            FaceOccurrenceId.From(reader.GetGuid(0)),
            reader.GetInt32(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            string.IsNullOrWhiteSpace(photoName) ? "Photo" : photoName,
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            new Sha256Digest(reader.GetString(7)),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetDouble(9),
            reviewState,
            assignedPerson,
            topSuggestion,
            AssetRevisionId.From(reader.GetGuid(25)),
            reader.IsDBNull(24) ? null : reader.GetString(24));
    }
}
