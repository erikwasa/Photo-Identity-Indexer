using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL preview/commit persistence for grouped acceptance of exact rank-one suggestions.
/// </summary>
public sealed class PostgresBulkSuggestionReviewRepository :
    IBulkSuggestionReviewRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresBulkSuggestionReviewRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<BulkSuggestionPreview> PreviewAsync(
        IReadOnlyCollection<long> suggestionIds,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        long[] requestedIds =
            NormalizeSuggestionIds(suggestionIds);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        SuggestionScope scope =
            await ReadScopeAsync(
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

        await transaction.CommitAsync(cancellationToken);

        return new BulkSuggestionPreview(
            requestedIds.Length,
            scope.EligibleRows.Count,
            requestedIds.Length - scope.EligibleRows.Count,
            previewToken,
            scope.Person,
            modelId,
            modelHash);
    }

    public async Task<BulkSuggestionResult> CommitAsync(
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
        long[] requestedIds =
            NormalizeSuggestionIds(suggestionIds);

        if (expectedAffectedCount is < 1 or > BulkReviewLimits.MaximumSuggestionsPerRequest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedAffectedCount),
                $"The expected affected count must be between 1 and {BulkReviewLimits.MaximumSuggestionsPerRequest}.");
        }

        string normalizedToken =
            NormalizePreviewToken(previewToken);
        string normalizedActor =
            Required(actor, nameof(actor), 200);
        string? normalizedNote =
            Optional(note, nameof(note), 1000);
        DateTimeOffset createdAt =
            createdAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        SuggestionScope initialScope =
            await ReadScopeAsync(
                connection,
                transaction,
                requestedIds,
                modelId,
                modelHash,
                cancellationToken);

        await LockFacesAsync(
            connection,
            transaction,
            initialScope.RequestedRows
                .Select(row => row.FaceOccurrenceId)
                .Distinct()
                .OrderBy(
                    id => id.ToString(),
                    StringComparer.Ordinal)
                .ToArray(),
            cancellationToken);

        await LockSuggestionsAsync(
            connection,
            transaction,
            requestedIds,
            cancellationToken);

        SuggestionScope scope =
            await ReadScopeAsync(
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
            !string.Equals(
                currentToken,
                normalizedToken,
                StringComparison.Ordinal))
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
                createdAt,
                cancellationToken);

            long reviewActionId =
                await InsertAssignmentActionAsync(
                    connection,
                    transaction,
                    row.FaceOccurrenceId,
                    scope.Person.Id,
                    labelId,
                    normalizedActor,
                    normalizedNote,
                    createdAt,
                    cancellationToken);

            await AcceptSuggestionAsync(
                connection,
                transaction,
                row.SuggestionId,
                reviewActionId,
                normalizedActor,
                normalizedNote,
                createdAt,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new BulkSuggestionResult(
            requestedIds.Length,
            scope.EligibleRows.Count,
            scope.Person,
            modelId,
            modelHash,
            createdAt);
    }

    private static long[] NormalizeSuggestionIds(
        IReadOnlyCollection<long> suggestionIds)
    {
        ArgumentNullException.ThrowIfNull(suggestionIds);

        long[] ids = suggestionIds
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (ids.Length == 0)
        {
            throw new ArgumentException(
                "Select at least one suggestion for grouped acceptance.",
                nameof(suggestionIds));
        }

        if (ids.Any(id => id <= 0))
        {
            throw new ArgumentException(
                "Suggestion identifiers must be positive.",
                nameof(suggestionIds));
        }

        if (ids.Length > BulkReviewLimits.MaximumSuggestionsPerRequest)
        {
            throw new ArgumentException(
                $"A grouped suggestion request cannot contain more than {BulkReviewLimits.MaximumSuggestionsPerRequest} suggestions.",
                nameof(suggestionIds));
        }

        return ids;
    }

    private static async Task<SuggestionScope> ReadScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<long> requestedIds,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        string[] parameterNames = requestedIds
            .Select((_, index) => $"@suggestion_{index}")
            .ToArray();

        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT
                suggestion.id,
                suggestion.face_occurrence_id,
                suggestion.suggested_person_id,
                COALESCE(
                    NULLIF(BTRIM(person.display_name), ''),
                    'Unnamed person'),
                suggestion.status,
                NOT EXISTS (
                    SELECT 1
                    FROM review_actions AS action
                    WHERE action.face_occurrence_id =
                        suggestion.face_occurrence_id
                      AND action.action_kind IN ('assign', 'unknown', 'reject')
                      AND action.reversed_at_utc IS NULL)
                    AS face_is_unreviewed,
                person.merged_into_person_id
            FROM identity_suggestions AS suggestion
            INNER JOIN identity_suggestion_rankings AS ranking
                ON ranking.suggestion_id = suggestion.id
               AND ranking.rank = 1
               AND ranking.model_id = @model_id
               AND ranking.model_hash = @model_hash
            INNER JOIN people AS person
                ON person.id = suggestion.suggested_person_id
            WHERE suggestion.id IN ({string.Join(", ", parameterNames)})
            ORDER BY suggestion.id;
            """;

        command.Parameters.AddWithValue(
            "model_id",
            modelId.ToString());
        command.Parameters.AddWithValue(
            "model_hash",
            modelHash.ToString());

        for (int index = 0; index < requestedIds.Count; index++)
        {
            command.Parameters.AddWithValue(
                parameterNames[index][1..],
                requestedIds[index]);
        }

        List<BulkSuggestionRow> rows = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(6))
            {
                throw new InvalidOperationException(
                    "The suggested person is no longer active. Refresh the suggestion groups before continuing.");
            }

            rows.Add(new BulkSuggestionRow(
                reader.GetInt64(0),
                FaceOccurrenceId.From(reader.GetGuid(1)),
                PersonId.From(reader.GetGuid(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5)));
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

        ReviewPerson person = new(
            personId,
            rows[0].PersonDisplayName);

        BulkSuggestionRow[] eligibleRows = rows
            .Where(row =>
                row.FaceIsUnreviewed &&
                string.Equals(
                    row.Status,
                    ReviewSuggestionStatuses.Pending,
                    StringComparison.Ordinal))
            .ToArray();

        return new SuggestionScope(
            person,
            rows,
            eligibleRows);
    }

    private static async Task LockFacesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<FaceOccurrenceId> faceIds,
        CancellationToken cancellationToken)
    {
        string[] parameterNames = faceIds
            .Select((_, index) => $"@face_{index}")
            .ToArray();

        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT id
            FROM face_occurrences
            WHERE id IN ({string.Join(", ", parameterNames)})
            ORDER BY id
            FOR UPDATE;
            """;

        for (int index = 0; index < faceIds.Count; index++)
        {
            command.Parameters.AddWithValue(
                parameterNames[index][1..],
                Guid.Parse(faceIds[index].ToString()));
        }

        int locked = 0;
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            locked++;
        }

        if (locked != faceIds.Count)
        {
            throw new InvalidOperationException(
                "One or more selected suggestion faces no longer exist. Refresh the group before committing.");
        }
    }

    private static async Task LockSuggestionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<long> suggestionIds,
        CancellationToken cancellationToken)
    {
        string[] parameterNames = suggestionIds
            .Select((_, index) => $"@suggestion_{index}")
            .ToArray();

        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT id
            FROM identity_suggestions
            WHERE id IN ({string.Join(", ", parameterNames)})
            ORDER BY id
            FOR UPDATE;
            """;

        for (int index = 0; index < suggestionIds.Count; index++)
        {
            command.Parameters.AddWithValue(
                parameterNames[index][1..],
                suggestionIds[index]);
        }

        int locked = 0;
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            locked++;
        }

        if (locked != suggestionIds.Count)
        {
            throw new InvalidOperationException(
                "One or more selected suggestions no longer exist. Refresh the group before committing.");
        }
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
        foreach (BulkSuggestionRow row in eligibleRows
                     .OrderBy(row => row.SuggestionId))
        {
            value
                .Append(row.SuggestionId)
                .Append(':')
                .Append(row.FaceOccurrenceId)
                .Append('\n');
        }

        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value.ToString())))
            .ToLowerInvariant();
    }

    private static string NormalizePreviewToken(
        string previewToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewToken);

        string normalized =
            previewToken.Trim().ToLowerInvariant();

        if (normalized.Length != 64 ||
            normalized.Any(
                character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "The grouped suggestion preview token is invalid.",
                nameof(previewToken));
        }

        return normalized;
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
        await using NpgsqlCommand command =
            connection.CreateCommand();
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
                @actor,
                @created_at_utc,
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
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue(
            "created_at_utc",
            createdAtUtc);
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
        await using NpgsqlCommand command =
            connection.CreateCommand();
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
        command.Parameters.AddWithValue(
            "person_label_id",
            personLabelId);
        command.Parameters.AddWithValue("actor", actor);
        AddNullableText(command, "note", note);
        command.Parameters.AddWithValue(
            "created_at_utc",
            createdAtUtc);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task AcceptSuggestionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long suggestionId,
        long reviewActionId,
        string actor,
        string? note,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using (NpgsqlCommand update =
                     connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE identity_suggestions
                SET status = 'accepted'
                WHERE id = @suggestion_id
                  AND status = 'pending';
                """;
            update.Parameters.AddWithValue(
                "suggestion_id",
                suggestionId);

            if (await update.ExecuteNonQueryAsync(
                    cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "A suggestion changed before the grouped acceptance could be saved.");
            }
        }

        await using NpgsqlCommand insert =
            connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO identity_suggestion_review_actions (
                suggestion_id,
                action_kind,
                actor,
                note,
                created_at_utc,
                review_action_id)
            VALUES (
                @suggestion_id,
                'accept',
                @actor,
                @note,
                @created_at_utc,
                @review_action_id);
            """;
        insert.Parameters.AddWithValue(
            "suggestion_id",
            suggestionId);
        insert.Parameters.AddWithValue("actor", actor);
        AddNullableText(insert, "note", note);
        insert.Parameters.AddWithValue(
            "created_at_utc",
            createdAtUtc);
        insert.Parameters.AddWithValue(
            "review_action_id",
            reviewActionId);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Required(
        string value,
        string parameterName,
        int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{parameterName} cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? Optional(
        string? value,
        string parameterName,
        int maximumLength)
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

    private static void AddNullableText(
        NpgsqlCommand command,
        string name,
        string? value)
    {
        NpgsqlParameter parameter =
            command.Parameters.Add(
                name,
                NpgsqlDbType.Text);
        parameter.Value = value is null
            ? DBNull.Value
            : value;
    }

    private sealed record SuggestionScope(
        ReviewPerson Person,
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
