using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Provides paged, read-only identity audit views for active assignments.
/// Optional suggestion comparisons are scoped to one exact model revision.
/// </summary>
public sealed class SqlitePersonAuditRepository
{
    private const string Ctes = """
        WITH latest_action AS (
            SELECT
                review_actions.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY id DESC) AS row_number
            FROM review_actions
            WHERE action_kind IN ('assign', 'reject')
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
              AND suggestions.status IN ('pending', 'accepted')
              AND $has_model = 1
              AND rankings.model_id = $model_id
              AND rankings.model_hash = $model_hash
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
        latest_action.created_at_utc,
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
        top_suggestion.generated_at_utc
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
        INNER JOIN latest_action
            ON latest_action.face_occurrence_id = face_occurrences.id
           AND latest_action.row_number = 1
           AND latest_action.action_kind = 'assign'
        INNER JOIN people AS assigned_people
            ON assigned_people.id = latest_action.person_id
           AND assigned_people.merged_into_person_id IS NULL
        LEFT JOIN top_suggestion
            ON top_suggestion.face_occurrence_id = face_occurrences.id
        """;

    private readonly SqliteCatalogueDatabase _database;

    public SqlitePersonAuditRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CataloguePersonAuditPage?> GetFacesAsync(
        PersonId personId,
        ModelId? modelId = null,
        Sha256Digest? modelHash = null,
        int offset = 0,
        int limit = 40,
        bool disagreementsOnly = false,
        string sort = CataloguePersonAuditSorts.AssignedDescending,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Person audit page size must be between 1 and 200.");
        }

        ValidateModelScope(modelId, modelHash);
        if (disagreementsOnly && modelId is null)
        {
            throw new ArgumentException(
                "Disagreement filtering requires an exact suggestion model revision.",
                nameof(disagreementsOnly));
        }

        string normalizedSort = NormalizeSort(sort);
        if (normalizedSort == CataloguePersonAuditSorts.DisagreementFirst && modelId is null)
        {
            throw new ArgumentException(
                "Disagreement ordering requires an exact suggestion model revision.",
                nameof(sort));
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        CatalogueReviewPerson? person = await GetPersonAsync(connection, personId, cancellationToken);
        if (person is null)
        {
            return null;
        }

        string predicate = BuildPredicate(disagreementsOnly);
        string orderBy = SortExpression(normalizedSort);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {Ctes}
            SELECT
                {Columns}
            {From}
            WHERE {predicate}
            ORDER BY {orderBy}
            LIMIT $limit OFFSET $offset;
            """;
        AddParameters(command, personId, modelId, modelHash);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        List<CataloguePersonAuditFace> items = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(ReadFace(reader));
            }
        }

        using SqliteCommand countCommand = connection.CreateCommand();
        countCommand.CommandText = $"""
            {Ctes}
            SELECT COUNT(*)
            {From}
            WHERE {predicate};
            """;
        AddParameters(countCommand, personId, modelId, modelHash);
        object? count = await countCommand.ExecuteScalarAsync(cancellationToken);

        int disagreementCount = 0;
        if (modelId is not null)
        {
            using SqliteCommand disagreementCommand = connection.CreateCommand();
            disagreementCommand.CommandText = $"""
                {Ctes}
                SELECT COUNT(*)
                {From}
                WHERE latest_action.person_id = $person_id
                  AND top_suggestion.suggestion_id IS NOT NULL
                  AND top_suggestion.suggested_person_id <> latest_action.person_id;
                """;
            AddParameters(disagreementCommand, personId, modelId, modelHash);
            object? disagreements = await disagreementCommand.ExecuteScalarAsync(cancellationToken);
            disagreementCount = Convert.ToInt32(disagreements, CultureInfo.InvariantCulture);
        }

        return new CataloguePersonAuditPage(
            person,
            items,
            offset,
            limit,
            Convert.ToInt32(count, CultureInfo.InvariantCulture),
            disagreementCount,
            normalizedSort);
    }

    private static async Task<CatalogueReviewPerson?> GetPersonAsync(
        SqliteConnection connection,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, display_name
            FROM people
            WHERE id = $person_id
              AND merged_into_person_id IS NULL
              AND display_name IS NOT NULL
              AND TRIM(display_name) <> '';
            """;
        command.Parameters.AddWithValue("$person_id", personId.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CatalogueReviewPerson(
                PersonId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1))
            : null;
    }

