using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Persists the last observed local/cloud availability for permanent-archive assets.
/// Availability is intentionally independent of immutable revisions so OneDrive placeholders
/// can remain catalogued without opening them or creating a content revision.
/// </summary>
public sealed class SqliteArchiveAvailabilityRepository : IArchiveAvailabilityRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveAvailabilityRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS archive_asset_availability (
                asset_id TEXT NOT NULL PRIMARY KEY,
                availability TEXT NOT NULL,
                checked_at_utc TEXT NOT NULL,
                FOREIGN KEY (asset_id) REFERENCES assets (id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_archive_asset_availability_state
                ON archive_asset_availability (availability, asset_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordAsync(
        AssetId assetId,
        AssetAvailability availability,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO archive_asset_availability (asset_id, availability, checked_at_utc)
            VALUES ($asset_id, $availability, $checked_at_utc)
            ON CONFLICT(asset_id) DO UPDATE SET
                availability = excluded.availability,
                checked_at_utc = excluded.checked_at_utc;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        command.Parameters.AddWithValue("$availability", ToStorageValue(availability));
        command.Parameters.AddWithValue("$checked_at_utc", Format(checkedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string ToStorageValue(AssetAvailability availability) => availability switch
    {
        AssetAvailability.Local => "local",
        AssetAvailability.OnlineOnly => "online-only",
        AssetAvailability.Downloading => "downloading",
        AssetAvailability.Unavailable => "unavailable",
        AssetAvailability.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(availability)),
    };

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
