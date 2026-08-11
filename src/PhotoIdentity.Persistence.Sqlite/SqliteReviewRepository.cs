using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Provides review-oriented queries and append-only, reversible human actions.
/// Current face state is derived from the newest unreversed assignment, Unknown decision or rejection.
/// </summary>
public sealed class SqliteReviewRepository
{
    private const string ReviewFaceSelect = """
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

    public SqliteReviewRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueReviewFacePage> GetFacesAsync(
        int offset = 0,
        int limit = 40,
        string state = CatalogueReviewStates.Unreviewed,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Review page size must be between 1 and 200.");
        }

        string normalizedState = NormalizeState(state);
        string predicate = normalizedState switch
        {
            CatalogueReviewStates.Unreviewed => "latest_action.id IS NULL",
            CatalogueReviewStates.Assigned => "latest_action.action_kind = 'assign'",
            CatalogueReviewStates.Unknown => "latest_action.action_kind = 'unknown'",
            CatalogueReviewStates.Rejected => "latest_action.action_kind = 'reject'",
            "all" => "1 = 1",
            _ => throw new ArgumentException($"Unsupported review state '{state}'.", nameof(state)),
        };

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {ReviewFaceSelect}
            WHERE {predicate}
            ORDER BY face_occurrences.created_at_utc DESC, face_occurrences.id
            LIMIT $limit OFFSET $offset;
            """;
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
            SELECT COUNT(*)
            FROM (
                {ReviewFaceSelect}
                WHERE {predicate}
            );
            """;
        object? count = await countCommand.ExecuteScalarAsync(cancellationToken);
        int total = Convert.ToInt32(count, CultureInfo.InvariantCulture);
        return new CatalogueReviewFacePage(items, offset, limit, total);
    }

    public async Task<CatalogueReviewFace?> GetFaceAsync(
        FaceOccurrenceId id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {ReviewFaceSelect}
            WHERE face_occurrences.id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadFace(reader) : null;
    }

    public async Task<IReadOnlyList<CatalogueReviewPerson>> GetPeopleAsync(
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, display_name
            FROM people
            WHERE merged_into_person_id IS NULL
              AND display_name IS NOT NULL
            ORDER BY display_name COLLATE NOCASE, id;
            """;

