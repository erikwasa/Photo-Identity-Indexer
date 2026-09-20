using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoCaptureDateRepository : IPhotoCaptureDateRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresPhotoCaptureDateRepository(
        PostgresCatalogueDatabase database,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _database = database;
        _timeProvider = timeProvider;
    }

    public async Task<PhotoCaptureDateState> GetStateAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction: null, revisionId, cancellationToken);
        return await ReadStateAsync(connection, revisionId, cancellationToken);
    }

    public async Task<PhotoCaptureDateState> SetManualAsync(
        AssetRevisionId revisionId,
        PhotoCaptureDateValue value,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalizedActor = NormalizeActor(actor);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);
        LatestAction? latest = await ReadLatestAsync(connection, transaction, revisionId, cancellationToken);
        if (latest?.ActionKind != PhotoCaptureDateActionKinds.Set || latest.Value != value)
        {
            await InsertAsync(
                connection,
                transaction,
                revisionId,
                PhotoCaptureDateActionKinds.Set,
                value,
                normalizedActor,
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await ReadStateAsync(connection, revisionId, cancellationToken);
    }

    public async Task<PhotoCaptureDateState> ClearManualAsync(
        AssetRevisionId revisionId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        string normalizedActor = NormalizeActor(actor);
        DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureRevisionExistsAsync(connection, transaction, revisionId, cancellationToken);
        LatestAction? latest = await ReadLatestAsync(connection, transaction, revisionId, cancellationToken);
        if (latest?.ActionKind == PhotoCaptureDateActionKinds.Set)
        {
            await InsertAsync(
                connection,
                transaction,
                revisionId,
                PhotoCaptureDateActionKinds.Clear,
                value: null,
                normalizedActor,
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await ReadStateAsync(connection, revisionId, cancellationToken);
    }

    private static async Task<PhotoCaptureDateState> ReadStateAsync(
        NpgsqlConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        DateTime? extractedTakenAtLocal;
        await using (NpgsqlCommand extracted = connection.CreateCommand())
        {
            extracted.CommandText = """
                SELECT taken_at_local
                FROM photo_capture_metadata
                WHERE asset_revision_id = @revision_id;
                """;
            extracted.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);
            object? value = await extracted.ExecuteScalarAsync(cancellationToken);
            extractedTakenAtLocal = value is null or DBNull
                ? null
                : DateTime.SpecifyKind((DateTime)value, DateTimeKind.Unspecified);
        }

        List<PhotoCaptureDateAction> history = [];
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    id,
                    action_kind,
                    precision,
                    capture_year,
                    capture_month,
                    capture_day,
                    actor,
                    created_at_utc
                FROM photo_capture_date_actions
                WHERE asset_revision_id = @revision_id
                ORDER BY id DESC;
                """;
            command.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string actionKind = reader.GetString(1);
                history.Add(new PhotoCaptureDateAction(
                    reader.GetInt64(0),
                    revisionId,
                    actionKind,
                    actionKind == PhotoCaptureDateActionKinds.Set ? ReadValue(reader, 2) : null,
                    reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7)));
            }
        }

        PhotoCaptureDateValue? manual = history.FirstOrDefault() is
            { ActionKind: PhotoCaptureDateActionKinds.Set, Value: not null } latest
                ? latest.Value
                : null;

        if (manual is not null)
        {
            return new PhotoCaptureDateState(
                revisionId,
                extractedTakenAtLocal,
                manual,
                manual.InclusiveRange,
                PhotoCaptureDateSources.Manual,
                history);
        }

        PhotoCaptureDateRange? extractedRange = extractedTakenAtLocal is null
            ? null
            : new PhotoCaptureDateRange(
                DateOnly.FromDateTime(extractedTakenAtLocal.Value),
                DateOnly.FromDateTime(extractedTakenAtLocal.Value));

        return new PhotoCaptureDateState(
            revisionId,
            extractedTakenAtLocal,
            null,
            extractedRange,
            extractedRange is null ? null : PhotoCaptureDateSources.Extracted,
            history);
    }

    private static async Task<LatestAction?> ReadLatestAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT action_kind, precision, capture_year, capture_month, capture_day
            FROM photo_capture_date_actions
            WHERE asset_revision_id = @revision_id
            ORDER BY id DESC
            LIMIT 1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        string actionKind = reader.GetString(0);
        return new LatestAction(
            actionKind,
            actionKind == PhotoCaptureDateActionKinds.Set ? ReadValue(reader, 1) : null);
    }

    private static PhotoCaptureDateValue ReadValue(NpgsqlDataReader reader, int precisionOrdinal)
    {
        string precision = reader.GetString(precisionOrdinal);
        int year = reader.GetInt16(precisionOrdinal + 1);
        int? month = reader.IsDBNull(precisionOrdinal + 2)
            ? null
            : reader.GetInt16(precisionOrdinal + 2);
        int? day = reader.IsDBNull(precisionOrdinal + 3)
            ? null
            : reader.GetInt16(precisionOrdinal + 3);

        return precision switch
        {
            PhotoCaptureDatePrecisions.Year when month is null && day is null =>
                new PhotoCaptureDateValue(year),
            PhotoCaptureDatePrecisions.Month when month is not null && day is null =>
                new PhotoCaptureDateValue(year, month),
            PhotoCaptureDatePrecisions.Day when month is not null && day is not null =>
                new PhotoCaptureDateValue(year, month, day),
            _ => throw new InvalidOperationException(
                $"Stored capture-date precision '{precision}' is inconsistent with its components."),
        };
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetRevisionId revisionId,
        string actionKind,
        PhotoCaptureDateValue? value,
        string actor,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO photo_capture_date_actions (
                asset_revision_id,
                action_kind,
                precision,
                capture_year,
                capture_month,
                capture_day,
                actor,
                created_at_utc)
            VALUES (
                @revision_id,
                @action_kind,
                @precision,
                @capture_year,
                @capture_month,
                @capture_day,
                @actor,
                @created_at_utc);
            """;
        command.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);
        command.Parameters.AddWithValue("action_kind", NpgsqlDbType.Text, actionKind);
        command.Parameters.Add("precision", NpgsqlDbType.Text).Value =
            value is null ? DBNull.Value : value.Precision;
        command.Parameters.Add("capture_year", NpgsqlDbType.Smallint).Value =
            value is null ? DBNull.Value : checked((short)value.Year);
        command.Parameters.Add("capture_month", NpgsqlDbType.Smallint).Value =
            value?.Month is int month ? checked((short)month) : DBNull.Value;
        command.Parameters.Add("capture_day", NpgsqlDbType.Smallint).Value =
            value?.Day is int day ? checked((short)day) : DBNull.Value;
        command.Parameters.AddWithValue("actor", NpgsqlDbType.Text, actor);
        command.Parameters.AddWithValue("created_at_utc", NpgsqlDbType.TimestampTz, createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureRevisionExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = transaction is null
            ? """
              SELECT 1
              FROM asset_revisions
              WHERE id = @revision_id;
              """
            : """
              SELECT 1
              FROM asset_revisions
              WHERE id = @revision_id
              FOR UPDATE;
              """;
        command.Parameters.AddWithValue("revision_id", NpgsqlDbType.Uuid, revisionId.Value);
        object? exists = await command.ExecuteScalarAsync(cancellationToken);
        if (exists is null)
        {
            throw new KeyNotFoundException($"Asset revision '{revisionId}' was not found.");
        }
    }

    private static string NormalizeActor(string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        string normalized = actor.Trim();
        if (normalized.Length > 120)
        {
            throw new ArgumentException(
                "Capture-date actor cannot exceed 120 characters.",
                nameof(actor));
        }

        return normalized;
    }

    private sealed record LatestAction(string ActionKind, PhotoCaptureDateValue? Value);
}
