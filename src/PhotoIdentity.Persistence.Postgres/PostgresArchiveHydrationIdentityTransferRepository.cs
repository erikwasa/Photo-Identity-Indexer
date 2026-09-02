using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresArchiveHydrationIdentityTransferRepository :
    IArchiveHydrationIdentityTransferRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveHydrationIdentityTransferRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<bool> MoveActiveRevisionLeaseToSourceAsync(
        AssetId assetId,
        DateTimeOffset transferredAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);

        AssetRevisionId? revisionId;
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT hydration.asset_revision_id
                FROM asset_revision_managed_hydrations AS hydration
                INNER JOIN asset_revisions AS revision
                    ON revision.id = hydration.asset_revision_id
                WHERE revision.asset_id = @asset_id
                  AND hydration.released_at_utc IS NULL
                  AND hydration.release_requested_at_utc IS NULL
                ORDER BY
                    hydration.requested_at_utc DESC,
                    hydration.asset_revision_id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "asset_id",
                Guid.Parse(assetId.ToString()));
            object? revisionValue =
                await command.ExecuteScalarAsync(cancellationToken);
            revisionId = revisionValue is Guid id
                ? AssetRevisionId.From(id)
                : null;
        }

        return revisionId is AssetRevisionId activeRevisionId &&
            await MoveRevisionLeaseToSourceAsync(
                activeRevisionId,
                assetId,
                transferredAtUtc,
                cancellationToken);
    }

    private static async Task UpsertTransferredSourceLeaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetId assetId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO archive_source_managed_hydrations (
                asset_id,
                requested_at_utc,
                release_requested_at_utc,
                released_at_utc)
            VALUES (
                @asset_id,
                @requested_at_utc,
                NULL,
                NULL)
            ON CONFLICT(asset_id) DO UPDATE SET
                requested_at_utc = CASE
                    WHEN archive_source_managed_hydrations.released_at_utc IS NULL
                        THEN archive_source_managed_hydrations.requested_at_utc
                    ELSE excluded.requested_at_utc
                END,
                release_requested_at_utc = NULL,
                released_at_utc = NULL;
            """;
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue(
            "requested_at_utc",
            requestedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> MoveRevisionLeaseToSourceAsync(
        AssetRevisionId revisionId,
        AssetId assetId,
        DateTimeOffset transferredAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset transferredAt =
            transferredAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        DateTimeOffset? requestedAt = null;
        DateTimeOffset? lastNeededAt = null;
        await using (NpgsqlCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                """
                SELECT
                    hydration.requested_at_utc,
                    COALESCE(
                        usage.last_needed_at_utc,
                        hydration.requested_at_utc)
                FROM asset_revision_managed_hydrations AS hydration
                INNER JOIN asset_revisions AS revision
                    ON revision.id = hydration.asset_revision_id
                LEFT JOIN asset_revision_managed_hydration_usage AS usage
                    ON usage.asset_revision_id = hydration.asset_revision_id
                WHERE hydration.asset_revision_id = @asset_revision_id
                  AND revision.asset_id = @asset_id
                  AND hydration.released_at_utc IS NULL
                  AND hydration.release_requested_at_utc IS NULL
                FOR UPDATE OF hydration;
                """;
            read.Parameters.AddWithValue(
                "asset_revision_id",
                Guid.Parse(revisionId.ToString()));
            read.Parameters.AddWithValue(
                "asset_id",
                Guid.Parse(assetId.ToString()));

            await using NpgsqlDataReader reader =
                await read.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                requestedAt =
                    reader.GetFieldValue<DateTimeOffset>(0);
                lastNeededAt =
                    reader.GetFieldValue<DateTimeOffset>(1);
            }
        }

        if (requestedAt is null || lastNeededAt is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await UpsertTransferredSourceLeaseAsync(
            connection,
            transaction,
            assetId,
            requestedAt.Value,
            cancellationToken);
        await PostgresArchiveSourceHydrationRepository.UpsertLastNeededAsync(
            connection,
            transaction,
            assetId,
            lastNeededAt.Value,
            preferLatest: true,
            cancellationToken: cancellationToken);

        await using (NpgsqlCommand close = connection.CreateCommand())
        {
            close.Transaction = transaction;
            close.CommandText =
                """
                UPDATE asset_revision_managed_hydrations
                SET released_at_utc = @transferred_at_utc
                WHERE asset_revision_id = @asset_revision_id
                  AND released_at_utc IS NULL
                  AND release_requested_at_utc IS NULL;
                """;
            close.Parameters.AddWithValue(
                "asset_revision_id",
                Guid.Parse(revisionId.ToString()));
            close.Parameters.AddWithValue(
                "transferred_at_utc",
                transferredAt);
            await close.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
