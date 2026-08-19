using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CataloguePhotoMetadataInspection(
    int ExtractionContractVersion,
    DateTimeOffset InspectedAtUtc);

public static class SqlitePhotoMetadataInspectionSchema
{
    public static async Task EnsureAsync(
        SqliteCatalogueDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        await using SqliteConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await EnsureAsync(connection, cancellationToken);
    }

    internal static async Task EnsureAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS photo_metadata_inspections (
                asset_revision_id TEXT NOT NULL PRIMARY KEY,
                extraction_contract_version INTEGER NOT NULL CHECK (extraction_contract_version > 0),
                inspected_at_utc TEXT NOT NULL,
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_photo_metadata_inspections_version
                ON photo_metadata_inspections (extraction_contract_version, asset_revision_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// Records which metadata extraction contract has been durably completed for an immutable revision.
/// The marker is intentionally separate from capture metadata so older rows remain readable and can
/// be recognized as stale after the extraction contract expands.
/// </summary>
public sealed class SqlitePhotoMetadataInspectionRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqlitePhotoMetadataInspectionRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CataloguePhotoMetadataInspection?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoMetadataInspectionSchema.EnsureAsync(connection, cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT extraction_contract_version, inspected_at_utc
            FROM photo_metadata_inspections
            WHERE asset_revision_id = $revision_id;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CataloguePhotoMetadataInspection(
            reader.GetInt32(0),
            DateTimeOffset.Parse(
                reader.GetString(1),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
    }

    public async Task<bool> IsCurrentAsync(
        AssetRevisionId revisionId,
        int currentVersion,
        CancellationToken cancellationToken = default)
    {
        if (currentVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentVersion));
        }

        CataloguePhotoMetadataInspection? inspection = await GetAsync(revisionId, cancellationToken);
        return inspection is not null && inspection.ExtractionContractVersion >= currentVersion;
    }

    public async Task MarkAsync(
        AssetRevisionId revisionId,
        int extractionContractVersion,
        DateTimeOffset inspectedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (extractionContractVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(extractionContractVersion));
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await SqlitePhotoMetadataInspectionSchema.EnsureAsync(connection, cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_metadata_inspections (
                asset_revision_id,
                extraction_contract_version,
                inspected_at_utc)
            VALUES ($revision_id, $version, $inspected_at_utc)
            ON CONFLICT(asset_revision_id) DO UPDATE SET
                extraction_contract_version = excluded.extraction_contract_version,
                inspected_at_utc = excluded.inspected_at_utc;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$version", extractionContractVersion);
        command.Parameters.AddWithValue(
            "$inspected_at_utc",
            inspectedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
