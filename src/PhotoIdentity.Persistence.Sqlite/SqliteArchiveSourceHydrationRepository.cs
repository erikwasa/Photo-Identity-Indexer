using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record ArchiveManagedSourceHydrationRecord(
    AssetId AssetId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ReleaseRequestedAtUtc,
    DateTimeOffset? ReleasedAtUtc)
{
    public bool IsActive => ReleasedAtUtc is null;
    public bool IsReleaseRequested => IsActive && ReleaseRequestedAtUtc is not null;
}

public sealed record ArchiveManagedSourceHydrationLease(
    AssetId AssetId,
    long SizeBytes,
    string RootLocator,
    string SourceKey,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset LastNeededAtUtc,
    DateTimeOffset? ReleaseRequestedAtUtc)
{
    public bool IsReleaseRequested => ReleaseRequestedAtUtc is not null;
}

/// <summary>
/// Tracks temporary Photo-Identity ownership for an archive source that does not yet have a usable
/// immutable revision. Once SHA-256 verification establishes/reselects the revision, ownership can
/// be transferred atomically to the revision-level hydration record used by normal analysis.
/// </summary>
public sealed class SqliteArchiveSourceHydrationRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveSourceHydrationRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ArchiveManagedSourceHydrationRecord?> GetAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, assetId, cancellationToken);
    }

    public async Task<IReadOnlyList<ArchiveManagedSourceHydrationLease>> GetActiveLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                hydration.asset_id,
                observation.observed_size_bytes,
                source.root_locator,
                asset.source_key,
                hydration.requested_at_utc,
                COALESCE(usage.last_needed_at_utc, hydration.requested_at_utc),
                hydration.release_requested_at_utc
            FROM archive_source_managed_hydrations AS hydration
            INNER JOIN assets AS asset ON asset.id = hydration.asset_id
            INNER JOIN sources AS source ON source.id = asset.source_id
            INNER JOIN archive_source_observations AS observation ON observation.asset_id = asset.id
            LEFT JOIN archive_source_managed_hydration_usage AS usage ON usage.asset_id = hydration.asset_id
            WHERE hydration.released_at_utc IS NULL
            ORDER BY COALESCE(usage.last_needed_at_utc, hydration.requested_at_utc), hydration.asset_id;
            """;

        List<ArchiveManagedSourceHydrationLease> leases = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            leases.Add(new ArchiveManagedSourceHydrationLease(
                AssetId.From(Guid.Parse(reader.GetString(0))),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                Parse(reader.GetString(4)),
                Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : Parse(reader.GetString(6))));
        }

        return leases;
    }

    public async Task<ArchiveManagedSourceHydrationRecord> ClaimAsync(
        AssetId assetId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        DateTimeOffset requestedAt = requestedAtUtc.ToUniversalTime();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO archive_source_managed_hydrations (
                    asset_id, requested_at_utc, release_requested_at_utc, released_at_utc)
                VALUES ($asset_id, $requested_at_utc, NULL, NULL)
                ON CONFLICT(asset_id) DO UPDATE SET
                    requested_at_utc = CASE
                        WHEN archive_source_managed_hydrations.released_at_utc IS NULL
                            THEN archive_source_managed_hydrations.requested_at_utc
                        ELSE excluded.requested_at_utc
                    END,
                    release_requested_at_utc = CASE
                        WHEN archive_source_managed_hydrations.released_at_utc IS NULL
                            THEN archive_source_managed_hydrations.release_requested_at_utc
                        ELSE NULL
                    END,
                    released_at_utc = NULL;
                """;
            command.Parameters.AddWithValue("$asset_id", assetId.ToString());
            command.Parameters.AddWithValue("$requested_at_utc", Format(requestedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpsertLastNeededAsync(connection, transaction, assetId, requestedAt, cancellationToken);
        transaction.Commit();
        return await ReadAsync(connection, assetId, cancellationToken)
            ?? throw new InvalidOperationException("Managed source hydration ownership was unavailable after persistence.");
    }

    public async Task TouchAsync(
        AssetId assetId,
        DateTimeOffset neededAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand owned = connection.CreateCommand())
        {
            owned.Transaction = transaction;
            owned.CommandText = """
                SELECT COUNT(*)
                FROM archive_source_managed_hydrations
                WHERE asset_id = $asset_id AND released_at_utc IS NULL;
                """;
            owned.Parameters.AddWithValue("$asset_id", assetId.ToString());
            long count = (long)(await owned.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (count == 0)
            {
                transaction.Commit();
                return;
            }
        }

        await UpsertLastNeededAsync(connection, transaction, assetId, neededAtUtc, cancellationToken);
        transaction.Commit();
    }

    public async Task MarkReleaseRequestedAsync(
        AssetId assetId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE archive_source_managed_hydrations
            SET release_requested_at_utc = COALESCE(release_requested_at_utc, $requested_at_utc)
            WHERE asset_id = $asset_id AND released_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        command.Parameters.AddWithValue("$requested_at_utc", Format(requestedAtUtc));
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException(
                "The source hydration is not owned by Photo Identity and cannot be released automatically.");
        }
    }

    public async Task MarkReleasedAsync(
        AssetId assetId,
        DateTimeOffset releasedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE archive_source_managed_hydrations
            SET released_at_utc = $released_at_utc
            WHERE asset_id = $asset_id AND released_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        command.Parameters.AddWithValue("$released_at_utc", Format(releasedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TransferToRevisionAsync(
        AssetId assetId,
        AssetRevisionId revisionId,
        DateTimeOffset transferredAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        // Ensure the revision hydration tables exist before entering our transaction.
        _ = await new SqliteArchiveHydrationRepository(_database).GetActiveLeasesAsync(cancellationToken);

        DateTimeOffset transferredAt = transferredAtUtc.ToUniversalTime();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        DateTimeOffset? requestedAt = null;
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT requested_at_utc
                FROM archive_source_managed_hydrations
                WHERE asset_id = $asset_id
                  AND released_at_utc IS NULL
                  AND release_requested_at_utc IS NULL;
                """;
            read.Parameters.AddWithValue("$asset_id", assetId.ToString());
            object? value = await read.ExecuteScalarAsync(cancellationToken);
            if (value is string timestamp)
            {
                requestedAt = Parse(timestamp);
            }
        }

        if (requestedAt is null)
        {
            transaction.Commit();
            return false;
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO asset_revision_managed_hydrations (
                    asset_revision_id, requested_at_utc, release_requested_at_utc, released_at_utc)
                VALUES ($asset_revision_id, $requested_at_utc, NULL, NULL)
                ON CONFLICT(asset_revision_id) DO UPDATE SET
                    requested_at_utc = CASE
                        WHEN asset_revision_managed_hydrations.released_at_utc IS NULL
                            THEN asset_revision_managed_hydrations.requested_at_utc
                        ELSE excluded.requested_at_utc
                    END,
                    release_requested_at_utc = CASE
                        WHEN asset_revision_managed_hydrations.released_at_utc IS NULL
                            THEN asset_revision_managed_hydrations.release_requested_at_utc
                        ELSE NULL
                    END,
                    released_at_utc = NULL;

                INSERT INTO asset_revision_managed_hydration_usage (asset_revision_id, last_needed_at_utc)
                VALUES ($asset_revision_id, $last_needed_at_utc)
                ON CONFLICT(asset_revision_id) DO UPDATE SET
                    last_needed_at_utc = excluded.last_needed_at_utc;

                UPDATE archive_source_managed_hydrations
                SET released_at_utc = $transferred_at_utc
                WHERE asset_id = $asset_id AND released_at_utc IS NULL;
                """;
            command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
            command.Parameters.AddWithValue("$requested_at_utc", Format(requestedAt.Value));
            command.Parameters.AddWithValue("$last_needed_at_utc", Format(transferredAt));
            command.Parameters.AddWithValue("$transferred_at_utc", Format(transferredAt));
            command.Parameters.AddWithValue("$asset_id", assetId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return true;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await new SqliteArchiveSourceObservationRepository(_database).EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS archive_source_managed_hydrations (
                asset_id TEXT NOT NULL PRIMARY KEY,
                requested_at_utc TEXT NOT NULL,
                release_requested_at_utc TEXT NULL,
                released_at_utc TEXT NULL,
                FOREIGN KEY (asset_id) REFERENCES assets (id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_archive_source_managed_hydrations_active
                ON archive_source_managed_hydrations (released_at_utc, asset_id);

            CREATE TABLE IF NOT EXISTS archive_source_managed_hydration_usage (
                asset_id TEXT NOT NULL PRIMARY KEY,
                last_needed_at_utc TEXT NOT NULL,
                FOREIGN KEY (asset_id) REFERENCES assets (id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertLastNeededAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetId assetId,
        DateTimeOffset neededAtUtc,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO archive_source_managed_hydration_usage (asset_id, last_needed_at_utc)
            VALUES ($asset_id, $last_needed_at_utc)
            ON CONFLICT(asset_id) DO UPDATE SET
                last_needed_at_utc = excluded.last_needed_at_utc;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        command.Parameters.AddWithValue("$last_needed_at_utc", Format(neededAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ArchiveManagedSourceHydrationRecord?> ReadAsync(
        SqliteConnection connection,
        AssetId assetId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_id, requested_at_utc, release_requested_at_utc, released_at_utc
            FROM archive_source_managed_hydrations
            WHERE asset_id = $asset_id;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ArchiveManagedSourceHydrationRecord(
            AssetId.From(Guid.Parse(reader.GetString(0))),
            Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : Parse(reader.GetString(2)),
            reader.IsDBNull(3) ? null : Parse(reader.GetString(3)));
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