        List<CatalogueReviewPerson> people = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            people.Add(new CatalogueReviewPerson(
                PersonId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1)));
        }

        return people;
    }

    public async Task<CatalogueReviewPerson> CreatePersonAsync(
        string displayName,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string normalizedName = displayName.Trim();
        if (normalizedName.Length > 200)
        {
            throw new ArgumentException("A person display name cannot exceed 200 characters.", nameof(displayName));
        }

        PersonId id = PersonId.New();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO people (id, display_name, created_at_utc, merged_into_person_id)
            VALUES ($id, $display_name, $created_at_utc, NULL);
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.Parameters.AddWithValue("$display_name", normalizedName);
        command.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new CatalogueReviewPerson(id, normalizedName);
    }

    public async Task<CatalogueReviewAction> AssignAsync(
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await RequireOccurrenceAsync(connection, transaction, faceOccurrenceId, cancellationToken);
        string displayName = await RequirePersonAsync(connection, transaction, personId, cancellationToken);

        using (SqliteCommand labelCommand = connection.CreateCommand())
        {
            labelCommand.Transaction = transaction;
            labelCommand.CommandText = """
                INSERT INTO person_labels (
                    person_id,
                    face_occurrence_id,
                    label_kind,
                    assigned_by,
                    assigned_at_utc,
                    note)
                VALUES (
                    $person_id,
                    $face_occurrence_id,
                    'manual',
                    $assigned_by,
                    $assigned_at_utc,
                    $note)
                ON CONFLICT(person_id, face_occurrence_id, label_kind) DO UPDATE SET
                    assigned_by = excluded.assigned_by,
                    assigned_at_utc = excluded.assigned_at_utc,
                    note = excluded.note;
                """;
            labelCommand.Parameters.AddWithValue("$person_id", personId.ToString());
            labelCommand.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
            labelCommand.Parameters.AddWithValue("$assigned_by", normalizedActor);
            labelCommand.Parameters.AddWithValue("$assigned_at_utc", Format(createdAtUtc));
            labelCommand.Parameters.AddWithValue("$note", (object?)normalizedNote ?? DBNull.Value);
            await labelCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        long labelId;
        using (SqliteCommand labelIdCommand = connection.CreateCommand())
        {
            labelIdCommand.Transaction = transaction;
            labelIdCommand.CommandText = """
                SELECT id
                FROM person_labels
                WHERE person_id = $person_id
                  AND face_occurrence_id = $face_occurrence_id
                  AND label_kind = 'manual';
                """;
            labelIdCommand.Parameters.AddWithValue("$person_id", personId.ToString());
            labelIdCommand.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
            object? value = await labelIdCommand.ExecuteScalarAsync(cancellationToken);
            labelId = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        long actionId = await InsertActionAsync(
            connection,
            transaction,
            faceOccurrenceId,
            CatalogueReviewActionKinds.Assign,
            personId,
            labelId,
            normalizedActor,
            normalizedNote,
            createdAtUtc,
            reversesActionId: null,
            cancellationToken);
        transaction.Commit();

        return new CatalogueReviewAction(
            actionId,
            faceOccurrenceId,
            CatalogueReviewActionKinds.Assign,
            personId,
            displayName,
            labelId,
            normalizedActor,
            normalizedNote,
            createdAtUtc.ToUniversalTime(),
            ReversedAtUtc: null,
            ReversesActionId: null);
    }

    public Task<CatalogueReviewAction> MarkUnknownAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default) =>
        RecordPersonlessDecisionAsync(
            faceOccurrenceId,
            CatalogueReviewActionKinds.Unknown,
            actor,
            createdAtUtc,
            note,
            cancellationToken);

    public Task<CatalogueReviewAction> RejectAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default) =>
        RecordPersonlessDecisionAsync(
            faceOccurrenceId,
            CatalogueReviewActionKinds.Reject,
            actor,
            createdAtUtc,
            note,
            cancellationToken);

    public async Task<CatalogueReviewAction?> UndoLatestAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await RequireOccurrenceAsync(connection, transaction, faceOccurrenceId, cancellationToken);
        CatalogueReviewAction? latest = await GetLatestActiveActionAsync(
            connection,
            transaction,
            faceOccurrenceId,
            cancellationToken);
        if (latest is null)
        {
            transaction.Commit();
            return null;
        }

        using (SqliteCommand reverseCommand = connection.CreateCommand())
        {
            reverseCommand.Transaction = transaction;
            reverseCommand.CommandText = """
                UPDATE review_actions
                SET reversed_at_utc = $reversed_at_utc
                WHERE id = $id AND reversed_at_utc IS NULL;
                """;
            reverseCommand.Parameters.AddWithValue("$reversed_at_utc", Format(createdAtUtc));
            reverseCommand.Parameters.AddWithValue("$id", latest.Id);
            int updated = await reverseCommand.ExecuteNonQueryAsync(cancellationToken);
            if (updated != 1)
            {
                throw new InvalidOperationException("The review action was changed before it could be undone.");
            }
        }

        using (SqliteCommand restoreSuggestionCommand = connection.CreateCommand())
        {
            restoreSuggestionCommand.Transaction = transaction;
            restoreSuggestionCommand.CommandText = """
                UPDATE identity_suggestions
                SET status = 'pending'
                WHERE status = 'accepted'
                  AND id IN (
                    SELECT suggestion_id
                    FROM identity_suggestion_review_actions
                    WHERE action_kind = 'accept'
                      AND review_action_id = $review_action_id);
                """;
            restoreSuggestionCommand.Parameters.AddWithValue("$review_action_id", latest.Id);
            await restoreSuggestionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        long undoId = await InsertActionAsync(
            connection,
            transaction,
            faceOccurrenceId,
            CatalogueReviewActionKinds.Undo,
            latest.PersonId,
            latest.PersonLabelId,
            normalizedActor,
            normalizedNote,
            createdAtUtc,
            latest.Id,
            cancellationToken);
        transaction.Commit();

        return new CatalogueReviewAction(
            undoId,
            faceOccurrenceId,
            CatalogueReviewActionKinds.Undo,
            latest.PersonId,
            latest.PersonDisplayName,
            latest.PersonLabelId,
            normalizedActor,
            normalizedNote,
            createdAtUtc.ToUniversalTime(),
            ReversedAtUtc: null,
            ReversesActionId: latest.Id);
    }

    public async Task<IReadOnlyList<CatalogueReviewAction>> GetActionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                review_actions.id,
                review_actions.face_occurrence_id,
                review_actions.action_kind,
                review_actions.person_id,
                people.display_name,
                review_actions.person_label_id,
                review_actions.actor,
                review_actions.note,
                review_actions.created_at_utc,
                review_actions.reversed_at_utc,
                review_actions.reverses_action_id
            FROM review_actions
            LEFT JOIN people ON people.id = review_actions.person_id
            WHERE review_actions.face_occurrence_id = $face_occurrence_id
            ORDER BY review_actions.id DESC;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());

        List<CatalogueReviewAction> actions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            actions.Add(ReadAction(reader));
        }

        return actions;
    }

    private async Task<CatalogueReviewAction> RecordPersonlessDecisionAsync(
        FaceOccurrenceId faceOccurrenceId,
        string kind,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note,
        CancellationToken cancellationToken)
    {
        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await RequireOccurrenceAsync(connection, transaction, faceOccurrenceId, cancellationToken);
        long actionId = await InsertActionAsync(
            connection,
            transaction,
            faceOccurrenceId,
            kind,
            personId: null,
            personLabelId: null,
            normalizedActor,
            normalizedNote,
            createdAtUtc,
            reversesActionId: null,
            cancellationToken);
        transaction.Commit();

        return new CatalogueReviewAction(
            actionId,
            faceOccurrenceId,
            kind,
            PersonId: null,
            PersonDisplayName: null,
            PersonLabelId: null,
            normalizedActor,
            normalizedNote,
            createdAtUtc.ToUniversalTime(),
            ReversedAtUtc: null,
            ReversesActionId: null);
    }

    private static async Task RequireOccurrenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId id,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM face_occurrences WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new KeyNotFoundException($"Face occurrence {id} was not found.");
        }
    }

    private static async Task<string> RequirePersonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersonId id,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT display_name
            FROM people
            WHERE id = $id AND merged_into_person_id IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string displayName && !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : throw new KeyNotFoundException($"Person {id} was not found or is not active.");
    }

    private static async Task<long> InsertActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        string kind,
        PersonId? personId,
        long? personLabelId,
        string actor,
        string? note,
        DateTimeOffset createdAtUtc,
        long? reversesActionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO review_actions (
                face_occurrence_id,
                action_kind,
                person_id,
                person_label_id,
                actor,
                note,
                created_at_utc,
                reversed_at_utc,
                reverses_action_id)
            VALUES (
                $face_occurrence_id,
                $action_kind,
                $person_id,
                $person_label_id,
                $actor,
                $note,
                $created_at_utc,
                NULL,
                $reverses_action_id);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$action_kind", kind);
        command.Parameters.AddWithValue("$person_id", personId is PersonId person ? person.ToString() : DBNull.Value);
        command.Parameters.AddWithValue("$person_label_id", (object?)personLabelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$actor", actor);
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
        command.Parameters.AddWithValue("$reverses_action_id", (object?)reversesActionId ?? DBNull.Value);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<CatalogueReviewAction?> GetLatestActiveActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                review_actions.id,
                review_actions.face_occurrence_id,
                review_actions.action_kind,
                review_actions.person_id,
                people.display_name,
                review_actions.person_label_id,
                review_actions.actor,
                review_actions.note,
                review_actions.created_at_utc,
                review_actions.reversed_at_utc,
                review_actions.reverses_action_id
            FROM review_actions
            LEFT JOIN people ON people.id = review_actions.person_id
            WHERE review_actions.face_occurrence_id = $face_occurrence_id
              AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
              AND review_actions.reversed_at_utc IS NULL
            ORDER BY review_actions.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAction(reader) : null;
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
        string state = actionKind switch
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
            state,
            person,
            activeActionId);
    }

    private static CatalogueReviewAction ReadAction(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : PersonId.From(Guid.Parse(reader.GetString(3))),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetInt64(5),
        reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        Parse(reader.GetString(8)),
        reader.IsDBNull(9) ? null : Parse(reader.GetString(9)),
        reader.IsDBNull(10) ? null : reader.GetInt64(10));

    private static string NormalizeState(string value) =>
        string.IsNullOrWhiteSpace(value) ? CatalogueReviewStates.Unreviewed : value.Trim().ToLowerInvariant();

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentException("The value cannot exceed 200 characters.", parameterName);
        }

        return normalized;
    }

    private static string? Optional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length > 1000)
        {
            throw new ArgumentException("The value cannot exceed 1000 characters.", nameof(value));
        }

        return normalized;
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
