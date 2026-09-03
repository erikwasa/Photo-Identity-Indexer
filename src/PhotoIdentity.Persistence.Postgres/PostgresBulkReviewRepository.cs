using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL preview/commit persistence for bounded bulk face review.
/// </summary>
public sealed class PostgresBulkReviewRepository :
    IBulkReviewRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresBulkReviewRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<BulkReviewPreview> PreviewAsync(
        IReadOnlyCollection<FaceOccurrenceId> faceOccurrenceIds,
        string action,
        PersonId? personId,
        CancellationToken cancellationToken = default)
    {
        FaceOccurrenceId[] requestedIds =
            NormalizeFaceIds(faceOccurrenceIds);
        string normalizedAction =
            NormalizeAction(action, personId);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        ReviewPerson? person =
            normalizedAction == BulkReviewActionKinds.Assign
                ? await RequireActivePersonAsync(
                    connection,
                    transaction,
                    personId!.Value,
                    cancellationToken)
                : null;

        FaceOccurrenceId[] eligibleIds =
            await ReadEligibleFaceIdsAsync(
                connection,
                transaction,
                requestedIds,
                cancellationToken);

        string previewToken = CreatePreviewToken(
            normalizedAction,
            personId,
            requestedIds,
            eligibleIds);

        await transaction.CommitAsync(cancellationToken);
        return new BulkReviewPreview(
            normalizedAction,
            requestedIds.Length,
            eligibleIds.Length,
            requestedIds.Length - eligibleIds.Length,
            previewToken,
            person);
    }

    public async Task<BulkReviewResult> CommitAsync(
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
        FaceOccurrenceId[] requestedIds =
            NormalizeFaceIds(faceOccurrenceIds);
        string normalizedAction =
            NormalizeAction(action, personId);
        string normalizedPreviewToken =
            NormalizePreviewToken(previewToken);
        string normalizedActor =
            Required(actor, nameof(actor));
        string? normalizedNote = Optional(note);

        if (expectedAffectedCount is < 1 or > BulkReviewLimits.MaximumFacesPerRequest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedAffectedCount),
                $"The expected affected count must be between 1 and {BulkReviewLimits.MaximumFacesPerRequest}.");
        }

        DateTimeOffset createdAt =
            createdAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await LockRequestedFacesAsync(
            connection,
            transaction,
            requestedIds,
            cancellationToken);

        ReviewPerson? person =
            normalizedAction == BulkReviewActionKinds.Assign
                ? await RequireActivePersonAsync(
                    connection,
                    transaction,
                    personId!.Value,
                    cancellationToken)
                : null;

        FaceOccurrenceId[] eligibleIds =
            await ReadEligibleFaceIdsAsync(
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
            !string.Equals(
                currentToken,
                normalizedPreviewToken,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The selected faces changed after the preview. Preview the bulk action again before committing.");
        }

        foreach (FaceOccurrenceId faceOccurrenceId in eligibleIds)
        {
            long? labelId =
                normalizedAction == BulkReviewActionKinds.Assign
                    ? await UpsertManualLabelAsync(
                        connection,
                        transaction,
                        faceOccurrenceId,
                        personId!.Value,
                        normalizedActor,
                        normalizedNote,
                        createdAt,
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
                createdAt,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new BulkReviewResult(
            normalizedAction,
            requestedIds.Length,
            eligibleIds.Length,
            person,
            createdAt);
    }

    private static FaceOccurrenceId[] NormalizeFaceIds(
        IReadOnlyCollection<FaceOccurrenceId> faceOccurrenceIds)
    {
        ArgumentNullException.ThrowIfNull(faceOccurrenceIds);

        FaceOccurrenceId[] ids = faceOccurrenceIds
            .Distinct()
            .OrderBy(
                id => id.ToString(),
                StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
        {
            throw new ArgumentException(
                "Select at least one face for bulk review.",
                nameof(faceOccurrenceIds));
        }

        if (ids.Length > BulkReviewLimits.MaximumFacesPerRequest)
        {
            throw new ArgumentException(
                $"A bulk review request cannot contain more than {BulkReviewLimits.MaximumFacesPerRequest} faces.",
                nameof(faceOccurrenceIds));
        }

        return ids;
    }

    private static string NormalizeAction(
        string action,
        PersonId? personId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        string normalized =
            action.Trim().ToLowerInvariant();

        return normalized switch
        {
            BulkReviewActionKinds.Assign when personId is null =>
                throw new ArgumentException(
                    "A person is required for a bulk assignment.",
                    nameof(personId)),
            BulkReviewActionKinds.Unknown when personId is not null =>
                throw new ArgumentException(
                    "A bulk Unknown decision cannot include a person.",
                    nameof(personId)),
            BulkReviewActionKinds.Reject when personId is not null =>
                throw new ArgumentException(
                    "A bulk face rejection cannot include a person.",
                    nameof(personId)),
            BulkReviewActionKinds.Assign => normalized,
            BulkReviewActionKinds.Unknown => normalized,
            BulkReviewActionKinds.Reject => normalized,
            _ => throw new ArgumentException(
                $"Unsupported bulk review action '{action}'.",
                nameof(action)),
        };
    }

    private static async Task LockRequestedFacesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<FaceOccurrenceId> requestedIds,
        CancellationToken cancellationToken)
    {
        string[] parameterNames = requestedIds
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

        AddFaceParameters(
            command,
            requestedIds,
            parameterNames);

        List<FaceOccurrenceId> locked = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            locked.Add(
                FaceOccurrenceId.From(reader.GetGuid(0)));
        }

        if (locked.Count != requestedIds.Count)
        {
            FaceOccurrenceId missing =
                requestedIds.First(id => !locked.Contains(id));
            throw new KeyNotFoundException(
                $"Face occurrence {missing} was not found.");
        }
    }

    private static async Task<ReviewPerson> RequireActivePersonAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PersonId personId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT display_name
            FROM people
            WHERE id = @person_id
              AND merged_into_person_id IS NULL;
            """;
        command.Parameters.AddWithValue(
            "person_id",
            Guid.Parse(personId.ToString()));

        object? value =
            await command.ExecuteScalarAsync(cancellationToken);
        return value is string displayName &&
            !string.IsNullOrWhiteSpace(displayName)
            ? new ReviewPerson(personId, displayName)
            : throw new KeyNotFoundException(
                $"Person {personId} was not found or is not active.");
    }

    private static async Task<FaceOccurrenceId[]> ReadEligibleFaceIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<FaceOccurrenceId> requestedIds,
        CancellationToken cancellationToken)
    {
        string[] parameterNames = requestedIds
            .Select((_, index) => $"@face_{index}")
            .ToArray();

        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT
                face.id,
                NOT EXISTS (
                    SELECT 1
                    FROM review_actions AS action
                    WHERE action.face_occurrence_id = face.id
                      AND action.action_kind IN ('assign', 'unknown', 'reject')
                      AND action.reversed_at_utc IS NULL)
                    AS is_eligible
            FROM face_occurrences AS face
            WHERE face.id IN ({string.Join(", ", parameterNames)});
            """;

        AddFaceParameters(
            command,
            requestedIds,
            parameterNames);

        Dictionary<FaceOccurrenceId, bool> eligibility = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            eligibility.Add(
                FaceOccurrenceId.From(reader.GetGuid(0)),
                reader.GetBoolean(1));
        }

        if (eligibility.Count != requestedIds.Count)
        {
            FaceOccurrenceId missing =
                requestedIds.First(
                    id => !eligibility.ContainsKey(id));
            throw new KeyNotFoundException(
                $"Face occurrence {missing} was not found.");
        }

        return requestedIds
            .Where(id => eligibility[id])
            .ToArray();
    }

    private static void AddFaceParameters(
        NpgsqlCommand command,
        IReadOnlyList<FaceOccurrenceId> ids,
        IReadOnlyList<string> parameterNames)
    {
        for (int index = 0; index < ids.Count; index++)
        {
            command.Parameters.AddWithValue(
                parameterNames[index][1..],
                Guid.Parse(ids[index].ToString()));
        }
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
                "The bulk review preview token is invalid.",
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

    private static async Task InsertReviewActionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        string action,
        PersonId? personId,
        long? personLabelId,
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
                @action,
                @person_id,
                @person_label_id,
                @actor,
                @note,
                @created_at_utc,
                NULL,
                NULL);
            """;
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));
        command.Parameters.AddWithValue("action", action);

        NpgsqlParameter person =
            command.Parameters.Add(
                "person_id",
                NpgsqlDbType.Uuid);
        person.Value = personId is PersonId id
            ? Guid.Parse(id.ToString())
            : DBNull.Value;

        NpgsqlParameter label =
            command.Parameters.Add(
                "person_label_id",
                NpgsqlDbType.Bigint);
        label.Value = personLabelId is long value
            ? value
            : DBNull.Value;

        command.Parameters.AddWithValue("actor", actor);
        AddNullableText(command, "note", note);
        command.Parameters.AddWithValue(
            "created_at_utc",
            createdAtUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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
            command.Parameters.Add(
                name,
                NpgsqlDbType.Text);
        parameter.Value = value is null
            ? DBNull.Value
            : value;
    }
}
