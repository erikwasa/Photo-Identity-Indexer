using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresSourceCopyExclusionRepository : ISourceCopyExclusionRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly Lazy<Task> _schema;

    public PostgresSourceCopyExclusionRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _schema = new Lazy<Task>(() => EnsureSchemaCoreAsync(CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<SourceCopyExclusionState?> GetAsync(
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        string key = SourceCopyLocator.NormalizeSourceKey(sourceKey);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_id, source_key, excluded_at_utc, last_seen_at_utc,
                   purge_state, purge_error_code, purge_updated_at_utc
            FROM source_copy_exclusions
            WHERE source_id = @source_id AND source_key = @source_key;
            """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);
        command.Parameters.AddWithValue("source_key", key);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<SourceCopyExclusionState>> ListAsync(
        SourceId? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sourceId is null
            ? """
              SELECT source_id, source_key, excluded_at_utc, last_seen_at_utc,
                     purge_state, purge_error_code, purge_updated_at_utc
              FROM source_copy_exclusions
              ORDER BY excluded_at_utc DESC, source_id, source_key;
              """
            : """
              SELECT source_id, source_key, excluded_at_utc, last_seen_at_utc,
                     purge_state, purge_error_code, purge_updated_at_utc
              FROM source_copy_exclusions
              WHERE source_id = @source_id
              ORDER BY excluded_at_utc DESC, source_key;
              """;
        if (sourceId is SourceId id)
        {
            command.Parameters.AddWithValue("source_id", id.Value);
        }

        List<SourceCopyExclusionState> values = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(Read(reader));
        }
        return values;
    }

    public async Task<SourceCopyExclusionState> ExcludeAsync(
        SourceId sourceId,
        string sourceKey,
        DateTimeOffset excludedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        string key = SourceCopyLocator.NormalizeSourceKey(sourceKey);
        DateTimeOffset now = excludedAtUtc.ToUniversalTime();
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO source_copy_exclusions (
                source_id, source_key, excluded_at_utc, last_seen_at_utc,
                purge_state, purge_error_code, purge_updated_at_utc)
            VALUES (@source_id, @source_key, @excluded_at_utc, NULL, 'pending', NULL, @updated_at_utc)
            ON CONFLICT(source_id, source_key) DO UPDATE SET
                purge_state = 'pending',
                purge_error_code = NULL,
                purge_updated_at_utc = excluded.purge_updated_at_utc;
            """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);
        command.Parameters.AddWithValue("source_key", key);
        command.Parameters.AddWithValue("excluded_at_utc", now);
        command.Parameters.AddWithValue("updated_at_utc", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return await GetAsync(sourceId, key, cancellationToken)
            ?? throw new InvalidOperationException("Source-copy exclusion was not durable after persistence.");
    }

    public async Task<bool> RestoreAsync(
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        string key = SourceCopyLocator.NormalizeSourceKey(sourceKey);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM source_copy_exclusions
            WHERE source_id = @source_id
              AND source_key = @source_key
              AND purge_state = 'completed';
            """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);
        command.Parameters.AddWithValue("source_key", key);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> RecordObservedIfExcludedAsync(
        SourceId sourceId,
        string sourceKey,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        string key = SourceCopyLocator.NormalizeSourceKey(sourceKey);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE source_copy_exclusions
            SET last_seen_at_utc = @observed_at_utc
            WHERE source_id = @source_id AND source_key = @source_key;
            """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);
        command.Parameters.AddWithValue("source_key", key);
        command.Parameters.AddWithValue("observed_at_utc", observedAtUtc.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task SetPurgeStateAsync(
        SourceId sourceId,
        string sourceKey,
        string purgeState,
        string? errorCode,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!SourceCopyPurgeStates.IsValid(purgeState))
        {
            throw new ArgumentOutOfRangeException(nameof(purgeState));
        }
        if (purgeState != SourceCopyPurgeStates.Failed && errorCode is not null)
        {
            throw new ArgumentException("Only a failed purge may retain an error code.", nameof(errorCode));
        }

        await EnsureSchemaAsync(cancellationToken);
        string key = SourceCopyLocator.NormalizeSourceKey(sourceKey);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE source_copy_exclusions
            SET purge_state = @purge_state,
                purge_error_code = @purge_error_code,
                purge_updated_at_utc = @updated_at_utc
            WHERE source_id = @source_id AND source_key = @source_key;
            """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);
        command.Parameters.AddWithValue("source_key", key);
        command.Parameters.AddWithValue("purge_state", purgeState);
        command.Parameters.AddWithValue("purge_error_code", NpgsqlDbType.Text, (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("updated_at_utc", updatedAtUtc.ToUniversalTime());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Source-copy exclusion does not exist.");
        }
    }

    public Task<bool> IsAssetExcludedAsync(AssetId assetId, CancellationToken cancellationToken = default) =>
        ExistsByJoinAsync("asset.id = @id", assetId.Value, cancellationToken);

    public Task<bool> IsRevisionExcludedAsync(AssetRevisionId revisionId, CancellationToken cancellationToken = default) =>
        ExistsByJoinAsync("revision.id = @id", revisionId.Value, cancellationToken);

    public Task<bool> IsFaceOccurrenceExcludedAsync(FaceOccurrenceId faceOccurrenceId, CancellationToken cancellationToken = default) =>
        ExistsByJoinAsync("face.id = @id", faceOccurrenceId.Value, cancellationToken);

    private async Task<bool> ExistsByJoinAsync(string predicate, Guid id, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT EXISTS (
                SELECT 1
                FROM assets AS asset
                LEFT JOIN asset_revisions AS revision ON revision.asset_id = asset.id
                LEFT JOIN face_occurrences AS face ON face.asset_revision_id = revision.id
                INNER JOIN source_copy_exclusions AS exclusion
                    ON exclusion.source_id = asset.source_id
                   AND exclusion.source_key = asset.source_key
                WHERE {predicate});
            """;
        command.Parameters.AddWithValue("id", id);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is bool result && result;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken) =>
        await _schema.Value.WaitAsync(cancellationToken);

    private async Task EnsureSchemaCoreAsync(CancellationToken cancellationToken)
    {
        await _database.InitializeAsync(cancellationToken);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS source_copy_exclusions (
                source_id uuid NOT NULL,
                source_key text NOT NULL CHECK (btrim(source_key) <> ''),
                excluded_at_utc timestamp with time zone NOT NULL,
                last_seen_at_utc timestamp with time zone NULL,
                purge_state text NOT NULL CHECK (purge_state IN ('pending', 'attempting', 'failed', 'completed')),
                purge_error_code text NULL,
                purge_updated_at_utc timestamp with time zone NOT NULL,
                PRIMARY KEY (source_id, source_key),
                CONSTRAINT fk_source_copy_exclusions_source
                    FOREIGN KEY (source_id) REFERENCES sources (id) ON DELETE CASCADE,
                CHECK (purge_state = 'failed' OR purge_error_code IS NULL)
            );
            ALTER TABLE source_copy_exclusions
                DROP CONSTRAINT IF EXISTS source_copy_exclusions_purge_state_check;
            ALTER TABLE source_copy_exclusions
                ADD CONSTRAINT source_copy_exclusions_purge_state_check
                CHECK (purge_state IN ('pending', 'attempting', 'failed', 'completed'));
            CREATE INDEX IF NOT EXISTS ix_source_copy_exclusions_purge_state
                ON source_copy_exclusions (purge_state, purge_updated_at_utc, source_id, source_key);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SourceCopyExclusionState Read(NpgsqlDataReader reader) => new(
        SourceId.From(reader.GetGuid(0)),
        reader.GetString(1),
        reader.GetFieldValue<DateTimeOffset>(2).ToUniversalTime(),
        reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3).ToUniversalTime(),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetFieldValue<DateTimeOffset>(6).ToUniversalTime());
}
