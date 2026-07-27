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
    private const string ReviewFaceSelect = """
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
        )
        SELECT
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
            people.display_name
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
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Review page size must be between 1 and 200.");
        }

        if ((modelId is null) != (modelHash is null))
        {
            throw new ArgumentException("Model ID and model hash must be supplied together.");
        }

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

        string predicate = string.Join(" AND ", predicates.Select(value => $"({value})"));
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {ReviewFaceSelect}
            WHERE {predicate}
            ORDER BY face_occurrences.created_at_utc DESC, face_occurrences.id
            LIMIT $limit OFFSET $offset;
            """;
        AddParameters(command, offset, limit, processingRunId, modelId, modelHash);

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
            SELECT COUNT(*)
            FROM (
                {ReviewFaceSelect}
                WHERE {predicate}
            );
            """;
        AddParameters(countCommand, offset, limit, processingRunId, modelId, modelHash);
        object? count = await countCommand.ExecuteScalarAsync(cancellationToken);
        return new CatalogueReviewFacePage(
            items,
            offset,
            limit,
            Convert.ToInt32(count, CultureInfo.InvariantCulture));
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

    private static void AddParameters(
        SqliteCommand command,
        int offset,
        int limit,
        ProcessingRunId? processingRunId,
        ModelId? modelId,
        Sha256Digest? modelHash)
    {
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
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
            CatalogueReviewStates.Rejected => "latest_action.action_kind = 'reject'",
            "all" => "1 = 1",
            _ => throw new ArgumentException($"Unsupported review state '{state}'.", nameof(state)),
        };
    }

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
            activeActionId);
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
}
