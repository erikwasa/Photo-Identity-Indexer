using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record ArchiveAdvancementState(
    SourceId SourceId,
    string DesiredState,
    string RuntimeState,
    bool SyncRequired,
    string? Message,
    DateTimeOffset UpdatedAtUtc)
{
    public bool IsRequested => string.Equals(DesiredState, "running", StringComparison.Ordinal);
}

public sealed class SqliteArchiveAdvancementRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveAdvancementRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS archive_advancement_control (
                source_id TEXT NOT NULL PRIMARY KEY,
                desired_state TEXT NOT NULL,
                runtime_state TEXT NOT NULL,
                sync_required INTEGER NOT NULL,
                message TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (source_id) REFERENCES sources (id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ArchiveAdvancementState?> GetAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT desired_state, runtime_state, sync_required, message, updated_at_utc
            FROM archive_advancement_control
            WHERE source_id = $source_id;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ArchiveAdvancementState(
            sourceId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2) != 0,
            reader.IsDBNull(3) ? null : reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    public Task RequestRunAsync(
        SourceId sourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(sourceId, "running", "queued", syncRequired: true, null, now, cancellationToken);

    public Task PauseAsync(
        SourceId sourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(sourceId, "paused", "paused", syncRequired: false, "Archive advancement was paused by the operator.", now, cancellationToken);

    public async Task UpdateRuntimeAsync(
        SourceId sourceId,
        string runtimeState,
        bool? syncRequired,
        string? message,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeState);
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE archive_advancement_control
            SET runtime_state = $runtime_state,
                sync_required = COALESCE($sync_required, sync_required),
                message = $message,
                updated_at_utc = $updated_at_utc
            WHERE source_id = $source_id;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$runtime_state", runtimeState);
        command.Parameters.AddWithValue("$sync_required", syncRequired is null ? DBNull.Value : syncRequired.Value ? 1 : 0);
        command.Parameters.AddWithValue("$message", message is null ? DBNull.Value : message);
        command.Parameters.AddWithValue("$updated_at_utc", now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task CompleteAsync(
        SourceId sourceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(sourceId, "paused", "complete", syncRequired: false, "Archive advancement completed.", now, cancellationToken);

    public Task BlockAsync(
        SourceId sourceId,
        string message,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(sourceId, "paused", "blocked", syncRequired: false, message, now, cancellationToken);

    private async Task UpsertAsync(
        SourceId sourceId,
        string desiredState,
        string runtimeState,
        bool syncRequired,
        string? message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO archive_advancement_control (
                source_id, desired_state, runtime_state, sync_required, message, updated_at_utc)
            VALUES ($source_id, $desired_state, $runtime_state, $sync_required, $message, $updated_at_utc)
            ON CONFLICT(source_id) DO UPDATE SET
                desired_state = excluded.desired_state,
                runtime_state = excluded.runtime_state,
                sync_required = excluded.sync_required,
                message = excluded.message,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$desired_state", desiredState);
        command.Parameters.AddWithValue("$runtime_state", runtimeState);
        command.Parameters.AddWithValue("$sync_required", syncRequired ? 1 : 0);
        command.Parameters.AddWithValue("$message", message is null ? DBNull.Value : message);
        command.Parameters.AddWithValue("$updated_at_utc", now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
