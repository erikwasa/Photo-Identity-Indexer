using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

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

/// <summary>
/// Records only hydration initiated by Photo Identity. Pre-existing local or user-pinned content
/// never receives an active record, which makes release permission fail closed after restarts.
/// </summary>
public sealed class SqliteArchiveHydrationRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveHydrationRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ArchiveManagedHydrationRecord?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, revisionId, cancellationToken);
    }

    public async Task<ArchiveManagedHydrationRecord> ClaimAsync(
        AssetRevisionId revisionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
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
        return await ReadAsync(connection, revisionId, cancellationToken)
            ?? throw new InvalidOperationException("Managed hydration ownership was unavailable after persistence.");
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
            """;
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

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
