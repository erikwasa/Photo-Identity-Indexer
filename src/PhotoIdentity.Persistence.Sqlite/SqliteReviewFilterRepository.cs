using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Provides review gallery queries scoped by processing run and ranked suggestion model revision.
/// </summary>
public sealed class SqliteReviewFilterRepository
{
    private const string ReviewFaceCtes = """
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
        )
        """;

    private const string ReviewFaceColumns = """
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
        people.display_name,
        latest_observation.bounding_box_json,
        face_occurrences.asset_revision_id
        """;

    private const string ReviewFaceFrom = """
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
        LEFT JOIN people
            ON people.id = latest_action.person_id
        """;

    private readonly SqliteCatalogueDatabase _database;

    public SqliteReviewFilterRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueReviewFacePage> GetFacesAsync(
        int offset = 0,
        int limit = 40,
        string state = CatalogueReviewStates.Unreviewed,
        ProcessingRunId? processingRunId = null,
        ModelId? modelId = null,
        Sha256Digest? modelHash = null,
        string sort = CatalogueReviewSorts.CreatedDescending,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Review page size must be between 1 and 200.");
        }

        ValidateScope(modelId, modelHash);
        string orderBy = SortExpression(sort);
        string predicate = BuildPredicate(state, processingRunId, modelId, modelHash);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {ReviewFaceCtes}
            SELECT
                {ReviewFaceColumns}
            {ReviewFaceFrom}
            WHERE {predicate}
            ORDER BY {orderBy}
            LIMIT $limit OFFSET $offset;
            """;
        AddScopeParameters(command, processingRunId, modelId, modelHash);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        List<CatalogueReviewFace> items = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(ReadFace(reader));
            }
        }

        using SqliteCommand countCommand = connection.CreateCommand();
        countCommand.CommandText = $"""
            {ReviewFaceCtes}
            SELECT COUNT(*)
            {ReviewFaceFrom}
            WHERE {predicate};
            """;
        AddScopeParameters(countCommand, processingRunId, modelId, modelHash);
        object? count = await countCommand.ExecuteScalarAsync(cancellationToken);
        return new CatalogueReviewFacePage(
            items,
            offset,
            limit,
            Convert.ToInt32(count, CultureInfo.InvariantCulture));
    }

    public async Task<CatalogueReviewFaceNavigation?> GetNavigationAsync(
        FaceOccurrenceId faceOccurrenceId,
        string state = "all",
        ProcessingRunId? processingRunId = null,
        ModelId? modelId = null,
        Sha256Digest? modelHash = null,
        string sort = CatalogueReviewSorts.CreatedDescending,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(modelId, modelHash);
        string normalizedSort = NormalizeSort(sort);
        string orderBy = SortExpression(normalizedSort);
        string predicate = BuildPredicate(state, processingRunId, modelId, modelHash);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {ReviewFaceCtes},
            scoped_faces AS (
                SELECT
                    face_occurrences.id,
                    LAG(face_occurrences.id) OVER (ORDER BY {orderBy}) AS previous_face_id,
                    LEAD(face_occurrences.id) OVER (ORDER BY {orderBy}) AS next_face_id,
                    ROW_NUMBER() OVER (ORDER BY {orderBy}) AS position,
                    COUNT(*) OVER () AS total
                {ReviewFaceFrom}
                WHERE {predicate}
            )
            SELECT previous_face_id, next_face_id, position, total
            FROM scoped_faces
            WHERE id = $face_occurrence_id;
            """;
        AddScopeParameters(command, processingRunId, modelId, modelHash);
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        FaceOccurrenceId? previous = reader.IsDBNull(0)
            ? null
            : FaceOccurrenceId.From(Guid.Parse(reader.GetString(0)));
        FaceOccurrenceId? next = reader.IsDBNull(1)
            ? null
            : FaceOccurrenceId.From(Guid.Parse(reader.GetString(1)));
        return new CatalogueReviewFaceNavigation(
            previous,
            next,
            checked((int)reader.GetInt64(2)),
            checked((int)reader.GetInt64(3)),
            normalizedSort);
    }

    public async Task<CatalogueReviewFilterOptions> GetOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        List<CatalogueReviewProcessingRun> runs = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    processing_runs.id,
                    processing_runs.status,
                    processing_runs.started_at_utc,
                    processing_runs.completed_at_utc,
                    COUNT(DISTINCT face_occurrences.id)
                FROM processing_runs
                INNER JOIN processing_jobs
                    ON processing_jobs.processing_run_id = processing_runs.id
                INNER JOIN face_occurrences
                    ON face_occurrences.asset_revision_id = processing_jobs.asset_revision_id
                GROUP BY
                    processing_runs.id,
                    processing_runs.status,
                    processing_runs.started_at_utc,
                    processing_runs.completed_at_utc
                ORDER BY processing_runs.started_at_utc DESC, processing_runs.id;
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                runs.Add(new CatalogueReviewProcessingRun(
                    ProcessingRunId.From(Guid.Parse(reader.GetString(0))),
                    reader.GetString(1),
                    Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : Parse(reader.GetString(3)),
                    reader.GetInt32(4)));
            }
        }

        List<CatalogueReviewModelRevision> models = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    model_id,
                    model_hash,
                    MAX(generated_at_utc),
                    COUNT(DISTINCT face_occurrence_id)
                FROM identity_suggestion_rankings
                GROUP BY model_id, model_hash
                ORDER BY MAX(generated_at_utc) DESC, model_id, model_hash;
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                models.Add(new CatalogueReviewModelRevision(
                    new ModelId(reader.GetString(0)),
                    new Sha256Digest(reader.GetString(1)),
                    Parse(reader.GetString(2)),
                    reader.GetInt32(3)));
            }
        }

        return new CatalogueReviewFilterOptions(runs, models);
    }

    private static string BuildPredicate(
        string state,
        ProcessingRunId? processingRunId,
        ModelId? modelId,
        Sha256Digest? modelHash)
    {
        List<string> predicates = [StatePredicate(state)];
        if (processingRunId is not null)
        {
            predicates.Add("""
                EXISTS (
                    SELECT 1
                    FROM processing_jobs
                    WHERE processing_jobs.asset_revision_id = face_occurrences.asset_revision_id
                      AND processing_jobs.processing_run_id = $processing_run_id)
                """);
        }

        if (modelId is not null && modelHash is not null)
        {
            predicates.Add("""
                EXISTS (
                    SELECT 1
                    FROM identity_suggestion_rankings
                    WHERE identity_suggestion_rankings.face_occurrence_id = face_occurrences.id
                      AND identity_suggestion_rankings.model_id = $model_id
                      AND identity_suggestion_rankings.model_hash = $model_hash)
                """);
        }

        return string.Join(" AND ", predicates.Select(value => $"({value})"));
    }

    private static void ValidateScope(ModelId? modelId, Sha256Digest? modelHash)
    {
        if ((modelId is null) != (modelHash is null))
        {
            throw new ArgumentException("Model ID and model hash must be supplied together.");
        }
    }

    private static void AddScopeParameters(
        SqliteCommand command,
        ProcessingRunId? processingRunId,
        ModelId? modelId,
        Sha256Digest? modelHash)
    {
        if (processingRunId is ProcessingRunId runId)
        {
            command.Parameters.AddWithValue("$processing_run_id", runId.ToString());
        }

        if (modelId is ModelId selectedModelId && modelHash is Sha256Digest selectedModelHash)
        {
            command.Parameters.AddWithValue("$model_id", selectedModelId.ToString());
            command.Parameters.AddWithValue("$model_hash", selectedModelHash.ToString());
        }
    }

    private static string StatePredicate(string state)
    {
        string normalized = string.IsNullOrWhiteSpace(state)
            ? CatalogueReviewStates.Unreviewed
            : state.Trim().ToLowerInvariant();
        return normalized switch
        {
            CatalogueReviewStates.Unreviewed => "latest_action.id IS NULL",
            CatalogueReviewStates.Assigned => "latest_action.action_kind = 'assign'",
            CatalogueReviewStates.Unknown => "latest_action.action_kind = 'unknown'",
            CatalogueReviewStates.Rejected => "latest_action.action_kind = 'reject'",
            "all" => "1 = 1",
            _ => throw new ArgumentException($"Unsupported review state '{state}'.", nameof(state)),
        };
    }

    private static string NormalizeSort(string sort)
    {
        string normalized = string.IsNullOrWhiteSpace(sort)
            ? CatalogueReviewSorts.CreatedDescending
            : sort.Trim().ToLowerInvariant();
        return normalized switch
        {
            CatalogueReviewSorts.CreatedDescending => normalized,
            _ => throw new ArgumentException($"Unsupported review sort '{sort}'.", nameof(sort)),
        };
    }

    private static string SortExpression(string sort) => NormalizeSort(sort) switch
    {
        CatalogueReviewSorts.CreatedDescending =>
            "face_occurrences.created_at_utc DESC, face_occurrences.id",
        _ => throw new ArgumentOutOfRangeException(nameof(sort)),
    };

    private static CatalogueReviewFace ReadFace(SqliteDataReader reader)
    {
        string sourceKey = reader.GetString(3).Replace('\\', '/');
        string photoName = Path.GetFileName(sourceKey);
        long? activeActionId = reader.IsDBNull(10) ? null : reader.GetInt64(10);
        string? actionKind = reader.IsDBNull(11) ? null : reader.GetString(11);
        PersonId? personId = reader.IsDBNull(12)
            ? null
            : PersonId.From(Guid.Parse(reader.GetString(12)));
        string? personName = reader.IsDBNull(13) ? null : reader.GetString(13);
        CatalogueReviewPerson? person = personId is PersonId id && personName is not null
            ? new CatalogueReviewPerson(id, personName)
            : null;
        string reviewState = actionKind switch
        {
            CatalogueReviewActionKinds.Assign => CatalogueReviewStates.Assigned,
            CatalogueReviewActionKinds.Unknown => CatalogueReviewStates.Unknown,
            CatalogueReviewActionKinds.Reject => CatalogueReviewStates.Rejected,
            _ => CatalogueReviewStates.Unreviewed,
        };

        return new CatalogueReviewFace(
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
            reader.GetInt32(1),
            Parse(reader.GetString(2)),
            string.IsNullOrWhiteSpace(photoName) ? "Photo" : photoName,
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            new Sha256Digest(reader.GetString(7)),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetDouble(9),
            reviewState,
            person,
            activeActionId,
            AssetRevisionId.From(Guid.Parse(reader.GetString(15))),
            reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
}
