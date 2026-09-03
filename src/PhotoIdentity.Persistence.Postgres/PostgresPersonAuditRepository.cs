using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL read-only audit view over active human assignments and exact-model suggestion disagreements.
/// </summary>
public sealed class PostgresPersonAuditRepository : IPersonAuditRepository
{
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
        latest_action.person_id,
        assigned_person.display_name,
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
        LEFT JOIN top_suggestion
            ON top_suggestion.face_occurrence_id = face.id
        """;

    private readonly PostgresCatalogueDatabase _database;

    public PostgresPersonAuditRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<PersonAuditPage?> GetFacesAsync(
        PersonId personId,
        ModelId? modelId = null,
        Sha256Digest? modelHash = null,
        int offset = 0,
        int limit = 40,
        bool disagreementsOnly = false,
        string sort = PersonAuditSorts.AssignedDescending,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Person audit page size must be between 1 and 200.");
        }

        ValidateModelScope(modelId, modelHash);
        if (disagreementsOnly && modelId is null)
        {
            throw new ArgumentException(
                "Disagreement filtering requires an exact suggestion model revision.",
                nameof(disagreementsOnly));
        }

        string normalizedSort = NormalizeSort(sort);
        if (normalizedSort == PersonAuditSorts.DisagreementFirst && modelId is null)
        {
            throw new ArgumentException(
                "Disagreement ordering requires an exact suggestion model revision.",
                nameof(sort));
        }

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);

        ReviewPerson? person = await GetPersonAsync(
            connection,
            personId,
            cancellationToken);
        if (person is null)
        {
            return null;
        }

        string predicate = BuildPredicate(disagreementsOnly);
        string orderBy = SortExpression(normalizedSort);

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
        AddParameters(command, personId, modelId, modelHash);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

        List<PersonAuditFace> items = [];
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
            WHERE {predicate};
            """;
        AddParameters(countCommand, personId, modelId, modelHash);
        int total = checked((int)Convert.ToInt64(
            await countCommand.ExecuteScalarAsync(cancellationToken)));

        int disagreementCount = 0;
        if (modelId is not null)
        {
            await using NpgsqlCommand disagreementCommand =
                connection.CreateCommand();
            disagreementCommand.CommandText = $"""
                {Ctes}
                SELECT COUNT(*)
                {From}
                WHERE latest_action.person_id = @person_id
                  AND top_suggestion.suggestion_id IS NOT NULL
                  AND top_suggestion.suggested_person_id <> latest_action.person_id;
                """;
            AddParameters(
                disagreementCommand,
                personId,
                modelId,
                modelHash);
            disagreementCount = checked((int)Convert.ToInt64(
                await disagreementCommand.ExecuteScalarAsync(
                    cancellationToken)));
        }

        return new PersonAuditPage(
            person,
            items,
            offset,
            limit,
            total,
            disagreementCount,
            normalizedSort);
    }

    private static async Task<ReviewPerson?> GetPersonAsync(
        NpgsqlConnection connection,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, display_name
            FROM people
            WHERE id = @person_id
              AND merged_into_person_id IS NULL
              AND display_name IS NOT NULL
              AND BTRIM(display_name) <> '';
            """;
        command.Parameters.AddWithValue(
            "person_id",
            Guid.Parse(personId.ToString()));

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ReviewPerson(
                PersonId.From(reader.GetGuid(0)),
                reader.GetString(1))
            : null;
    }

    private static string BuildPredicate(bool disagreementsOnly) =>
        disagreementsOnly
            ? """
              latest_action.person_id = @person_id
              AND top_suggestion.suggestion_id IS NOT NULL
              AND top_suggestion.suggested_person_id <> latest_action.person_id
              """
            : "latest_action.person_id = @person_id";

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
        PersonId personId,
        ModelId? modelId,
        Sha256Digest? modelHash)
    {
        bool hasModel =
            modelId is ModelId && modelHash is Sha256Digest;
        command.Parameters.AddWithValue(
            "person_id",
            Guid.Parse(personId.ToString()));
        command.Parameters.AddWithValue("has_model", hasModel);
        command.Parameters.AddWithValue(
            "model_id",
            hasModel ? modelId!.Value.ToString() : string.Empty);
        command.Parameters.AddWithValue(
            "model_hash",
            hasModel ? modelHash!.Value.ToString() : string.Empty);
    }

    private static string NormalizeSort(string sort)
    {
        string normalized = string.IsNullOrWhiteSpace(sort)
            ? PersonAuditSorts.AssignedDescending
            : sort.Trim().ToLowerInvariant();
        return normalized switch
        {
            PersonAuditSorts.AssignedDescending => normalized,
            PersonAuditSorts.AssignedAscending => normalized,
            PersonAuditSorts.DisagreementFirst => normalized,
            PersonAuditSorts.ConfidenceAscending => normalized,
            _ => throw new ArgumentException(
                $"Unsupported person audit sort '{sort}'.",
                nameof(sort)),
        };
    }

    private static string SortExpression(string sort) =>
        NormalizeSort(sort) switch
        {
            PersonAuditSorts.AssignedDescending =>
                "latest_action.created_at_utc DESC, face.id",
            PersonAuditSorts.AssignedAscending =>
                "latest_action.created_at_utc, face.id",
            PersonAuditSorts.DisagreementFirst =>
                "CASE WHEN top_suggestion.suggestion_id IS NOT NULL " +
                "AND top_suggestion.suggested_person_id <> latest_action.person_id " +
                "THEN 0 ELSE 1 END, " +
                "latest_action.created_at_utc DESC, face.id",
            PersonAuditSorts.ConfidenceAscending =>
                "CASE WHEN latest_observation.confidence IS NULL THEN 1 ELSE 0 END, " +
                "latest_observation.confidence, latest_action.created_at_utc DESC, face.id",
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };

    private static PersonAuditFace ReadFace(NpgsqlDataReader reader)
    {
        string sourceKey = reader.GetString(3).Replace('\\', '/');
        string photoName = Path.GetFileName(sourceKey);
        PersonId assignedPersonId = PersonId.From(reader.GetGuid(12));
        ReviewPerson assignedPerson = new(
            assignedPersonId,
            reader.GetString(13));

        PersonAuditTopSuggestion? topSuggestion = reader.IsDBNull(14)
            ? null
            : new PersonAuditTopSuggestion(
                reader.GetInt64(14),
                new ReviewPerson(
                    PersonId.From(reader.GetGuid(15)),
                    reader.GetString(16)),
                new ModelId(reader.GetString(17)),
                new Sha256Digest(reader.GetString(18)),
                reader.GetInt32(19),
                reader.GetDouble(20),
                reader.IsDBNull(21) ? null : reader.GetDouble(21),
                reader.GetString(22),
                reader.GetFieldValue<DateTimeOffset>(23));

        bool disagrees =
            topSuggestion is not null &&
            topSuggestion.Person.Id != assignedPersonId;

        return new PersonAuditFace(
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
            assignedPerson,
            topSuggestion,
            disagrees);
    }
}
