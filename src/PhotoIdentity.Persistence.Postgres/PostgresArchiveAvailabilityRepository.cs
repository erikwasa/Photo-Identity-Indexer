using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Persists the last observed archive-asset availability in PostgreSQL.
/// </summary>
public sealed class PostgresArchiveAvailabilityRepository : IArchiveAvailabilityRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveAvailabilityRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task RecordAsync(
        AssetId assetId,
        AssetAvailability availability,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO archive_asset_availability (
                asset_id,
                availability,
                checked_at_utc)
            VALUES (
                @asset_id,
                @availability,
                @checked_at_utc)
            ON CONFLICT(asset_id) DO UPDATE SET
                availability = excluded.availability,
                checked_at_utc = excluded.checked_at_utc;
            """;
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue(
            "availability",
            ToStorageValue(availability));
        command.Parameters.AddWithValue(
            "checked_at_utc",
            checkedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ToStorageValue(AssetAvailability availability) => availability switch
    {
        AssetAvailability.Local => "local",
        AssetAvailability.OnlineOnly => "online-only",
        AssetAvailability.Downloading => "downloading",
        AssetAvailability.Unavailable => "unavailable",
        AssetAvailability.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(availability)),
    };
}
