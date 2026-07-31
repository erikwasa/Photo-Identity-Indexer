using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Previews and commits explicit acceptance of one same-person group of rank-one suggestions.
/// Every accepted suggestion creates both a normal assignment action and a linked suggestion action.
/// </summary>
public sealed class SqliteBulkSuggestionReviewRepository
{
    public const int MaximumSuggestionsPerRequest = 200;

    private const string PendingStatus = "pending";
    private const string AcceptedStatus = "accepted";
    private const string AcceptAction = "accept";

    private readonly SqliteCatalogueDatabase _database;

    public SqliteBulkSuggestionReviewRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueBulkSuggestionPreview> PreviewAsync(
        IReadOnlyCollection<long> suggestionIds,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        long[] requestedIds = NormalizeSuggestionIds(suggestionIds);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        SuggestionScope scope = await ReadScopeAsync(
            connection,
            transaction,
            requestedIds,
            modelId,
            modelHash,
            cancellationToken);
        string previewToken = CreatePreviewToken(
            requestedIds,
            scope.EligibleRows,
            scope.Person.Id,
            modelId,
            modelHash);
        transaction.Commit();

        return new CatalogueBulkSuggestionPreview(
            requestedIds.Length,
            scope.EligibleRows.Count,
            requestedIds.Length - scope.EligibleRows.Count,
            previewToken,
            scope.Person,
            modelId,
            modelHash);
    }

