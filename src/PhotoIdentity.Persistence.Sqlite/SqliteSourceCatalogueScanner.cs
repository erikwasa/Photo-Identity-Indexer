using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record SourceCatalogueScanSummary(
    SourceId SourceId,
    DateTimeOffset ScannedAtUtc,
    int SupportedFileCount,
    int NewRevisionCount,
    int UnchangedFileCount,
    int MarkedDeletedCount);

/// <summary>
/// Catalogues any <see cref="IAssetSource"/> while keeping filesystem concerns outside SQLite.
/// </summary>
public sealed class SqliteSourceCatalogueScanner
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteSourceCatalogueScanner(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<SourceCatalogueScanSummary> ScanAsync(
        IAssetSource source,
        CatalogueSource catalogueSource,
        SourceScanOptions options,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogueSource);
        ArgumentNullException.ThrowIfNull(options);

        DateTimeOffset scannedAt = scannedAtUtc.ToUniversalTime();
        int supported = 0;
        int newRevisions = 0;

        await foreach (SourceAsset sourceAsset in source.EnumerateAsync(options, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceAsset.Reference.SourceId != catalogueSource.Id)
            {
                throw new InvalidOperationException(
                    "The source returned an asset owned by a different source identifier.");
            }

            if (sourceAsset.Availability != AssetAvailability.Local)
            {
                throw new InvalidOperationException(
                    $"Asset '{sourceAsset.RelativePath}' is not locally available for cataloguing.");
            }

            Sha256Digest contentHash;
            await using (Stream content = await source.OpenContentAsync(
                sourceAsset.Reference,
                cancellationToken))
            {
                byte[] hash = await SHA256.HashDataAsync(content, cancellationToken);
                contentHash = new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant());
            }

            bool inserted = await SaveObservationAsync(
                catalogueSource,
                sourceAsset,
                contentHash,
                scannedAt,
                cancellationToken);
            supported++;
            if (inserted)
            {
                newRevisions++;
            }
        }

        int deleted = await MarkMissingAssetsAsync(
            catalogueSource.Id,
            scannedAt,
            cancellationToken);
        return new SourceCatalogueScanSummary(
            catalogueSource.Id,
            scannedAt,
            supported,
            newRevisions,
            supported - newRevisions,
            deleted);
    }

    public async Task<IReadOnlyList<CatalogueAsset>> GetAssetsAsync(
        SourceId sourceId,
        bool includeDeleted = true,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = includeDeleted
            ? """
              SELECT id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc
              FROM assets
              WHERE source_id = $source_id
              ORDER BY source_key;
              """
            : """
              SELECT id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc
              FROM assets
              WHERE source_id = $source_id AND deleted_at_utc IS NULL
              ORDER BY source_key;
              """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());

        List<CatalogueAsset> assets = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            assets.Add(ReadAsset(reader));
        }

        return assets;
    }

    public async Task<IReadOnlyList<CatalogueAssetRevision>> GetRevisionsAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height
            FROM asset_revisions
            WHERE asset_id = $asset_id
            ORDER BY observed_at_utc, id;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());

        List<CatalogueAssetRevision> revisions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(ReadRevision(reader));
        }

        return revisions;
    }

    private async Task<bool> SaveObservationAsync(
        CatalogueSource source,
        SourceAsset sourceAsset,
        Sha256Digest contentHash,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await UpsertSourceAsync(connection, transaction, source, cancellationToken);

        AssetId proposedAssetId = AssetId.New();
        DateTimeOffset createdAt = scannedAtUtc;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO assets (
                    id,
                    source_id,
                    source_key,
                    created_at_utc,
                    last_seen_at_utc,
                    deleted_at_utc)
                    VALUES (
                        $id,
                        $source_id,
                        $source_key,
                        $created_at_utc,
                        $last_seen_at_utc,
                        NULL)
                ON CONFLICT(source_id, source_key) DO UPDATE SET
                    last_seen_at_utc = excluded.last_seen_at_utc,
                    deleted_at_utc = NULL;
                """;
            command.Parameters.AddWithValue("$id", proposedAssetId.ToString());
            command.Parameters.AddWithValue("$source_id", source.Id.ToString());
            command.Parameters.AddWithValue("$source_key", sourceAsset.Reference.ItemKey);
            command.Parameters.AddWithValue("$created_at_utc", Format(createdAt));
            command.Parameters.AddWithValue("$last_seen_at_utc", Format(scannedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        AssetId assetId;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id, created_at_utc
                FROM assets
                WHERE source_id = $source_id AND source_key = $source_key;
                """;
            command.Parameters.AddWithValue("$source_id", source.Id.ToString());
            command.Parameters.AddWithValue("$source_key", sourceAsset.Reference.ItemKey);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("The asset was not available after it was persisted.");
            }

            assetId = AssetId.From(Guid.Parse(reader.GetString(0)));
            createdAt = ParseTimestamp(reader.GetString(1));
        }

        int inserted;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO asset_revisions (
                    id,
                    asset_id,
                    content_sha256,
                    size_bytes,
                    observed_at_utc,
                    media_type,
                    width,
                    height)
                    VALUES (
                        $id,
                        $asset_id,
                        $content_sha256,
                        $size_bytes,
                        $observed_at_utc,
                        $media_type,
                        NULL,
                        NULL)
                ON CONFLICT(asset_id, content_sha256) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$id", AssetRevisionId.New().ToString());
            command.Parameters.AddWithValue("$asset_id", assetId.ToString());
            command.Parameters.AddWithValue("$content_sha256", contentHash.ToString());
            command.Parameters.AddWithValue("$size_bytes", sourceAsset.SizeBytes);
            command.Parameters.AddWithValue("$observed_at_utc", Format(scannedAtUtc));
            command.Parameters.AddWithValue("$media_type", sourceAsset.MediaType);
            inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        _ = createdAt;
        return inserted == 1;
    }

    private async Task<int> MarkMissingAssetsAsync(
        SourceId sourceId,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE assets
            SET deleted_at_utc = COALESCE(deleted_at_utc, $scanned_at_utc)
            WHERE source_id = $source_id
              AND (last_seen_at_utc IS NULL OR last_seen_at_utc <> $scanned_at_utc)
              AND deleted_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$scanned_at_utc", Format(scannedAtUtc));
        int updated = await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
        return updated;
    }

    private static async Task UpsertSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogueSource source,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($id, $kind, $root_locator, $created_at_utc)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                root_locator = excluded.root_locator;
            """;
        command.Parameters.AddWithValue("$id", source.Id.ToString());
        command.Parameters.AddWithValue("$kind", source.Kind);
        command.Parameters.AddWithValue("$root_locator", source.RootLocator);
        command.Parameters.AddWithValue("$created_at_utc", Format(source.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CatalogueAsset ReadAsset(SqliteDataReader reader)
    {
        DateTimeOffset created = ParseTimestamp(reader.GetString(3));
        return new CatalogueAsset(
            AssetId.From(Guid.Parse(reader.GetString(0))),
            SourceId.From(Guid.Parse(reader.GetString(1))),
            reader.GetString(2),
            created,
            reader.IsDBNull(4) ? created : ParseTimestamp(reader.GetString(4)),
            reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5)));
    }

    private static CatalogueAssetRevision ReadRevision(SqliteDataReader reader) =>
        new(
            AssetRevisionId.From(Guid.Parse(reader.GetString(0))),
            AssetId.From(Guid.Parse(reader.GetString(1))),
            new Sha256Digest(reader.GetString(2)),
            reader.GetInt64(3),
            ParseTimestamp(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7));

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
