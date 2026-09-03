using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL canonical, append-only human review history.
/// </summary>
public sealed class PostgresReviewActionRepository :
    IReviewActionRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresReviewActionRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ReviewPerson> CreatePersonAsync(
        string displayName,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string normalizedName = displayName.Trim();
        if (normalizedName.Length > 200)
        {
            throw new ArgumentException(
                "A person display name cannot exceed 200 characters.",
                nameof(displayName));
        }

        PersonId id = PersonId.New();
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO people (
                id,
                display_name,
                created_at_utc,
                merged_into_person_id)
            VALUES (
                @id,
                @display_name,
                @created_at_utc,
                NULL);
            """;
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(id.ToString()));
        command.Parameters.AddWithValue(
            "display_name",
            normalizedName);
        command.Parameters.AddWithValue(
            "created_at_utc",
            createdAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new ReviewPerson(id, normalizedName);
    }

    public async Task<ReviewAction> AssignAsync(
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);
        DateTimeOffset createdAt = createdAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await RequireOccurrenceAsync(
            connection,
            transaction,
            faceOccurrenceId,
            cancellationToken);
        string displayName =
            await RequirePersonAsync(
                connection,
                transaction,
                personId,
                cancellationToken);

        long labelId;
        await using (NpgsqlCommand label = connection.CreateCommand())
        {
            label.Transaction = transaction;
            label.CommandText =
                """
                INSERT INTO person_labels (
                    person_id,
                    face_occurrence_id,
                    label_kind,
                    assigned_by,
                    assigned_at_utc,
                    note)
                VALUES (
                    @person_id,
                    @face_occurrence_id,
                    'manual',
                    @assigned_by,
                    @assigned_at_utc,
                    @note)
                ON CONFLICT(person_id, face_occurrence_id, label_kind)
                DO UPDATE SET
                    assigned_by = excluded.assigned_by,
                    assigned_at_utc = excluded.assigned_at_utc,
                    note = excluded.note
                RETURNING id;
                """;
            label.Parameters.AddWithValue(
                "person_id",
                Guid.Parse(personId.ToString()));
            label.Parameters.AddWithValue(
                "face_occurrence_id",
                Guid.Parse(faceOccurrenceId.ToString()));
            label.Parameters.AddWithValue(
                "assigned_by",
                normalizedActor);
            label.Parameters.AddWithValue(
                "assigned_at_utc",
                createdAt);
            AddNullableText(label, "note", normalizedNote);

            object? value =
                await label.ExecuteScalarAsync(cancellationToken);
            labelId = Convert.ToInt64(value);
        }

        long actionId =
            await InsertActionAsync(
                connection,
                transaction,
                faceOccurrenceId,
                ReviewActionKinds.Assign,
                personId,
                labelId,
                normalizedActor,
                normalizedNote,
                createdAt,
                reversesActionId: null,
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ReviewAction(
            actionId,
            faceOccurrenceId,
            ReviewActionKinds.Assign,
            personId,
            displayName,
            labelId,
            normalizedActor,
            normalizedNote,
            createdAt,
            ReversedAtUtc: null,
            ReversesActionId: null);
    }

    public Task<ReviewAction> MarkUnknownAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default) =>
        RecordPersonlessDecisionAsync(
            faceOccurrenceId,
            ReviewActionKinds.Unknown,
            actor,
            createdAtUtc,
            note,
            cancellationToken);

    public Task<ReviewAction> RejectAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default) =>
        RecordPersonlessDecisionAsync(
            faceOccurrenceId,
            ReviewActionKinds.Reject,
            actor,
            createdAtUtc,
            note,
            cancellationToken);

    public async Task<ReviewAction?> UndoLatestAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);
        DateTimeOffset createdAt = createdAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await RequireOccurrenceAsync(
            connection,
            transaction,
            faceOccurrenceId,
            cancellationToken);
        ReviewAction? latest =
            await GetLatestActiveActionAsync(
                connection,
                transaction,
                faceOccurrenceId,
                cancellationToken);
        if (latest is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await using (NpgsqlCommand reverse = connection.CreateCommand())
        {
            reverse.Transaction = transaction;
            reverse.CommandText =
                """
                UPDATE review_actions
                SET reversed_at_utc = @reversed_at_utc
                WHERE id = @id
                  AND reversed_at_utc IS NULL;
                """;
            reverse.Parameters.AddWithValue(
                "reversed_at_utc",
                createdAt);
            reverse.Parameters.AddWithValue(
                "id",
                latest.Id);
            int updated =
                await reverse.ExecuteNonQueryAsync(cancellationToken);
            if (updated != 1)
            {
                throw new InvalidOperationException(
                    "The review action was changed before it could be undone.");
            }
        }

        await using (NpgsqlCommand restoreSuggestion =
                     connection.CreateCommand())
        {
            restoreSuggestion.Transaction = transaction;
            restoreSuggestion.CommandText =
                """
                UPDATE identity_suggestions
                SET status = 'pending'
                WHERE status = 'accepted'
                  AND id IN (
                    SELECT suggestion_id
                    FROM identity_suggestion_review_actions
                    WHERE action_kind = 'accept'
                      AND review_action_id = @review_action_id);
                """;
            restoreSuggestion.Parameters.AddWithValue(
                "review_action_id",
                latest.Id);
            await restoreSuggestion.ExecuteNonQueryAsync(
                cancellationToken);
        }

        long undoId =
            await InsertActionAsync(
                connection,
                transaction,
                faceOccurrenceId,
                ReviewActionKinds.Undo,
                latest.PersonId,
                latest.PersonLabelId,
                normalizedActor,
                normalizedNote,
                createdAt,
                latest.Id,
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ReviewAction(
            undoId,
            faceOccurrenceId,
            ReviewActionKinds.Undo,
            latest.PersonId,
            latest.PersonDisplayName,
            latest.PersonLabelId,
            normalizedActor,
            normalizedNote,
            createdAt,
            ReversedAtUtc: null,
            ReversesActionId: latest.Id);
    }

    public async Task<IReadOnlyList<ReviewAction>> GetActionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                action.id,
                action.face_occurrence_id,
                action.action_kind,
                action.person_id,
                person.display_name,
                action.person_label_id,
                action.actor,
                action.note,
                action.created_at_utc,
                action.reversed_at_utc,
                action.reverses_action_id
            FROM review_actions AS action
            LEFT JOIN people AS person
                ON person.id = action.person_id
            WHERE action.face_occurrence_id = @face_occurrence_id
            ORDER BY action.id DESC;
            """;
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));

        List<ReviewAction> actions = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            actions.Add(ReadAction(reader));
        }

        return actions;
    }

    private async Task<ReviewAction> RecordPersonlessDecisionAsync(
        FaceOccurrenceId faceOccurrenceId,
        string kind,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note,
        CancellationToken cancellationToken)
    {
        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);
        DateTimeOffset createdAt = createdAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await RequireOccurrenceAsync(
            connection,
            transaction,
            faceOccurrenceId,
            cancellationToken);
        long actionId =
            await InsertActionAsync(
                connection,
                transaction,
                faceOccurrenceId,
                kind,
                personId: null,
                personLabelId: null,
                normalizedActor,
                normalizedNote,
                createdAt,
                reversesActionId: null,
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ReviewAction(
            actionId,
            faceOccurrenceId,
            kind,
            PersonId: null,
            PersonDisplayName: null,
            PersonLabelId: null,
            normalizedActor,
            normalizedNote,
            createdAt,
            ReversedAtUtc: null,
            ReversesActionId: null);
    }

    private static async Task RequireOccurrenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId id,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT 1 FROM face_occurrences WHERE id = @id;";
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(id.ToString()));
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new KeyNotFoundException(
                $"Face occurrence {id} was not found.");
        }
    }

    private static async Task<string> RequirePersonAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PersonId id,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT display_name
            FROM people
            WHERE id = @id
              AND merged_into_person_id IS NULL;
            """;
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(id.ToString()));
        object? value =
            await command.ExecuteScalarAsync(cancellationToken);
        return value is string displayName &&
            !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : throw new KeyNotFoundException(
                $"Person {id} was not found or is not active.");
    }

    private static async Task<long> InsertActionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
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
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
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
                @face_occurrence_id,
                @action_kind,
                @person_id,
                @person_label_id,
                @actor,
                @note,
                @created_at_utc,
                NULL,
                @reverses_action_id)
            RETURNING id;
            """;
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));
        command.Parameters.AddWithValue(
            "action_kind",
            kind);
        AddNullableUuid(command, "person_id", personId);
        AddNullableInt64(
            command,
            "person_label_id",
            personLabelId);
        command.Parameters.AddWithValue("actor", actor);
        AddNullableText(command, "note", note);
        command.Parameters.AddWithValue(
            "created_at_utc",
            createdAtUtc.ToUniversalTime());
        AddNullableInt64(
            command,
            "reverses_action_id",
            reversesActionId);

        object? value =
            await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value);
    }

    private static async Task<ReviewAction?> GetLatestActiveActionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                action.id,
                action.face_occurrence_id,
                action.action_kind,
                action.person_id,
                person.display_name,
                action.person_label_id,
                action.actor,
                action.note,
                action.created_at_utc,
                action.reversed_at_utc,
                action.reverses_action_id
            FROM review_actions AS action
            LEFT JOIN people AS person
                ON person.id = action.person_id
            WHERE action.face_occurrence_id = @face_occurrence_id
              AND action.action_kind IN ('assign', 'unknown', 'reject')
              AND action.reversed_at_utc IS NULL
            ORDER BY action.id DESC
            FOR UPDATE OF action
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadAction(reader)
            : null;
    }

    private static ReviewAction ReadAction(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        FaceOccurrenceId.From(reader.GetGuid(1)),
        reader.GetString(2),
        reader.IsDBNull(3)
            ? null
            : PersonId.From(reader.GetGuid(3)),
        reader.IsDBNull(4)
            ? null
            : reader.GetString(4),
        reader.IsDBNull(5)
            ? null
            : reader.GetInt64(5),
        reader.GetString(6),
        reader.IsDBNull(7)
            ? null
            : reader.GetString(7),
        reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10)
            ? null
            : reader.GetInt64(10));

    private static string Required(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);
        string normalized = value.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentException(
                "The value cannot exceed 200 characters.",
                parameterName);
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
            throw new ArgumentException(
                "The value cannot exceed 1000 characters.",
                nameof(value));
        }

        return normalized;
    }

    private static void AddNullableText(
        NpgsqlCommand command,
        string name,
        string? value)
    {
        NpgsqlParameter parameter =
            command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = value is null
            ? DBNull.Value
            : value;
    }

    private static void AddNullableUuid(
        NpgsqlCommand command,
        string name,
        PersonId? value)
    {
        NpgsqlParameter parameter =
            command.Parameters.Add(name, NpgsqlDbType.Uuid);
        parameter.Value = value is PersonId personId
            ? Guid.Parse(personId.ToString())
            : DBNull.Value;
    }

    private static void AddNullableInt64(
        NpgsqlCommand command,
        string name,
        long? value)
    {
        NpgsqlParameter parameter =
            command.Parameters.Add(name, NpgsqlDbType.Bigint);
        parameter.Value = value is long actual
            ? actual
            : DBNull.Value;
    }
}
