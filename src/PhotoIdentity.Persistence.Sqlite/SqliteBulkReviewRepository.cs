using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Previews and commits bounded bulk assignment, Unknown or face rejection operations.
/// A commit must present the exact token produced by a preview of the same eligible face set.
/// </summary>
public sealed class SqliteBulkReviewRepository
{
    public const int MaximumFacesPerRequest = 200;

    private readonly SqliteCatalogueDatabase _database;

    public SqliteBulkReviewRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueBulkReviewPreview> PreviewAsync(
        IReadOnlyCollection<FaceOccurrenceId> faceOccurrenceIds,
        string action,
        PersonId? personId,
        CancellationToken cancellationToken = default)
    {
        FaceOccurrenceId[] requestedIds = NormalizeFaceIds(faceOccurrenceIds);
        string normalizedAction = NormalizeAction(action, personId);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        CatalogueReviewPerson? person = normalizedAction == CatalogueBulkReviewActionKinds.Assign
            ? await RequireActivePersonAsync(
                connection,
                transaction,
                personId!.Value,
                cancellationToken)
            : null;
        FaceOccurrenceId[] eligibleIds = await ReadEligibleFaceIdsAsync(
            connection,
            transaction,
            requestedIds,
            cancellationToken);
        string previewToken = CreatePreviewToken(
            normalizedAction,
            personId,
            requestedIds,
            eligibleIds);
        transaction.Commit();

        return new CatalogueBulkReviewPreview(
            normalizedAction,
            requestedIds.Length,
            eligibleIds.Length,
            requestedIds.Length - eligibleIds.Length,
            previewToken,
            person);
    }

