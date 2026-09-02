using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresArchiveHydrationRepository :
    IArchiveHydrationRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveHydrationRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ArchiveManagedHydrationState?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(
            connection,
            transaction: null,
            revisionId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ArchiveManagedHydrationLeaseState>>
        GetActiveLeasesAsync(
            CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                hydration.asset_revision_id,
                revision.asset_id,
                revision.size_bytes,
                source.root_locator,
                asset.source_key,
                hydration.requested_at_utc,
                COALESCE(
                    usage.last_needed_at_utc,
                    hydration.requested_at_utc),
                hydration.release_requested_at_utc
            FROM asset_revision_managed_hydrations AS hydration
            INNER JOIN asset_revisions AS revision
                ON revision.id = hydration.asset_revision_id
            INNER JOIN assets AS asset
                ON asset.id = revision.asset_id
            INNER JOIN sources AS source
                ON source.id = asset.source_id
            LEFT JOIN asset_revision_managed_hydration_usage AS usage
                ON usage.asset_revision_id = hydration.asset_revision_id
            WHERE hydration.released_at_utc IS NULL
            ORDER BY
                COALESCE(
                    usage.last_needed_at_utc,
                    hydration.requested_at_utc),
                hydration.asset_revision_id;
            """;

        List<ArchiveManagedHydrationLeaseState> values = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new ArchiveManagedHydrationLeaseState(
                AssetRevisionId.From(reader.GetGuid(0)),
                AssetId.From(reader.GetGuid(1)),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return values;
    }

    public async Task<ArchiveManagedHydrationState> ClaimAsync(
        AssetRevisionId revisionId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset requestedAt =
            requestedAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand command = connection.CreateCommand())
        {
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
                requestedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpsertLastNeededAsync(
            connection,
            transaction,
            revisionId,
            requestedAt,
            cancellationToken);

        ArchiveManagedHydrationState persisted =
            await ReadAsync(
                connection,
                transaction,
                revisionId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Managed hydration ownership was unavailable after persistence.");

        await transaction.CommitAsync(cancellationToken);
        return persisted;
    }

    public async Task TouchAsync(
        AssetRevisionId revisionId,
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
                    FROM asset_revision_managed_hydrations
                    WHERE asset_revision_id = @asset_revision_id
                      AND released_at_utc IS NULL);
                """;
            owned.Parameters.AddWithValue(
                "asset_revision_id",
                Guid.Parse(revisionId.ToString()));
            active =
                (bool)(await owned.ExecuteScalarAsync(cancellationToken)
                    ?? false);
        }

        if (active)
        {
            await UpsertLastNeededAsync(
                connection,
                transaction,
                revisionId,
                neededAtUtc.ToUniversalTime(),
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ArchiveManagedHydrationState>
        MarkReleaseRequestedAsync(
            AssetRevisionId revisionId,
            DateTimeOffset requestedAtUtc,
            CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE asset_revision_managed_hydrations
            SET release_requested_at_utc =
                COALESCE(
                    release_requested_at_utc,
                    @requested_at_utc)
            WHERE asset_revision_id = @asset_revision_id
              AND released_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue(
            "requested_at_utc",
            requestedAtUtc.ToUniversalTime());

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException(
                "The original is not owned by Photo Identity and cannot be released automatically.");
        }

        return await ReadAsync(
            connection,
            transaction: null,
            revisionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Managed hydration ownership was unavailable after release request.");
    }

    public async Task MarkReleasedAsync(
        AssetRevisionId revisionId,
        DateTimeOffset releasedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE asset_revision_managed_hydrations
            SET released_at_utc = @released_at_utc
            WHERE asset_revision_id = @asset_revision_id
              AND released_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue(
            "released_at_utc",
            releasedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task UpsertLastNeededAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetRevisionId revisionId,
        DateTimeOffset neededAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO asset_revision_managed_hydration_usage (
                asset_revision_id,
                last_needed_at_utc)
            VALUES (
                @asset_revision_id,
                @last_needed_at_utc)
            ON CONFLICT(asset_revision_id) DO UPDATE SET
                last_needed_at_utc = excluded.last_needed_at_utc;
            """;
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue(
            "last_needed_at_utc",
            neededAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<ArchiveManagedHydrationState?> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command =
            connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                asset_revision_id,
                requested_at_utc,
                release_requested_at_utc,
                released_at_utc
            FROM asset_revision_managed_hydrations
            WHERE asset_revision_id = @asset_revision_id;
            """;
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(revisionId.ToString()));

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ArchiveManagedHydrationState(
            AssetRevisionId.From(reader.GetGuid(0)),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.IsDBNull(2)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(2),
            reader.IsDBNull(3)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(3));
    }
}
