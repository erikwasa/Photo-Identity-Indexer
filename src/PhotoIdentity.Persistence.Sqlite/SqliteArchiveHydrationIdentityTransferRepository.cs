using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Moves durable Photo-Identity hydration ownership from a now-suspect immutable revision back to
/// its source asset before authoritative re-verification. The source verifier later transfers that
/// same ownership to the revision established by SHA-256, so accounting/release responsibility is
/// not lost when content identity changes under a managed local file.
/// </summary>
public sealed class SqliteArchiveHydrationIdentityTransferRepository : IArchiveHydrationIdentityTransferRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveHydrationIdentityTransferRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<bool> MoveActiveRevisionLeaseToSourceAsync(
        AssetId assetId,
        DateTimeOffset transferredAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemasAsync(cancellationToken);
        AssetRevisionId? managedRevisionId;
        await using (SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT hydration.asset_revision_id
                FROM asset_revision_managed_hydrations AS hydration
                INNER JOIN asset_revisions AS revision
                    ON revision.id = hydration.asset_revision_id
                WHERE revision.asset_id = $asset_id
                  AND hydration.released_at_utc IS NULL
                  AND hydration.release_requested_at_utc IS NULL
                ORDER BY hydration.requested_at_utc DESC, hydration.asset_revision_id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$asset_id", assetId.ToString());
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            managedRevisionId = value is string id
                ? AssetRevisionId.From(Guid.Parse(id))
                : null;
        }

        return managedRevisionId is AssetRevisionId revisionId &&
            await MoveRevisionLeaseToSourceCoreAsync(
                revisionId,
                assetId,
                transferredAtUtc,
                cancellationToken);
    }

    public async Task<bool> MoveRevisionLeaseToSourceAsync(
        AssetRevisionId revisionId,
        AssetId assetId,
        DateTimeOffset transferredAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemasAsync(cancellationToken);
        return await MoveRevisionLeaseToSourceCoreAsync(
            revisionId,
            assetId,
            transferredAtUtc,
            cancellationToken);
    }

    private async Task<bool> MoveRevisionLeaseToSourceCoreAsync(
        AssetRevisionId revisionId,
        AssetId assetId,
        DateTimeOffset transferredAtUtc,
        CancellationToken cancellationToken)
    {
        DateTimeOffset transferredAt = transferredAtUtc.ToUniversalTime();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        DateTimeOffset? requestedAt = null;
        DateTimeOffset? lastNeededAt = null;
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT
                    hydration.requested_at_utc,
                    COALESCE(usage.last_needed_at_utc, hydration.requested_at_utc)
                FROM asset_revision_managed_hydrations AS hydration
                INNER JOIN asset_revisions AS revision
                    ON revision.id = hydration.asset_revision_id
                LEFT JOIN asset_revision_managed_hydration_usage AS usage
                    ON usage.asset_revision_id = hydration.asset_revision_id
                WHERE hydration.asset_revision_id = $asset_revision_id
                  AND revision.asset_id = $asset_id
                  AND hydration.released_at_utc IS NULL
                  AND hydration.release_requested_at_utc IS NULL;
                """;
            read.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
            read.Parameters.AddWithValue("$asset_id", assetId.ToString());
            await using SqliteDataReader reader = await read.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                requestedAt = Parse(reader.GetString(0));
                lastNeededAt = Parse(reader.GetString(1));
            }
        }

        if (requestedAt is null || lastNeededAt is null)
        {
            transaction.Commit();
            return false;
        }

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
                    release_requested_at_utc = NULL,
                    released_at_utc = NULL;

                INSERT INTO archive_source_managed_hydration_usage (asset_id, last_needed_at_utc)
                VALUES ($asset_id, $last_needed_at_utc)
                ON CONFLICT(asset_id) DO UPDATE SET
                    last_needed_at_utc = CASE
                        WHEN archive_source_managed_hydration_usage.last_needed_at_utc > excluded.last_needed_at_utc
                            THEN archive_source_managed_hydration_usage.last_needed_at_utc
                        ELSE excluded.last_needed_at_utc
                    END;

                UPDATE asset_revision_managed_hydrations
                SET released_at_utc = $transferred_at_utc
                WHERE asset_revision_id = $asset_revision_id
                  AND released_at_utc IS NULL
                  AND release_requested_at_utc IS NULL;
                """;
            command.Parameters.AddWithValue("$asset_id", assetId.ToString());
            command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
            command.Parameters.AddWithValue("$requested_at_utc", Format(requestedAt.Value));
            command.Parameters.AddWithValue("$last_needed_at_utc", Format(lastNeededAt.Value));
            command.Parameters.AddWithValue("$transferred_at_utc", Format(transferredAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return true;
    }

    private async Task EnsureSchemasAsync(CancellationToken cancellationToken)
    {
        // Force both lazy schemas to exist outside transfer transactions.
        _ = await new SqliteArchiveHydrationRepository(_database).GetActiveLeasesAsync(cancellationToken);
        _ = await new SqliteArchiveSourceHydrationRepository(_database).GetActiveLeasesAsync(cancellationToken);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