    public async Task<CatalogueBulkReviewResult> CommitAsync(
        IReadOnlyCollection<FaceOccurrenceId> faceOccurrenceIds,
        string action,
        PersonId? personId,
        int expectedAffectedCount,
        string previewToken,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        FaceOccurrenceId[] requestedIds = NormalizeFaceIds(faceOccurrenceIds);
        string normalizedAction = NormalizeAction(action, personId);
        string normalizedPreviewToken = NormalizePreviewToken(previewToken);
        string normalizedActor = Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);
        if (expectedAffectedCount is < 1 or > MaximumFacesPerRequest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedAffectedCount),
                $"The expected affected count must be between 1 and {MaximumFacesPerRequest}.");
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        CatalogueReviewPerson? person = normalizedAction == CatalogueBulkReviewActionKinds.Assign
            ? await RequireActivePersonAsync(
                connection,
                transaction,
                personId!.Value,
                cancellationToken)
            : null;
        FaceOccurrenceId[] eligibleIds = await ReadEligibleFaceIdsAsync(
            connection,
            transaction,
            requestedIds,
            cancellationToken);
        string currentToken = CreatePreviewToken(
            normalizedAction,
            personId,
            requestedIds,
            eligibleIds);
        if (eligibleIds.Length != expectedAffectedCount ||
            !string.Equals(currentToken, normalizedPreviewToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The selected faces changed after the preview. Preview the bulk action again before committing.");
        }

        foreach (FaceOccurrenceId faceOccurrenceId in eligibleIds)
        {
            long? labelId = normalizedAction == CatalogueBulkReviewActionKinds.Assign
                ? await UpsertManualLabelAsync(
                    connection,
                    transaction,
                    faceOccurrenceId,
                    personId!.Value,
                    normalizedActor,
                    normalizedNote,
                    createdAtUtc,
                    cancellationToken)
                : null;
            await InsertReviewActionAsync(
                connection,
                transaction,
                faceOccurrenceId,
                normalizedAction,
                personId,
                labelId,
                normalizedActor,
                normalizedNote,
                createdAtUtc,
                cancellationToken);
        }

        transaction.Commit();
        return new CatalogueBulkReviewResult(
            normalizedAction,
            requestedIds.Length,
            eligibleIds.Length,
            person,
            createdAtUtc.ToUniversalTime());
    }

    private static FaceOccurrenceId[] NormalizeFaceIds(
        IReadOnlyCollection<FaceOccurrenceId> faceOccurrenceIds)
    {
        ArgumentNullException.ThrowIfNull(faceOccurrenceIds);
        FaceOccurrenceId[] ids = faceOccurrenceIds
            .Distinct()
            .OrderBy(id => id.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            throw new ArgumentException("Select at least one face for bulk review.", nameof(faceOccurrenceIds));
        }

        if (ids.Length > MaximumFacesPerRequest)
        {
            throw new ArgumentException(
                $"A bulk review request cannot contain more than {MaximumFacesPerRequest} faces.",
                nameof(faceOccurrenceIds));
        }

        return ids;
    }

    private static string NormalizeAction(string action, PersonId? personId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        string normalized = action.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case CatalogueBulkReviewActionKinds.Assign when personId is null:
                throw new ArgumentException("A person is required for a bulk assignment.", nameof(personId));
            case CatalogueBulkReviewActionKinds.Unknown when personId is not null:
                throw new ArgumentException("A bulk Unknown decision cannot include a person.", nameof(personId));
            case CatalogueBulkReviewActionKinds.Reject when personId is not null:
                throw new ArgumentException("A bulk face rejection cannot include a person.", nameof(personId));
            case CatalogueBulkReviewActionKinds.Assign:
            case CatalogueBulkReviewActionKinds.Unknown:
            case CatalogueBulkReviewActionKinds.Reject:
                return normalized;
            default:
                throw new ArgumentException($"Unsupported bulk review action '{action}'.", nameof(action));
        }
    }

    private static async Task<CatalogueReviewPerson> RequireActivePersonAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT display_name
            FROM people
            WHERE id = $person_id
              AND merged_into_person_id IS NULL;
            """;
        command.Parameters.AddWithValue("$person_id", personId.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string displayName && !string.IsNullOrWhiteSpace(displayName)
            ? new CatalogueReviewPerson(personId, displayName)
            : throw new KeyNotFoundException($"Person {personId} was not found or is not active.");
    }

    private static async Task<FaceOccurrenceId[]> ReadEligibleFaceIdsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<FaceOccurrenceId> requestedIds,
        CancellationToken cancellationToken)
    {
        string[] parameterNames = requestedIds
            .Select((_, index) => $"$face_{index}")
            .ToArray();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                face_occurrences.id,
                CASE WHEN EXISTS (
                    SELECT 1
                    FROM review_actions
                    WHERE review_actions.face_occurrence_id = face_occurrences.id
                      AND review_actions.action_kind IN ('assign', 'unknown', 'reject')
                      AND review_actions.reversed_at_utc IS NULL)
                THEN 0 ELSE 1 END AS is_eligible
            FROM face_occurrences
            WHERE face_occurrences.id IN ({string.Join(", ", parameterNames)});
            """;
        for (int index = 0; index < requestedIds.Count; index++)
        {
            command.Parameters.AddWithValue(parameterNames[index], requestedIds[index].ToString());
        }

        Dictionary<FaceOccurrenceId, bool> eligibility = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                FaceOccurrenceId id = FaceOccurrenceId.From(Guid.Parse(reader.GetString(0)));
                eligibility.Add(id, reader.GetInt32(1) == 1);
            }
        }

        if (eligibility.Count != requestedIds.Count)
        {
            FaceOccurrenceId missing = requestedIds.First(id => !eligibility.ContainsKey(id));
            throw new KeyNotFoundException($"Face occurrence {missing} was not found.");
        }

        return requestedIds.Where(id => eligibility[id]).ToArray();
    }

    private static string CreatePreviewToken(
        string action,
        PersonId? personId,
        IReadOnlyCollection<FaceOccurrenceId> requestedIds,
        IReadOnlyCollection<FaceOccurrenceId> eligibleIds)
    {
        StringBuilder value = new();
        value.Append(action).Append('\n');
        value.Append(personId?.ToString() ?? string.Empty).Append('\n');
        value.Append("requested").Append('\n');
        foreach (FaceOccurrenceId id in requestedIds)
        {
            value.Append(id).Append('\n');
        }

        value.Append("eligible").Append('\n');
        foreach (FaceOccurrenceId id in eligibleIds)
        {
            value.Append(id).Append('\n');
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
            throw new ArgumentException("The bulk review preview token is invalid.", nameof(previewToken));
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

    private static async Task InsertReviewActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        string action,
        PersonId? personId,
        long? personLabelId,
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
                $action,
                $person_id,
                $person_label_id,
                $actor,
                $note,
                $created_at_utc,
                NULL,
                NULL);
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$person_id", personId is PersonId value ? value.ToString() : DBNull.Value);
        command.Parameters.AddWithValue("$person_label_id", (object?)personLabelId ?? DBNull.Value);
        command.Parameters.AddWithValue("$actor", actor);
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at_utc", Format(createdAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
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
}
