using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL persistence for operator intent and runtime state of archive advancement.
/// </summary>
public sealed class PostgresArchiveAdvancementControlRepository :
    IArchiveAdvancementControlRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveAdvancementControlRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ArchiveAdvancementControlState?> GetAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                desired_state,
                runtime_state,
                sync_required,
                message,
                updated_at_utc
            FROM archive_advancement_control
            WHERE source_id = @source_id;
            """;
        command.Parameters.AddWithValue(
            "source_id",
            Guid.Parse(sourceId.ToString()));

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ArchiveAdvancementControlState(
            sourceId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetBoolean(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    public Task RequestRunAsync(
        SourceId sourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(
            sourceId,
            "running",
            "queued",
            syncRequired: true,
            message: null,
            now,
            cancellationToken);

    public Task PauseAsync(
        SourceId sourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(
            sourceId,
            "paused",
            "paused",
            syncRequired: false,
            "Archive advancement was paused by the operator.",
            now,
            cancellationToken);

    public async Task UpdateRuntimeAsync(
        SourceId sourceId,
        string runtimeState,
        bool? syncRequired,
        string? message,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeState);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE archive_advancement_control
            SET runtime_state = @runtime_state,
                sync_required = COALESCE(@sync_required, sync_required),
                message = @message,
                updated_at_utc = @updated_at_utc
            WHERE source_id = @source_id;
            """;

        command.Parameters.AddWithValue(
            "source_id",
            Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue(
            "runtime_state",
            runtimeState.Trim());

        NpgsqlParameter syncParameter =
            command.Parameters.Add(
                "sync_required",
                NpgsqlDbType.Boolean);
        syncParameter.Value = syncRequired is null
            ? DBNull.Value
            : syncRequired.Value;

        NpgsqlParameter messageParameter =
            command.Parameters.Add(
                "message",
                NpgsqlDbType.Text);
        messageParameter.Value = message is null
            ? DBNull.Value
            : message;

        command.Parameters.AddWithValue(
            "updated_at_utc",
            now.ToUniversalTime());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task CompleteAsync(
        SourceId sourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(
            sourceId,
            "paused",
            "complete",
            syncRequired: false,
            "Archive advancement completed.",
            now,
            cancellationToken);

    public Task BlockAsync(
        SourceId sourceId,
        string message,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return UpsertAsync(
            sourceId,
            "paused",
            "blocked",
            syncRequired: false,
            message.Trim(),
            now,
            cancellationToken);
    }

    private async Task UpsertAsync(
        SourceId sourceId,
        string desiredState,
        string runtimeState,
        bool syncRequired,
        string? message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO archive_advancement_control (
                source_id,
                desired_state,
                runtime_state,
                sync_required,
                message,
                updated_at_utc)
            VALUES (
                @source_id,
                @desired_state,
                @runtime_state,
                @sync_required,
                @message,
                @updated_at_utc)
            ON CONFLICT(source_id) DO UPDATE SET
                desired_state = excluded.desired_state,
                runtime_state = excluded.runtime_state,
                sync_required = excluded.sync_required,
                message = excluded.message,
                updated_at_utc = excluded.updated_at_utc;
            """;

        command.Parameters.AddWithValue(
            "source_id",
            Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("desired_state", desiredState);
        command.Parameters.AddWithValue("runtime_state", runtimeState);
        command.Parameters.AddWithValue("sync_required", syncRequired);

        NpgsqlParameter messageParameter =
            command.Parameters.Add(
                "message",
                NpgsqlDbType.Text);
        messageParameter.Value = message is null
            ? DBNull.Value
            : message;

        command.Parameters.AddWithValue(
            "updated_at_utc",
            now.ToUniversalTime());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