    private static string BuildPredicate(bool disagreementsOnly) =>
        disagreementsOnly
            ? """
                latest_action.person_id = $person_id
                AND top_suggestion.suggestion_id IS NOT NULL
                AND top_suggestion.suggested_person_id <> latest_action.person_id
                """
            : "latest_action.person_id = $person_id";

    private static void ValidateModelScope(ModelId? modelId, Sha256Digest? modelHash)
    {
        if ((modelId is null) != (modelHash is null))
        {
            throw new ArgumentException("Model ID and model hash must be supplied together.");
        }
    }

    private static void AddParameters(
        SqliteCommand command,
        PersonId personId,
        ModelId? modelId,
        Sha256Digest? modelHash)
    {
        bool hasModel = modelId is ModelId && modelHash is Sha256Digest;
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        command.Parameters.AddWithValue("$has_model", hasModel ? 1 : 0);
        command.Parameters.AddWithValue("$model_id", hasModel ? modelId!.Value.ToString() : "");
        command.Parameters.AddWithValue("$model_hash", hasModel ? modelHash!.Value.ToString() : "");
    }

    private static string NormalizeSort(string sort)
    {
        string normalized = string.IsNullOrWhiteSpace(sort)
            ? CataloguePersonAuditSorts.AssignedDescending
            : sort.Trim().ToLowerInvariant();
        return normalized switch
        {
            CataloguePersonAuditSorts.AssignedDescending => normalized,
            CataloguePersonAuditSorts.AssignedAscending => normalized,
            CataloguePersonAuditSorts.DisagreementFirst => normalized,
            CataloguePersonAuditSorts.ConfidenceAscending => normalized,
            _ => throw new ArgumentException($"Unsupported person audit sort '{sort}'.", nameof(sort)),
        };
    }

    private static string SortExpression(string sort) => NormalizeSort(sort) switch
    {
        CataloguePersonAuditSorts.AssignedDescending =>
            "latest_action.created_at_utc DESC, face_occurrences.id",
        CataloguePersonAuditSorts.AssignedAscending =>
            "latest_action.created_at_utc, face_occurrences.id",
        CataloguePersonAuditSorts.DisagreementFirst =>
            "CASE WHEN top_suggestion.suggestion_id IS NOT NULL " +
            "AND top_suggestion.suggested_person_id <> latest_action.person_id THEN 0 ELSE 1 END, " +
            "latest_action.created_at_utc DESC, face_occurrences.id",
        CataloguePersonAuditSorts.ConfidenceAscending =>
            "CASE WHEN latest_observation.confidence IS NULL THEN 1 ELSE 0 END, " +
            "latest_observation.confidence, latest_action.created_at_utc DESC, face_occurrences.id",
        _ => throw new ArgumentOutOfRangeException(nameof(sort)),
    };

    private static CataloguePersonAuditFace ReadFace(SqliteDataReader reader)
    {
        string sourceKey = reader.GetString(3).Replace('\\', '/');
        string photoName = Path.GetFileName(sourceKey);
        PersonId assignedPersonId = PersonId.From(Guid.Parse(reader.GetString(12)));
        CatalogueReviewPerson assignedPerson = new(assignedPersonId, reader.GetString(13));
        CatalogueSuggestionGalleryTopSuggestion? topSuggestion = reader.IsDBNull(14)
            ? null
            : new CatalogueSuggestionGalleryTopSuggestion(
                reader.GetInt64(14),
                new CatalogueReviewPerson(
                    PersonId.From(Guid.Parse(reader.GetString(15))),
                    reader.GetString(16)),
                new ModelId(reader.GetString(17)),
                new Sha256Digest(reader.GetString(18)),
                reader.GetInt32(19),
                reader.GetDouble(20),
                reader.IsDBNull(21) ? null : reader.GetDouble(21),
                reader.GetString(22),
                Parse(reader.GetString(23)));
        bool disagrees = topSuggestion is not null && topSuggestion.Person.Id != assignedPersonId;

        return new CataloguePersonAuditFace(
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
            reader.GetInt32(1),
            Parse(reader.GetString(2)),
            Parse(reader.GetString(11)),
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

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
}
