using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record ArchiveManagedHydrationRecord(
    AssetRevisionId AssetRevisionId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ReleaseRequestedAtUtc,
    DateTimeOffset? ReleasedAtUtc)
{
    public bool IsActive => ReleasedAtUtc is null;
    public bool IsReleaseRequested => IsActive && ReleaseRequestedAtUtc is not null;
}

public sealed record ArchiveManagedHydrationLease(
    AssetRevisionId AssetRevisionId,
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
/// Records only hydration initiated by Photo Identity. Pre-existing local or user-pinned content
/// never receives an active record, which makes release permission fail closed after restarts.
/// A separate usage table retains the last time managed content was actually needed so bounded
/// storage eviction can prefer the least-recently-needed managed originals.
/// </summary>
public sealed class SqliteArchiveHydrationRepository : IArchiveHydrationRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveHydrationRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    async Task<ArchiveManagedHydrationState?> IArchiveHydrationRepository.GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        ArchiveManagedHydrationRecord? value =
            await GetAsync(revisionId, cancellationToken);
        return value is null ? null : ToCoreState(value);
    }

    async Task<IReadOnlyList<ArchiveManagedHydrationLeaseState>>
        IArchiveHydrationRepository.GetActiveLeasesAsync(
            CancellationToken cancellationToken)
    {
        IReadOnlyList<ArchiveManagedHydrationLease> values =
            await GetActiveLeasesAsync(cancellationToken);
        return values.Select(ToCoreLease).ToArray();
    }

    async Task<ArchiveManagedHydrationState> IArchiveHydrationRepository.ClaimAsync(
        AssetRevisionId revisionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken) =>
        ToCoreState(await ClaimAsync(
            revisionId,
            requestedAtUtc,
            cancellationToken));

    async Task<ArchiveManagedHydrationState>
        IArchiveHydrationRepository.MarkReleaseRequestedAsync(
            AssetRevisionId revisionId,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken) =>
        ToCoreState(await MarkReleaseRequestedAsync(
            revisionId,
            requestedAtUtc,
            cancellationToken));

    public async Task<ArchiveManagedHydrationRecord?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, revisionId, cancellationToken);
    }

    public async Task<IReadOnlyList<ArchiveManagedHydrationLease>> GetActiveLeasesAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                hydration.asset_revision_id,
                revision.asset_id,
                revision.size_bytes,
                source.root_locator,
                asset.source_key,
                hydration.requested_at_utc,
                COALESCE(usage.last_needed_at_utc, hydration.requested_at_utc),
                hydration.release_requested_at_utc
            FROM asset_revision_managed_hydrations AS hydration
            INNER JOIN asset_revisions AS revision ON revision.id = hydration.asset_revision_id
            INNER JOIN assets AS asset ON asset.id = revision.asset_id
            INNER JOIN sources AS source ON source.id = asset.source_id
            LEFT JOIN asset_revision_managed_hydration_usage AS usage
                ON usage.asset_revision_id = hydration.asset_revision_id
            WHERE hydration.released_at_utc IS NULL
            ORDER BY COALESCE(usage.last_needed_at_utc, hydration.requested_at_utc), hydration.asset_revision_id;
            """;

        List<ArchiveManagedHydrationLease> leases = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            leases.Add(new ArchiveManagedHydrationLease(
                AssetRevisionId.From(Guid.Parse(reader.GetString(0))),
                AssetId.From(Guid.Parse(reader.GetString(1))),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                Parse(reader.GetString(5)),
                Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : Parse(reader.GetString(7))));
        }

        return leases;
    }

    public async Task<ArchiveManagedHydrationRecord> ClaimAsync(
        AssetRevisionId revisionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO asset_revision_managed_hydrations (
                    asset_revision_id,
                    requested_at_utc,
                    release_requested_at_utc,
                    released_at_utc)
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
                """;
            command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
            command.Parameters.AddWithValue("$requested_at_utc", Format(requestedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpsertLastNeededAsync(connection, transaction, revisionId, requestedAtUtc, cancellationToken);
        transaction.Commit();
        return await ReadAsync(connection, revisionId, cancellationToken)
            ?? throw new InvalidOperationException("Managed hydration ownership was unavailable after persistence.");
    }

    public async Task TouchAsync(
        AssetRevisionId revisionId,
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
                FROM asset_revision_managed_hydrations
                WHERE asset_revision_id = $asset_revision_id
                  AND released_at_utc IS NULL;
                """;
            owned.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
            long count = (long)(await owned.ExecuteScalarAsync(cancellationToken) ?? 0L);
            if (count == 0)
            {
                transaction.Commit();
                return;
            }
        }

        await UpsertLastNeededAsync(connection, transaction, revisionId, neededAtUtc, cancellationToken);
        transaction.Commit();
    }

    public async Task<ArchiveManagedHydrationRecord> MarkReleaseRequestedAsync(
        AssetRevisionId revisionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE asset_revision_managed_hydrations
            SET release_requested_at_utc = COALESCE(release_requested_at_utc, $requested_at_utc)
            WHERE asset_revision_id = $asset_revision_id
              AND released_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$requested_at_utc", Format(requestedAtUtc));
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException(
                "The original is not owned by Photo Identity and cannot be released automatically.");
        }

        return await ReadAsync(connection, revisionId, cancellationToken)
            ?? throw new InvalidOperationException("Managed hydration ownership was unavailable after release request.");
    }

    public async Task MarkReleasedAsync(
        AssetRevisionId revisionId,
        DateTimeOffset releasedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE asset_revision_managed_hydrations
            SET released_at_utc = $released_at_utc
            WHERE asset_revision_id = $asset_revision_id
              AND released_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$released_at_utc", Format(releasedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS asset_revision_managed_hydrations (
                asset_revision_id TEXT NOT NULL PRIMARY KEY,
                requested_at_utc TEXT NOT NULL,
                release_requested_at_utc TEXT NULL,
                released_at_utc TEXT NULL,
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_asset_revision_managed_hydrations_active
                ON asset_revision_managed_hydrations (released_at_utc, asset_revision_id);

            CREATE TABLE IF NOT EXISTS asset_revision_managed_hydration_usage (
                asset_revision_id TEXT NOT NULL PRIMARY KEY,
                last_needed_at_utc TEXT NOT NULL,
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertLastNeededAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId revisionId,
        DateTimeOffset neededAtUtc,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO asset_revision_managed_hydration_usage (asset_revision_id, last_needed_at_utc)
            VALUES ($asset_revision_id, $last_needed_at_utc)
            ON CONFLICT(asset_revision_id) DO UPDATE SET
                last_needed_at_utc = excluded.last_needed_at_utc;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$last_needed_at_utc", Format(neededAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<ArchiveManagedHydrationRecord?> ReadAsync(
        SqliteConnection connection,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_revision_id, requested_at_utc, release_requested_at_utc, released_at_utc
            FROM asset_revision_managed_hydrations
            WHERE asset_revision_id = $asset_revision_id;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ArchiveManagedHydrationRecord(
            AssetRevisionId.From(Guid.Parse(reader.GetString(0))),
            Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : Parse(reader.GetString(2)),
            reader.IsDBNull(3) ? null : Parse(reader.GetString(3)));
    }

    private static ArchiveManagedHydrationState ToCoreState(
        ArchiveManagedHydrationRecord value) => new(
        value.AssetRevisionId,
        value.RequestedAtUtc,
        value.ReleaseRequestedAtUtc,
        value.ReleasedAtUtc);

    private static ArchiveManagedHydrationLeaseState ToCoreLease(
        ArchiveManagedHydrationLease value) => new(
        value.AssetRevisionId,
        value.AssetId,
        value.SizeBytes,
        value.RootLocator,
        value.SourceKey,
        value.RequestedAtUtc,
        value.LastNeededAtUtc,
        value.ReleaseRequestedAtUtc);

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
