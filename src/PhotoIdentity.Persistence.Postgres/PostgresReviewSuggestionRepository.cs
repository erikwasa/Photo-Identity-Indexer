using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresReviewSuggestionRepository :
    IReviewSuggestionRepository
{
    private const string SuggestionSelect = """
        WITH latest_decision AS (
            SELECT
                decision.*,
                ROW_NUMBER() OVER (
                    PARTITION BY suggestion_id
                    ORDER BY id DESC) AS row_number
            FROM identity_suggestion_review_actions AS decision
        )
        SELECT
            suggestion.id,
            suggestion.suggested_person_id,
            COALESCE(NULLIF(BTRIM(person.display_name), ''), 'Unnamed person'),
            suggestion.model_id,
            suggestion.model_hash,
            ranking.rank,
            suggestion.score,
            ranking.score_margin,
            suggestion.status,
            ranking.generated_at_utc,
            latest_decision.id,
            latest_decision.action_kind,
            latest_decision.actor,
            latest_decision.note,
            latest_decision.created_at_utc,
            latest_decision.review_action_id
        FROM identity_suggestion_rankings AS ranking
        INNER JOIN identity_suggestions AS suggestion
            ON suggestion.id = ranking.suggestion_id
        INNER JOIN people AS person
            ON person.id = suggestion.suggested_person_id
        LEFT JOIN latest_decision
            ON latest_decision.suggestion_id = suggestion.id
           AND latest_decision.row_number = 1
        """;

    private readonly PostgresCatalogueDatabase _database;

    public PostgresReviewSuggestionRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<ReviewIdentitySuggestion>> GetSuggestionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {SuggestionSelect}
            WHERE ranking.face_occurrence_id = @face_occurrence_id
            ORDER BY
                ranking.generated_at_utc DESC,
                suggestion.model_id,
                suggestion.model_hash,
                ranking.rank;
            """;
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));

        List<ReviewIdentitySuggestion> suggestions = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            suggestions.Add(ReadSuggestion(reader));
        }

        return suggestions;
    }

    public async Task<ReviewIdentitySuggestion> AcceptAsync(
        FaceOccurrenceId faceOccurrenceId,
        long suggestionId,
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

        await LockFaceAsync(connection, transaction, faceOccurrenceId, cancellationToken);
        ReviewIdentitySuggestion suggestion = await RequirePendingSuggestionAsync(
            connection, transaction, faceOccurrenceId, suggestionId, cancellationToken);
        await RequireUnreviewedFaceAsync(
            connection, transaction, faceOccurrenceId, cancellationToken);

        long labelId = await UpsertManualLabelAsync(
            connection, transaction, faceOccurrenceId, suggestion.Person.Id,
            normalizedActor, normalizedNote, createdAt, cancellationToken);
        long reviewActionId = await InsertAssignmentActionAsync(
            connection, transaction, faceOccurrenceId, suggestion.Person.Id,
            labelId, normalizedActor, normalizedNote, createdAt, cancellationToken);
        await UpdateStatusAsync(
            connection, transaction, suggestionId,
            ReviewSuggestionStatuses.Accepted, cancellationToken);
        long decisionId = await InsertSuggestionActionAsync(
            connection, transaction, suggestionId,
            ReviewSuggestionActionKinds.Accept, reviewActionId,
            normalizedActor, normalizedNote, createdAt, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return suggestion with
        {
            Status = ReviewSuggestionStatuses.Accepted,
            LatestAction = new ReviewSuggestionAction(
                decisionId,
                ReviewSuggestionActionKinds.Accept,
                normalizedActor,
                normalizedNote,
                createdAt,
                reviewActionId),
        };
    }

    public async Task<ReviewIdentitySuggestion> RejectAsync(
        FaceOccurrenceId faceOccurrenceId,
        long suggestionId,
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

        await LockFaceAsync(connection, transaction, faceOccurrenceId, cancellationToken);
        ReviewIdentitySuggestion suggestion = await RequirePendingSuggestionAsync(
            connection, transaction, faceOccurrenceId, suggestionId, cancellationToken);
        await RequireUnreviewedFaceAsync(
            connection, transaction, faceOccurrenceId, cancellationToken);

        await UpdateStatusAsync(
            connection, transaction, suggestionId,
            ReviewSuggestionStatuses.Rejected, cancellationToken);
        long decisionId = await InsertSuggestionActionAsync(
            connection, transaction, suggestionId,
            ReviewSuggestionActionKinds.Reject, reviewActionId: null,
            normalizedActor, normalizedNote, createdAt, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return suggestion with
        {
            Status = ReviewSuggestionStatuses.Rejected,
            LatestAction = new ReviewSuggestionAction(
                decisionId,
                ReviewSuggestionActionKinds.Reject,
                normalizedActor,
                normalizedNote,
                createdAt,
                ReviewActionId: null),
        };
    }

    private static async Task LockFaceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT 1
            FROM face_occurrences
            WHERE id = @face_occurrence_id
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new KeyNotFoundException(
                $"Face occurrence {faceOccurrenceId} was not found.");
        }
    }

    private static async Task<ReviewIdentitySuggestion> RequirePendingSuggestionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        long suggestionId,
        CancellationToken cancellationToken)
    {
        if (suggestionId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(suggestionId),
                "The suggestion identifier must be positive.");
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            {SuggestionSelect}
            WHERE ranking.face_occurrence_id = @face_occurrence_id
              AND suggestion.id = @suggestion_id;
            """;
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));
        command.Parameters.AddWithValue("suggestion_id", suggestionId);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new KeyNotFoundException(
                $"Suggestion {suggestionId} was not found for face occurrence {faceOccurrenceId}.");
        }

        ReviewIdentitySuggestion suggestion = ReadSuggestion(reader);
        if (!string.Equals(
                suggestion.Status,
                ReviewSuggestionStatuses.Pending,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Suggestion {suggestionId} has already been {suggestion.Status}.");
        }

        return suggestion;
    }

    private static async Task RequireUnreviewedFaceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT 1
            FROM review_actions
            WHERE face_occurrence_id = @face_occurrence_id
              AND action_kind IN ('assign', 'unknown', 'reject')
              AND reversed_at_utc IS NULL
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));
        if (await command.ExecuteScalarAsync(cancellationToken) is not null)
        {
            throw new InvalidOperationException(
                "The face has already been reviewed. Undo the active face decision before reviewing a suggestion.");
        }
    }

    private static async Task<long> UpsertManualLabelAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        string actor,
        string? note,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
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
        command.Parameters.AddWithValue(
            "person_id",
            Guid.Parse(personId.ToString()));
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));
        command.Parameters.AddWithValue("assigned_by", actor);
        command.Parameters.AddWithValue("assigned_at_utc", createdAtUtc);
        AddNullableText(command, "note", note);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long> InsertAssignmentActionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        long personLabelId,
        string actor,
        string? note,
        DateTimeOffset createdAtUtc,
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
                'assign',
                @person_id,
                @person_label_id,
                @actor,
                @note,
                @created_at_utc,
                NULL,
                NULL)
            RETURNING id;
            """;
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));
        command.Parameters.AddWithValue(
            "person_id",
            Guid.Parse(personId.ToString()));
        command.Parameters.AddWithValue("person_label_id", personLabelId);
        command.Parameters.AddWithValue("actor", actor);
        AddNullableText(command, "note", note);
        command.Parameters.AddWithValue("created_at_utc", createdAtUtc);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task UpdateStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long suggestionId,
        string status,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE identity_suggestions
            SET status = @status
            WHERE id = @suggestion_id
              AND status = 'pending';
            """;
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("suggestion_id", suggestionId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException(
                "The suggestion was changed before the review decision could be saved.");
        }
    }

    private static async Task<long> InsertSuggestionActionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long suggestionId,
        string actionKind,
        long? reviewActionId,
        string actor,
        string? note,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO identity_suggestion_review_actions (
                suggestion_id,
                action_kind,
                review_action_id,
                actor,
                note,
                created_at_utc)
            VALUES (
                @suggestion_id,
                @action_kind,
                @review_action_id,
                @actor,
                @note,
                @created_at_utc)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("suggestion_id", suggestionId);
        command.Parameters.AddWithValue("action_kind", actionKind);
        NpgsqlParameter reviewAction =
            command.Parameters.Add("review_action_id", NpgsqlDbType.Bigint);
        reviewAction.Value = reviewActionId is long id ? id : DBNull.Value;
        command.Parameters.AddWithValue("actor", actor);
        AddNullableText(command, "note", note);
        command.Parameters.AddWithValue("created_at_utc", createdAtUtc);
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private static ReviewIdentitySuggestion ReadSuggestion(
        NpgsqlDataReader reader)
    {
        ReviewSuggestionAction? latestAction = reader.IsDBNull(10)
            ? null
            : new ReviewSuggestionAction(
                reader.GetInt64(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetFieldValue<DateTimeOffset>(14),
                reader.IsDBNull(15) ? null : reader.GetInt64(15));

        return new ReviewIdentitySuggestion(
            reader.GetInt64(0),
            new ReviewPerson(
                PersonId.From(reader.GetGuid(1)),
                reader.GetString(2)),
            new ModelId(reader.GetString(3)),
            new Sha256Digest(reader.GetString(4)),
            reader.GetInt32(5),
            reader.GetDouble(6),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            latestAction);
    }

    private static string Required(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
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
                "A review note cannot exceed 1000 characters.",
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
        parameter.Value = value is null ? DBNull.Value : value;
    }
}
