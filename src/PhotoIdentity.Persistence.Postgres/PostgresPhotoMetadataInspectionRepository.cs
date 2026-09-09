using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoMetadataInspectionRepository :
    IPhotoMetadataInspectionRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresPhotoMetadataInspectionRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CataloguePhotoMetadataInspection?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT extraction_contract_version, inspected_at_utc
            FROM photo_metadata_inspections
            WHERE asset_revision_id = @revision_id;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CataloguePhotoMetadataInspection(
                reader.GetInt32(0),
                reader.GetFieldValue<DateTimeOffset>(1))
            : null;
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

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_metadata_inspections (
                asset_revision_id, extraction_contract_version, inspected_at_utc)
            VALUES (@revision_id, @version, @inspected_at_utc)
            ON CONFLICT (asset_revision_id) DO UPDATE SET
                extraction_contract_version = excluded.extraction_contract_version,
                inspected_at_utc = excluded.inspected_at_utc;
            """;
        command.Parameters.AddWithValue("revision_id", Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue("version", extractionContractVersion);
        command.Parameters.AddWithValue("inspected_at_utc", inspectedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
