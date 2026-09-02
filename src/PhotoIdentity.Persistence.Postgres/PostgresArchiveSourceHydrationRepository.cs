using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresArchiveSourceHydrationRepository :
    IArchiveSourceHydrationRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveSourceHydrationRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ArchiveManagedSourceHydrationState?> GetAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(
            connection,
            transaction: null,
            assetId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ArchiveManagedSourceHydrationLeaseState>>
        GetActiveLeasesAsync(
            CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                hydration.asset_id,
                observation.observed_size_bytes,
                source.root_locator,
                asset.source_key,
                hydration.requested_at_utc,
                COALESCE(
                    usage.last_needed_at_utc,
                    hydration.requested_at_utc),
                hydration.release_requested_at_utc
            FROM archive_source_managed_hydrations AS hydration
            INNER JOIN assets AS asset
                ON asset.id = hydration.asset_id
            INNER JOIN sources AS source
                ON source.id = asset.source_id
            INNER JOIN archive_source_observations AS observation
                ON observation.asset_id = asset.id
            LEFT JOIN archive_source_managed_hydration_usage AS usage
                ON usage.asset_id = hydration.asset_id
            WHERE hydration.released_at_utc IS NULL
            ORDER BY
                COALESCE(
                    usage.last_needed_at_utc,
                    hydration.requested_at_utc),
                hydration.asset_id;
            """;

        List<ArchiveManagedSourceHydrationLeaseState> values = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new ArchiveManagedSourceHydrationLeaseState(
                AssetId.From(reader.GetGuid(0)),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return values;
    }

    public async Task<ArchiveManagedSourceHydrationState> ClaimAsync(
        AssetId assetId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset requestedAt =
            requestedAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await UpsertActiveSourceLeaseAsync(
            connection,
            transaction,
            assetId,
            requestedAt,
            cancellationToken);
        await UpsertLastNeededAsync(
            connection,
            transaction,
            assetId,
            requestedAt,
            preferLatest: false,
            cancellationToken);

        ArchiveManagedSourceHydrationState persisted =
            await ReadAsync(
                connection,
                transaction,
                assetId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Managed source hydration ownership was unavailable after persistence.");

        await transaction.CommitAsync(cancellationToken);
        return persisted;
    }

    public async Task TouchAsync(
        AssetId assetId,
        DateTimeOffset neededAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        bool active;
        await using (NpgsqlCommand owned = connection.CreateCommand())
        {
            owned.Transaction = transaction;
            owned.CommandText =
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM archive_source_managed_hydrations
                    WHERE asset_id = @asset_id
                      AND released_at_utc IS NULL);
                """;
            owned.Parameters.AddWithValue(
                "asset_id",
                Guid.Parse(assetId.ToString()));
            active =
                (bool)(await owned.ExecuteScalarAsync(cancellationToken)
                    ?? false);
        }

        if (active)
        {
            await UpsertLastNeededAsync(
                connection,
                transaction,
                assetId,
                neededAtUtc.ToUniversalTime(),
                preferLatest: false,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MarkReleaseRequestedAsync(
        AssetId assetId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.CommandText =
            """
            UPDATE archive_source_managed_hydrations
            SET release_requested_at_utc =
                COALESCE(
                    release_requested_at_utc,
                    @requested_at_utc)
            WHERE asset_id = @asset_id
              AND released_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue(
            "requested_at_utc",
            requestedAtUtc.ToUniversalTime());
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
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.CommandText =
            """
            UPDATE archive_source_managed_hydrations
            SET released_at_utc = @released_at_utc
            WHERE asset_id = @asset_id
              AND released_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue(
            "released_at_utc",
            releasedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TransferToRevisionAsync(
        AssetId assetId,
        AssetRevisionId revisionId,
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
        await using (NpgsqlCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                """
                SELECT requested_at_utc
                FROM archive_source_managed_hydrations
                WHERE asset_id = @asset_id
                  AND released_at_utc IS NULL
                  AND release_requested_at_utc IS NULL
                FOR UPDATE;
                """;
            read.Parameters.AddWithValue(
                "asset_id",
                Guid.Parse(assetId.ToString()));
            object? value =
                await read.ExecuteScalarAsync(cancellationToken);
            if (value is DateTimeOffset timestamp)
            {
                requestedAt = timestamp;
            }
        }

        if (requestedAt is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        await UpsertActiveRevisionLeaseAsync(
            connection,
            transaction,
            revisionId,
            requestedAt.Value,
            cancellationToken);
        await PostgresArchiveHydrationRepository.UpsertLastNeededAsync(
            connection,
            transaction,
            revisionId,
            transferredAt,
            cancellationToken);

        await using (NpgsqlCommand close = connection.CreateCommand())
        {
            close.Transaction = transaction;
            close.CommandText =
                """
                UPDATE archive_source_managed_hydrations
                SET released_at_utc = @transferred_at_utc
                WHERE asset_id = @asset_id
                  AND released_at_utc IS NULL;
                """;
            close.Parameters.AddWithValue(
                "asset_id",
                Guid.Parse(assetId.ToString()));
            close.Parameters.AddWithValue(
                "transferred_at_utc",
                transferredAt);
            await close.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    internal static async Task UpsertActiveSourceLeaseAsync(
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
                release_requested_at_utc = CASE
                    WHEN archive_source_managed_hydrations.released_at_utc IS NULL
                        THEN archive_source_managed_hydrations.release_requested_at_utc
                    ELSE NULL
                END,
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

    internal static async Task UpsertLastNeededAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetId assetId,
        DateTimeOffset lastNeededAtUtc,
        bool preferLatest,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = preferLatest
            ? """
              INSERT INTO archive_source_managed_hydration_usage (
                  asset_id,
                  last_needed_at_utc)
              VALUES (
                  @asset_id,
                  @last_needed_at_utc)
              ON CONFLICT(asset_id) DO UPDATE SET
                  last_needed_at_utc =
                      GREATEST(
                          archive_source_managed_hydration_usage.last_needed_at_utc,
                          excluded.last_needed_at_utc);
              """
            : """
              INSERT INTO archive_source_managed_hydration_usage (
                  asset_id,
                  last_needed_at_utc)
              VALUES (
                  @asset_id,
                  @last_needed_at_utc)
              ON CONFLICT(asset_id) DO UPDATE SET
                  last_needed_at_utc = excluded.last_needed_at_utc;
              """;
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue(
            "last_needed_at_utc",
            lastNeededAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<ArchiveManagedSourceHydrationState?> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AssetId assetId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                asset_id,
                requested_at_utc,
                release_requested_at_utc,
                released_at_utc
            FROM archive_source_managed_hydrations
            WHERE asset_id = @asset_id;
            """;
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ArchiveManagedSourceHydrationState(
            AssetId.From(reader.GetGuid(0)),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(3));
    }

    private static async Task UpsertActiveRevisionLeaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetRevisionId revisionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO asset_revision_managed_hydrations (
                asset_revision_id,
                requested_at_utc,
                release_requested_at_utc,
                released_at_utc)
            VALUES (
                @asset_revision_id,
                @requested_at_utc,
                NULL,
                NULL)
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
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue(
            "requested_at_utc",
            requestedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
