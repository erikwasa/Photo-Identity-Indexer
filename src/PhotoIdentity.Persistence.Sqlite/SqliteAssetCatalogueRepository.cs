using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Stores source, asset and immutable revision records used by local catalogue scans.
/// </summary>
public sealed class SqliteAssetCatalogueRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteAssetCatalogueRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueAssetRevision> SaveRevisionAsync(
        CatalogueSource source,
        CatalogueAsset asset,
        CatalogueAssetRevision revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(revision);
        if (asset.SourceId != source.Id) throw new ArgumentException("The asset must belong to the supplied source.", nameof(asset));
        if (revision.AssetId != asset.Id) throw new ArgumentException("The revision must belong to the supplied asset.", nameof(revision));

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await UpsertSourceAsync(connection, transaction, source, cancellationToken);
        await UpsertAssetAsync(connection, transaction, asset, cancellationToken);
        await InsertRevisionAsync(connection, transaction, revision, cancellationToken);
        CatalogueAssetRevision persisted = await FindRevisionByContentAsync(connection, transaction, revision.AssetId, revision.ContentHash, cancellationToken)
            ?? throw new InvalidOperationException("The revision was not available after it was persisted.");
        transaction.Commit();
        return persisted;
    }

    public async Task<CatalogueSource?> GetSourceAsync(SourceId id, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, kind, root_locator, created_at_utc FROM sources WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSource(reader) : null;
    }

    public async Task<CatalogueSource?> FindSourceAsync(string kind, string rootLocator, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLocator);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, kind, root_locator, created_at_utc FROM sources WHERE kind = $kind AND root_locator = $root_locator;";
        command.Parameters.AddWithValue("$kind", kind.Trim());
        command.Parameters.AddWithValue("$root_locator", rootLocator.Trim());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSource(reader) : null;
    }

    public async Task<CatalogueAsset?> GetAssetAsync(AssetId id, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, source_id, source_key, created_at_utc FROM assets WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAsset(reader) : null;
    }

    public async Task<CatalogueAsset?> FindAssetAsync(SourceId sourceId, string sourceKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, source_id, source_key, created_at_utc FROM assets WHERE source_id = $source_id AND source_key = $source_key;";
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$source_key", sourceKey.Trim());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAsset(reader) : null;
    }

    public async Task<CatalogueAssetRevision?> GetRevisionAsync(AssetRevisionId id, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height FROM asset_revisions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    public async Task<CatalogueAssetRevision?> GetLatestRevisionAsync(AssetId assetId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height FROM asset_revisions WHERE asset_id = $asset_id ORDER BY observed_at_utc DESC, id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    public async Task<PhotoCaptureMetadata?> GetPhotoMetadataAsync(AssetRevisionId revisionId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsurePhotoMetadataSchemaAsync(connection, cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT taken_at_local, utc_offset_minutes, latitude, longitude FROM photo_capture_metadata WHERE asset_revision_id = $revision_id;";
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new PhotoCaptureMetadata(
            reader.IsDBNull(0) ? null : ParseLocalTimestamp(reader.GetString(0)),
            reader.IsDBNull(1) ? null : TimeSpan.FromMinutes(reader.GetInt32(1)),
            reader.IsDBNull(2) ? null : reader.GetDouble(2),
            reader.IsDBNull(3) ? null : reader.GetDouble(3));
    }

    public async Task SavePhotoMetadataAsync(AssetRevisionId revisionId, PhotoCaptureMetadata metadata, DateTimeOffset extractedAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsurePhotoMetadataSchemaAsync(connection, cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_capture_metadata (asset_revision_id, taken_at_local, utc_offset_minutes, latitude, longitude, extracted_at_utc)
            VALUES ($revision_id, $taken_at_local, $offset_minutes, $latitude, $longitude, $extracted_at_utc)
            ON CONFLICT(asset_revision_id) DO UPDATE SET
                taken_at_local = excluded.taken_at_local,
                utc_offset_minutes = excluded.utc_offset_minutes,
                latitude = excluded.latitude,
                longitude = excluded.longitude,
                extracted_at_utc = excluded.extracted_at_utc;
            """;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$taken_at_local", metadata.TakenAtLocal is null ? DBNull.Value : FormatLocalTimestamp(metadata.TakenAtLocal.Value));
        command.Parameters.AddWithValue("$offset_minutes", metadata.UtcOffset is null ? DBNull.Value : checked((int)metadata.UtcOffset.Value.TotalMinutes));
        command.Parameters.AddWithValue("$latitude", metadata.Latitude is null ? DBNull.Value : metadata.Latitude.Value);
        command.Parameters.AddWithValue("$longitude", metadata.Longitude is null ? DBNull.Value : metadata.Longitude.Value);
        command.Parameters.AddWithValue("$extracted_at_utc", Format(extractedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsurePhotoMetadataSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS photo_capture_metadata (
                asset_revision_id TEXT NOT NULL PRIMARY KEY,
                taken_at_local TEXT NULL,
                utc_offset_minutes INTEGER NULL CHECK (utc_offset_minutes IS NULL OR utc_offset_minutes BETWEEN -840 AND 840),
                latitude REAL NULL CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90),
                longitude REAL NULL CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180),
                extracted_at_utc TEXT NOT NULL,
                FOREIGN KEY (asset_revision_id) REFERENCES asset_revisions (id) ON DELETE CASCADE,
                CHECK ((latitude IS NULL) = (longitude IS NULL)),
                CHECK (utc_offset_minutes IS NULL OR taken_at_local IS NOT NULL));
            CREATE INDEX IF NOT EXISTS ix_photo_capture_metadata_taken ON photo_capture_metadata (taken_at_local, asset_revision_id);
            CREATE INDEX IF NOT EXISTS ix_photo_capture_metadata_location ON photo_capture_metadata (latitude, longitude, asset_revision_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertSourceAsync(SqliteConnection connection, SqliteTransaction transaction, CatalogueSource source, CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO sources (id, kind, root_locator, created_at_utc) VALUES ($id, $kind, $root_locator, $created_at_utc) ON CONFLICT(id) DO UPDATE SET kind = excluded.kind, root_locator = excluded.root_locator;";
        command.Parameters.AddWithValue("$id", source.Id.ToString());
        command.Parameters.AddWithValue("$kind", source.Kind);
        command.Parameters.AddWithValue("$root_locator", source.RootLocator);
        command.Parameters.AddWithValue("$created_at_utc", Format(source.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertAssetAsync(SqliteConnection connection, SqliteTransaction transaction, CatalogueAsset asset, CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO assets (id, source_id, source_key, created_at_utc) VALUES ($id, $source_id, $source_key, $created_at_utc) ON CONFLICT(id) DO UPDATE SET source_id = excluded.source_id, source_key = excluded.source_key;";
        command.Parameters.AddWithValue("$id", asset.Id.ToString());
        command.Parameters.AddWithValue("$source_id", asset.SourceId.ToString());
        command.Parameters.AddWithValue("$source_key", asset.SourceKey);
        command.Parameters.AddWithValue("$created_at_utc", Format(asset.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRevisionAsync(SqliteConnection connection, SqliteTransaction transaction, CatalogueAssetRevision revision, CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO asset_revisions (id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height) VALUES ($id, $asset_id, $content_sha256, $size_bytes, $observed_at_utc, $media_type, $width, $height) ON CONFLICT(asset_id, content_sha256) DO NOTHING;";
        command.Parameters.AddWithValue("$id", revision.Id.ToString());
        command.Parameters.AddWithValue("$asset_id", revision.AssetId.ToString());
        command.Parameters.AddWithValue("$content_sha256", revision.ContentHash.ToString());
        command.Parameters.AddWithValue("$size_bytes", revision.SizeBytes);
        command.Parameters.AddWithValue("$observed_at_utc", Format(revision.ObservedAtUtc));
        command.Parameters.AddWithValue("$media_type", (object?)revision.MediaType ?? DBNull.Value);
        command.Parameters.AddWithValue("$width", (object?)revision.Width ?? DBNull.Value);
        command.Parameters.AddWithValue("$height", (object?)revision.Height ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CatalogueAssetRevision?> FindRevisionByContentAsync(SqliteConnection connection, SqliteTransaction transaction, AssetId assetId, Sha256Digest contentHash, CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height FROM asset_revisions WHERE asset_id = $asset_id AND content_sha256 = $content_sha256;";
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        command.Parameters.AddWithValue("$content_sha256", contentHash.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    private static CatalogueSource ReadSource(SqliteDataReader reader) => new(SourceId.From(Guid.Parse(reader.GetString(0))), reader.GetString(1), reader.GetString(2), ParseTimestamp(reader.GetString(3)));
    private static CatalogueAsset ReadAsset(SqliteDataReader reader) => new(AssetId.From(Guid.Parse(reader.GetString(0))), SourceId.From(Guid.Parse(reader.GetString(1))), reader.GetString(2), ParseTimestamp(reader.GetString(3)));
    private static CatalogueAssetRevision ReadRevision(SqliteDataReader reader) => new(AssetRevisionId.From(Guid.Parse(reader.GetString(0))), AssetId.From(Guid.Parse(reader.GetString(1))), new Sha256Digest(reader.GetString(2)), reader.GetInt64(3), ParseTimestamp(reader.GetString(4)), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetInt32(7));
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTimestamp(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string FormatLocalTimestamp(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
    private static DateTime ParseLocalTimestamp(string value) => DateTime.SpecifyKind(DateTime.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture, DateTimeStyles.None), DateTimeKind.Unspecified);
}
