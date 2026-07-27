using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Reads model-generated identity suggestions and records explicit human decisions.
/// Accepting a suggestion creates a normal append-only face assignment; rejecting one
/// records a durable face-person exclusion without rejecting the face itself.
/// </summary>
public sealed class SqliteReviewSuggestionRepository
{
    private const string PendingStatus = "pending";
    private const string AcceptedStatus = "accepted";
    private const string RejectedStatus = "rejected";
    private const string AcceptAction = "accept";
    private const string RejectAction = "reject";

    private const string SuggestionSelect = """
        WITH latest_decision AS (
            SELECT
                identity_suggestion_review_actions.*,
                ROW_NUMBER() OVER (
                    PARTITION BY suggestion_id
                    ORDER BY id DESC) AS row_number
            FROM identity_suggestion_review_actions
        )
        SELECT
            suggestion.id,
            suggestion.suggested_person_id,
            COALESCE(NULLIF(TRIM(person.display_name), ''), 'Unnamed person'),
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

    private readonly SqliteCatalogueDatabase _database;

    public SqliteReviewSuggestionRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<CatalogueReviewIdentitySuggestion>> GetSuggestionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {SuggestionSelect}
            WHERE ranking.face_occurrence_id = $face_occurrence_id
            ORDER BY
                ranking.generated_at_utc DESC,
                suggestion.model_id,
                suggestion.model_hash,
                ranking.rank;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());

        List<CatalogueReviewIdentitySuggestion> suggestions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            suggestions.Add(ReadSuggestion(reader));
        }

        return suggestions;
    }

    public async Task<CatalogueReviewIdentitySuggestion> AcceptAsync(
        FaceOccurrenceId faceOccurrenceId,
        long suggestionId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        CatalogueReviewIdentitySuggestion suggestion = await RequirePendingSuggestionAsync(
            connection,
            transaction,
            faceOccurrenceId,
            suggestionId,
            cancellationToken);
        await RequireUnreviewedFaceAsync(connection, transaction, faceOccurrenceId, cancellationToken);

        long labelId = await UpsertManualLabelAsync(
            connection,
            transaction,
            faceOccurrenceId,
            suggestion.Person.Id,
            normalizedActor,
            normalizedNote,
            createdAtUtc,
            cancellationToken);
        long reviewActionId = await InsertAssignmentActionAsync(
            connection,
            transaction,
            faceOccurrenceId,
            suggestion.Person.Id,
            labelId,
            normalizedActor,
            normalizedNote,
            createdAtUtc,
            cancellationToken);
        await UpdateStatusAsync(
            connection,
            transaction,
            suggestionId,
            AcceptedStatus,
            cancellationToken);
        long decisionId = await InsertSuggestionActionAsync(
            connection,
            transaction,
            suggestionId,
            AcceptAction,
            reviewActionId,
            normalizedActor,
            normalizedNote,
            createdAtUtc,
            cancellationToken);
        transaction.Commit();

        return suggestion with
        {
            Status = AcceptedStatus,
            LatestAction = new CatalogueReviewSuggestionAction(
                decisionId,
                AcceptAction,
                normalizedActor,
                normalizedNote,
                createdAtUtc.ToUniversalTime(),
                reviewActionId),
        };
    }

    public async Task<CatalogueReviewIdentitySuggestion> RejectAsync(
        FaceOccurrenceId faceOccurrenceId,
        long suggestionId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        CatalogueReviewIdentitySuggestion suggestion = await RequirePendingSuggestionAsync(
            connection,
            transaction,
            faceOccurrenceId,
            suggestionId,
            cancellationToken);
        await RequireUnreviewedFaceAsync(connection, transaction, faceOccurrenceId, cancellationToken);

        await UpdateStatusAsync(
            connection,
            transaction,
            suggestionId,
            RejectedStatus,
            cancellationToken);
        long decisionId = await InsertSuggestionActionAsync(
            connection,
            transaction,
            suggestionId,
            RejectAction,
            reviewActionId: null,
            normalizedActor,
            normalizedNote,
            createdAtUtc,
            cancellationToken);
        transaction.Commit();

        return suggestion with
        {
            Status = RejectedStatus,
            LatestAction = new CatalogueReviewSuggestionAction(
                decisionId,
                RejectAction,
                normalizedActor,
                normalizedNote,
                createdAtUtc.ToUniversalTime(),
                ReviewActionId: null),
        };
    }

    private static async Task<CatalogueReviewIdentitySuggestion> RequirePendingSuggestionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        long suggestionId,
        CancellationToken cancellationToken)
    {
        if (suggestionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(suggestionId), "The suggestion identifier must be positive.");
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            {SuggestionSelect}
            WHERE ranking.face_occurrence_id = $face_occurrence_id
              AND suggestion.id = $suggestion_id;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$suggestion_id", suggestionId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new KeyNotFoundException(
                $"Suggestion {suggestionId} was not found for face occurrence {faceOccurrenceId}.");
        }

        CatalogueReviewIdentitySuggestion suggestion = ReadSuggestion(reader);
        if (!string.Equals(suggestion.Status, PendingStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Suggestion {suggestionId} has already been {suggestion.Status}.");
        }

        return suggestion;
    }

    private static async Task RequireUnreviewedFaceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM review_actions
            WHERE face_occurrence_id = $face_occurrence_id
              AND action_kind IN ('assign', 'reject')
              AND reversed_at_utc IS NULL
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        if (await command.ExecuteScalarAsync(cancellationToken) is not null)
        {
            throw new InvalidOperationException(
                "The face has already been reviewed. Undo the active face decision before reviewing a suggestion.");
        }
    }

    private static async Task<long> UpsertManualLabelAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        string actor,
        string? note,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
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
            labelCommand.Parameters.AddWithValue("$assigned_by", actor);
            labelCommand.Parameters.AddWithValue("$assigned_at_utc", Format(createdAtUtc));
            labelCommand.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            await labelCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        using SqliteCommand labelIdCommand = connection.CreateCommand();
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
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<long> InsertAssignmentActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        long personLabelId,
        string actor,
        string? note,
        DateTimeOffset createdAtUtc,
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
                'assign',
                $person_id,
                $person_label_id,
                $actor,
                $note,
                $created_at_utc,
                NULL,
                NULL);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        command.Parameters.AddWithValue("$person_label_id", personLabelId);
        command.Parameters.AddWithValue("$actor", actor);
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task UpdateStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long suggestionId,
        string status,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE identity_suggestions
            SET status = $status
            WHERE id = $suggestion_id
              AND status = $pending_status;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$suggestion_id", suggestionId);
        command.Parameters.AddWithValue("$pending_status", PendingStatus);
        int updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated != 1)
        {
            throw new InvalidOperationException(
                "The suggestion was changed before the review decision could be saved.");
        }
    }

    private static async Task<long> InsertSuggestionActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long suggestionId,
        string actionKind,
        long? reviewActionId,
        string actor,
        string? note,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO identity_suggestion_review_actions (
                suggestion_id,
                action_kind,
                review_action_id,
                actor,
                note,
                created_at_utc)
            VALUES (
                $suggestion_id,
                $action_kind,
                $review_action_id,
                $actor,
                $note,
                $created_at_utc);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$suggestion_id", suggestionId);
        command.Parameters.AddWithValue("$action_kind", actionKind);
        command.Parameters.AddWithValue("$review_action_id", (object?)reviewActionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$actor", actor);
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static CatalogueReviewIdentitySuggestion ReadSuggestion(SqliteDataReader reader)
    {
        CatalogueReviewSuggestionAction? latestAction = reader.IsDBNull(10)
            ? null
            : new CatalogueReviewSuggestionAction(
                reader.GetInt64(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                Parse(reader.GetString(14)),
                reader.IsDBNull(15) ? null : reader.GetInt64(15));

        return new CatalogueReviewIdentitySuggestion(
            reader.GetInt64(0),
            new CatalogueReviewPerson(
                PersonId.From(Guid.Parse(reader.GetString(1))),
                reader.GetString(2)),
            new ModelId(reader.GetString(3)),
            new Sha256Digest(reader.GetString(4)),
            reader.GetInt32(5),
            reader.GetDouble(6),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.GetString(8),
            Parse(reader.GetString(9)),
            latestAction);
    }

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
            throw new ArgumentException("A review note cannot exceed 1000 characters.", nameof(value));
        }

        return normalized;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind).ToUniversalTime();
}