    public async Task<CatalogueBulkSuggestionResult> CommitAsync(
        IReadOnlyCollection<long> suggestionIds,
        ModelId modelId,
        Sha256Digest modelHash,
        int expectedAffectedCount,
        string previewToken,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        long[] requestedIds = NormalizeSuggestionIds(suggestionIds);
        if (expectedAffectedCount is < 1 or > MaximumSuggestionsPerRequest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedAffectedCount),
                $"The expected affected count must be between 1 and {MaximumSuggestionsPerRequest}.");
        }

        string normalizedToken = NormalizePreviewToken(previewToken);
        string normalizedActor = Required(actor, nameof(actor), 200);
        string? normalizedNote = Optional(note, nameof(note), 1000);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        SuggestionScope scope = await ReadScopeAsync(
            connection,
            transaction,
            requestedIds,
            modelId,
            modelHash,
            cancellationToken);
        string currentToken = CreatePreviewToken(
            requestedIds,
            scope.EligibleRows,
            scope.Person.Id,
            modelId,
            modelHash);
        if (scope.EligibleRows.Count != expectedAffectedCount ||
            !string.Equals(currentToken, normalizedToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The selected suggestions changed after preview. Preview the grouped acceptance again before committing.");
        }

        foreach (BulkSuggestionRow row in scope.EligibleRows)
        {
            long labelId = await UpsertManualLabelAsync(
                connection,
                transaction,
                row.FaceOccurrenceId,
                scope.Person.Id,
                normalizedActor,
                normalizedNote,
                createdAtUtc,
                cancellationToken);
            long reviewActionId = await InsertAssignmentActionAsync(
                connection,
                transaction,
                row.FaceOccurrenceId,
                scope.Person.Id,
                labelId,
                normalizedActor,
                normalizedNote,
                createdAtUtc,
                cancellationToken);
            await AcceptSuggestionAsync(
                connection,
                transaction,
                row.SuggestionId,
                reviewActionId,
                normalizedActor,
                normalizedNote,
                createdAtUtc,
                cancellationToken);
        }

        transaction.Commit();
        return new CatalogueBulkSuggestionResult(
            requestedIds.Length,
            scope.EligibleRows.Count,
            scope.Person,
            modelId,
            modelHash,
            createdAtUtc.ToUniversalTime());
    }

    private static long[] NormalizeSuggestionIds(IReadOnlyCollection<long> suggestionIds)
    {
        ArgumentNullException.ThrowIfNull(suggestionIds);
        long[] ids = suggestionIds.Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0)
        {
            throw new ArgumentException("Select at least one suggestion for grouped acceptance.", nameof(suggestionIds));
        }

        if (ids.Any(id => id <= 0))
        {
            throw new ArgumentException("Suggestion identifiers must be positive.", nameof(suggestionIds));
        }

        if (ids.Length > MaximumSuggestionsPerRequest)
        {
            throw new ArgumentException(
                $"A grouped suggestion request cannot contain more than {MaximumSuggestionsPerRequest} suggestions.",
                nameof(suggestionIds));
        }

        return ids;
    }

    private static async Task<SuggestionScope> ReadScopeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<long> requestedIds,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        string[] parameterNames = requestedIds
            .Select((_, index) => $"$suggestion_{index}")
            .ToArray();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                suggestion.id,
                suggestion.face_occurrence_id,
                suggestion.suggested_person_id,
                COALESCE(NULLIF(TRIM(person.display_name), ''), 'Unnamed person'),
                suggestion.status,
                CASE WHEN EXISTS (
                    SELECT 1
                    FROM review_actions
                    WHERE review_actions.face_occurrence_id = suggestion.face_occurrence_id
                      AND review_actions.action_kind IN ('assign', 'reject')
                      AND review_actions.reversed_at_utc IS NULL)
                THEN 0 ELSE 1 END AS face_is_unreviewed,
                person.merged_into_person_id
            FROM identity_suggestions AS suggestion
            INNER JOIN identity_suggestion_rankings AS ranking
                ON ranking.suggestion_id = suggestion.id
               AND ranking.rank = 1
               AND ranking.model_id = $model_id
               AND ranking.model_hash = $model_hash
            INNER JOIN people AS person
                ON person.id = suggestion.suggested_person_id
            WHERE suggestion.id IN ({string.Join(", ", parameterNames)})
            ORDER BY suggestion.id;
            """;
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());
        for (int index = 0; index < requestedIds.Count; index++)
        {
            command.Parameters.AddWithValue(parameterNames[index], requestedIds[index]);
        }

        List<BulkSuggestionRow> rows = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(6))
                {
                    throw new InvalidOperationException(
                        "The suggested person is no longer active. Refresh the suggestion groups before continuing.");
                }

                rows.Add(new BulkSuggestionRow(
                    reader.GetInt64(0),
                    FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))),
                    PersonId.From(Guid.Parse(reader.GetString(2))),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5) == 1));
            }
        }

        if (rows.Count != requestedIds.Count)
        {
            throw new InvalidOperationException(
                "One or more selected suggestions are no longer rank-one matches for the exact model revision. Refresh the group before previewing.");
        }

        PersonId personId = rows[0].PersonId;
        if (rows.Any(row => row.PersonId != personId))
        {
            throw new ArgumentException(
                "Grouped suggestion acceptance must contain suggestions for one person only.",
                nameof(requestedIds));
        }

        CatalogueReviewPerson person = new(personId, rows[0].PersonDisplayName);
        BulkSuggestionRow[] eligibleRows = rows
            .Where(row => row.FaceIsUnreviewed && string.Equals(row.Status, PendingStatus, StringComparison.Ordinal))
            .ToArray();
        return new SuggestionScope(person, rows, eligibleRows);
    }

    private static string CreatePreviewToken(
        IReadOnlyCollection<long> requestedIds,
        IReadOnlyCollection<BulkSuggestionRow> eligibleRows,
        PersonId personId,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        StringBuilder value = new();
        value.Append(modelId).Append('\n');
        value.Append(modelHash).Append('\n');
        value.Append(personId).Append('\n');
        value.Append("requested").Append('\n');
        foreach (long id in requestedIds)
        {
            value.Append(id).Append('\n');
        }

        value.Append("eligible").Append('\n');
        foreach (BulkSuggestionRow row in eligibleRows.OrderBy(row => row.SuggestionId))
        {
            value.Append(row.SuggestionId).Append(':').Append(row.FaceOccurrenceId).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())))
            .ToLowerInvariant();
    }

    private static string NormalizePreviewToken(string previewToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewToken);
        string normalized = previewToken.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("The grouped suggestion preview token is invalid.", nameof(previewToken));
        }

        return normalized;
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
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
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
                    $actor,
                    $created_at_utc,
                    $note)
                ON CONFLICT(person_id, face_occurrence_id, label_kind) DO UPDATE SET
                    assigned_by = excluded.assigned_by,
                    assigned_at_utc = excluded.assigned_at_utc,
                    note = excluded.note;
                """;
            command.Parameters.AddWithValue("$person_id", personId.ToString());
            command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
            command.Parameters.AddWithValue("$actor", actor);
            command.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
            command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT id
            FROM person_labels
            WHERE person_id = $person_id
              AND face_occurrence_id = $face_occurrence_id
              AND label_kind = 'manual';
            """;
        read.Parameters.AddWithValue("$person_id", personId.ToString());
        read.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        object? value = await read.ExecuteScalarAsync(cancellationToken);
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

    private static async Task AcceptSuggestionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long suggestionId,
        long reviewActionId,
        string actor,
        string? note,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE identity_suggestions
                SET status = $accepted_status
                WHERE id = $suggestion_id
                  AND status = $pending_status;
                """;
            update.Parameters.AddWithValue("$accepted_status", AcceptedStatus);
            update.Parameters.AddWithValue("$pending_status", PendingStatus);
            update.Parameters.AddWithValue("$suggestion_id", suggestionId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "A suggestion changed before the grouped acceptance could be saved.");
            }
        }

        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO identity_suggestion_review_actions (
                suggestion_id,
                action_kind,
                actor,
                note,
                created_at_utc,
                review_action_id)
            VALUES (
                $suggestion_id,
                $action_kind,
                $actor,
                $note,
                $created_at_utc,
                $review_action_id);
            """;
        insert.Parameters.AddWithValue("$suggestion_id", suggestionId);
        insert.Parameters.AddWithValue("$action_kind", AcceptAction);
        insert.Parameters.AddWithValue("$actor", actor);
        insert.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        insert.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
        insert.Parameters.AddWithValue("$review_action_id", reviewActionId);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{parameterName} cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? Optional(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{parameterName} cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    private sealed record SuggestionScope(
        CatalogueReviewPerson Person,
        IReadOnlyList<BulkSuggestionRow> RequestedRows,
        IReadOnlyList<BulkSuggestionRow> EligibleRows);

    private sealed record BulkSuggestionRow(
        long SuggestionId,
        FaceOccurrenceId FaceOccurrenceId,
        PersonId PersonId,
        string PersonDisplayName,
        string Status,
        bool FaceIsUnreviewed);
}
