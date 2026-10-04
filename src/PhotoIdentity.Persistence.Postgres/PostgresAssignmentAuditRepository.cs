using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresAssignmentAuditRepository : IAssignmentAuditRepository
{
    private const string AutomaticActor = "identity-matcher:auto";
    private const string MultiEvidenceAutomaticActor = "identity-matcher:auto-multi-evidence";

    private const string Ctes = """
        WITH latest_action AS (
            SELECT
                action.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY id DESC) AS row_number
            FROM review_actions AS action
            WHERE action_kind IN ('assign', 'unknown', 'reject')
              AND reversed_at_utc IS NULL
        ),
        latest_crop AS (
            SELECT
                crop.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY created_at_utc DESC, id DESC) AS row_number
            FROM face_crops AS crop
        ),
        latest_observation AS (
            SELECT
                observation.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY observed_at_utc DESC, detector_model_id, detector_model_hash) AS row_number
            FROM face_observations AS observation
        ),
        accepted_suggestion AS (
            SELECT
                decision.review_action_id,
                suggestion.id AS suggestion_id,
                suggestion.suggested_person_id,
                suggested_person.display_name,
                ranking.model_id,
                ranking.model_hash,
                ranking.rank,
                suggestion.score,
                ranking.score_margin,
                suggestion.status,
                ranking.generated_at_utc,
                ROW_NUMBER() OVER (
                    PARTITION BY decision.review_action_id
                    ORDER BY decision.id DESC, ranking.rank, suggestion.id DESC) AS row_number
            FROM identity_suggestion_review_actions AS decision
            INNER JOIN identity_suggestions AS suggestion
                ON suggestion.id = decision.suggestion_id
            INNER JOIN identity_suggestion_rankings AS ranking
                ON ranking.suggestion_id = suggestion.id
            INNER JOIN people AS suggested_person
                ON suggested_person.id = suggestion.suggested_person_id
            WHERE decision.action_kind = 'accept'
              AND decision.review_action_id IS NOT NULL
        ),
        top_suggestion AS (
            SELECT
                ranking.face_occurrence_id,
                suggestion.id AS suggestion_id,
                suggestion.suggested_person_id,
                suggested_person.display_name,
                ranking.model_id,
                ranking.model_hash,
                ranking.rank,
                suggestion.score,
                ranking.score_margin,
                suggestion.status,
                ranking.generated_at_utc
            FROM identity_suggestion_rankings AS ranking
            INNER JOIN identity_suggestions AS suggestion
                ON suggestion.id = ranking.suggestion_id
            INNER JOIN people AS suggested_person
                ON suggested_person.id = suggestion.suggested_person_id
               AND suggested_person.merged_into_person_id IS NULL
            WHERE ranking.rank = 1
              AND suggestion.status IN ('pending', 'accepted')
              AND @has_model = TRUE
              AND ranking.model_id = @model_id
              AND ranking.model_hash = @model_hash
        )
        """;

    private const string Columns = """
        face.id,
        face.ordinal,
        face.created_at_utc,
        asset.source_key,
        COALESCE(revision.media_type, 'application/octet-stream'),
        revision.width,
        revision.height,
        revision.content_sha256,
        latest_crop.storage_path,
        latest_observation.confidence,
        latest_action.id,
        latest_action.created_at_utc,
        latest_action.actor,
        latest_action.person_id,
        assigned_person.display_name,
        accepted_suggestion.suggestion_id,
        accepted_suggestion.suggested_person_id,
        accepted_suggestion.display_name,
        accepted_suggestion.model_id,
        accepted_suggestion.model_hash,
        accepted_suggestion.rank,
        accepted_suggestion.score,
        accepted_suggestion.score_margin,
        accepted_suggestion.status,
        accepted_suggestion.generated_at_utc,
        top_suggestion.suggestion_id,
        top_suggestion.suggested_person_id,
        top_suggestion.display_name,
        top_suggestion.model_id,
        top_suggestion.model_hash,
        top_suggestion.rank,
        top_suggestion.score,
        top_suggestion.score_margin,
        top_suggestion.status,
        top_suggestion.generated_at_utc
        """;

    private const string From = """
        FROM face_occurrences AS face
        INNER JOIN asset_revisions AS revision
            ON revision.id = face.asset_revision_id
        INNER JOIN assets AS asset
            ON asset.id = revision.asset_id
        LEFT JOIN latest_crop
            ON latest_crop.face_occurrence_id = face.id
           AND latest_crop.row_number = 1
        LEFT JOIN latest_observation
            ON latest_observation.face_occurrence_id = face.id
           AND latest_observation.row_number = 1
        INNER JOIN latest_action
            ON latest_action.face_occurrence_id = face.id
           AND latest_action.row_number = 1
           AND latest_action.action_kind = 'assign'
        INNER JOIN people AS assigned_person
            ON assigned_person.id = latest_action.person_id
           AND assigned_person.merged_into_person_id IS NULL
        LEFT JOIN accepted_suggestion
            ON accepted_suggestion.review_action_id = latest_action.id
           AND accepted_suggestion.row_number = 1
        LEFT JOIN top_suggestion
            ON top_suggestion.face_occurrence_id = face.id
        """;

    private const string Predicate = """
        (@source = 'all'
         OR (@source = 'automatic' AND latest_action.actor IN ('identity-matcher:auto', 'identity-matcher:auto-multi-evidence'))
         OR (@source = 'manual' AND latest_action.actor NOT IN ('identity-matcher:auto', 'identity-matcher:auto-multi-evidence')))
        AND (@from_utc IS NULL OR latest_action.created_at_utc >= @from_utc)
        AND (@to_utc IS NULL OR latest_action.created_at_utc < @to_utc)
        """;

    private const string OrderBy = """
        assigned_person.display_name,
        assigned_person.id,
        CASE WHEN accepted_suggestion.score_margin IS NULL THEN 1 ELSE 0 END,
        accepted_suggestion.score_margin,
        latest_action.created_at_utc DESC,
        face.id
        """;

    private readonly PostgresCatalogueDatabase _database;

    public PostgresAssignmentAuditRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<AssignmentAuditPage> GetAssignmentsAsync(
        string source = AssignmentAuditSources.Automatic,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        ModelId? modelId = null,
        Sha256Digest? modelHash = null,
        int offset = 0,
        int limit = 120,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Assignment audit page size must be between 1 and 200.");
        }

        ValidateModelScope(modelId, modelHash);
        DateTimeOffset? normalizedFrom = fromUtc?.ToUniversalTime();
        DateTimeOffset? normalizedTo = toUtc?.ToUniversalTime();
        if (normalizedFrom is DateTimeOffset from &&
            normalizedTo is DateTimeOffset to &&
            from >= to)
        {
            throw new ArgumentException("The audit From time must be earlier than the To time.");
        }

        string normalizedSource = NormalizeSource(source);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {Ctes}
            SELECT
                {Columns}
            {From}
            WHERE {Predicate}
            ORDER BY {OrderBy}
            LIMIT @limit OFFSET @offset;
            """;
        AddParameters(
            command,
            normalizedSource,
            normalizedFrom,
            normalizedTo,
            modelId,
            modelHash);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

        List<AssignmentAuditFace> items = [];
        await using (NpgsqlDataReader reader =
                     await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(ReadFace(reader));
            }
        }

        await using NpgsqlCommand countCommand = connection.CreateCommand();
        countCommand.CommandText = $"""
            {Ctes}
            SELECT COUNT(*)
            {From}
            WHERE {Predicate};
            """;
        AddParameters(
            countCommand,
            normalizedSource,
            normalizedFrom,
            normalizedTo,
            modelId,
            modelHash);
        int total = checked((int)Convert.ToInt64(
            await countCommand.ExecuteScalarAsync(cancellationToken)));

        return new AssignmentAuditPage(
            items,
            offset,
            limit,
            total,
            normalizedSource,
            normalizedFrom,
            normalizedTo);
    }

    private static string NormalizeSource(string source)
    {
        string normalized = string.IsNullOrWhiteSpace(source)
            ? AssignmentAuditSources.Automatic
            : source.Trim().ToLowerInvariant();
        return normalized switch
        {
            AssignmentAuditSources.All => normalized,
            AssignmentAuditSources.Automatic => normalized,
            AssignmentAuditSources.Manual => normalized,
            _ => throw new ArgumentException(
                $"Unsupported assignment audit source '{source}'.",
                nameof(source)),
        };
    }

    private static void ValidateModelScope(
        ModelId? modelId,
        Sha256Digest? modelHash)
    {
        if ((modelId is null) != (modelHash is null))
        {
            throw new ArgumentException(
                "Model ID and model hash must be supplied together.");
        }
    }

    private static void AddParameters(
        NpgsqlCommand command,
        string source,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        ModelId? modelId,
        Sha256Digest? modelHash)
    {
        command.Parameters.AddWithValue("source", source);
        AddNullableTimestamp(command, "from_utc", fromUtc);
        AddNullableTimestamp(command, "to_utc", toUtc);

        bool hasModel = modelId is ModelId && modelHash is Sha256Digest;
        command.Parameters.AddWithValue("has_model", hasModel);
        command.Parameters.AddWithValue(
            "model_id",
            hasModel ? modelId!.Value.ToString() : string.Empty);
        command.Parameters.AddWithValue(
            "model_hash",
            hasModel ? modelHash!.Value.ToString() : string.Empty);
    }

    private static void AddNullableTimestamp(
        NpgsqlCommand command,
        string name,
        DateTimeOffset? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value is DateTimeOffset timestamp
            ? timestamp.ToUniversalTime()
            : DBNull.Value;
    }

    private static AssignmentAuditFace ReadFace(NpgsqlDataReader reader)
    {
        string sourceKey = reader.GetString(3).Replace('\\', '/');
        string photoName = Path.GetFileName(sourceKey);
        PersonId assignedPersonId = PersonId.From(reader.GetGuid(13));
        ReviewPerson assignedPerson = new(
            assignedPersonId,
            reader.GetString(14));

        PersonAuditTopSuggestion? acceptedSuggestion = ReadSuggestion(reader, 15);
        PersonAuditTopSuggestion? currentTopSuggestion = ReadSuggestion(reader, 25);
        bool currentSuggestionDisagrees =
            currentTopSuggestion is not null &&
            currentTopSuggestion.Person.Id != assignedPersonId;

        return new AssignmentAuditFace(
            FaceOccurrenceId.From(reader.GetGuid(0)),
            reader.GetInt32(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(11),
            string.IsNullOrWhiteSpace(photoName) ? "Photo" : photoName,
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            new Sha256Digest(reader.GetString(7)),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetDouble(9),
            reader.GetInt64(10),
            reader.GetString(12),
            assignedPerson,
            acceptedSuggestion,
            currentTopSuggestion,
            currentSuggestionDisagrees);
    }

    private static PersonAuditTopSuggestion? ReadSuggestion(
        NpgsqlDataReader reader,
        int startIndex)
    {
        if (reader.IsDBNull(startIndex))
        {
            return null;
        }

        return new PersonAuditTopSuggestion(
            reader.GetInt64(startIndex),
            new ReviewPerson(
                PersonId.From(reader.GetGuid(startIndex + 1)),
                reader.GetString(startIndex + 2)),
            new ModelId(reader.GetString(startIndex + 3)),
            new Sha256Digest(reader.GetString(startIndex + 4)),
            reader.GetInt32(startIndex + 5),
            reader.GetDouble(startIndex + 6),
            reader.IsDBNull(startIndex + 7) ? null : reader.GetDouble(startIndex + 7),
            reader.GetString(startIndex + 8),
            reader.GetFieldValue<DateTimeOffset>(startIndex + 9));
    }
}
