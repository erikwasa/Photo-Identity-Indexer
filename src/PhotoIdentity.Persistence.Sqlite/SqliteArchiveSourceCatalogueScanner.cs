using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record ArchiveSourceCatalogueScanSummary(
    SourceId SourceId,
    DateTimeOffset ScannedAtUtc,
    int SupportedFileCount,
    int LocalFileCount,
    int OnlineOnlyFileCount,
    int DownloadingFileCount,
    int UnavailableFileCount,
    int AvailabilityErrorCount,
    int NewRevisionCount,
    int UnchangedFileCount,
    int MarkedDeletedCount);

/// <summary>
/// Catalogues permanent-archive presence and availability without opening OneDrive placeholders.
/// Only locally available items are hashed and assigned immutable revisions.
/// </summary>
public sealed class SqliteArchiveSourceCatalogueScanner
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveSourceCatalogueScanner(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ArchiveSourceCatalogueScanSummary> ScanAsync(
        IAssetSource source,
        CatalogueSource catalogueSource,
        SourceScanOptions options,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogueSource);
        ArgumentNullException.ThrowIfNull(options);

        await new SqliteArchiveAvailabilityRepository(_database).EnsureSchemaAsync(cancellationToken);
        DateTimeOffset scannedAt = scannedAtUtc.ToUniversalTime();
        int supported = 0;
        int local = 0;
        int onlineOnly = 0;
        int downloading = 0;
        int unavailable = 0;
        int availabilityErrors = 0;
        int newRevisions = 0;
        int unchanged = 0;

        await foreach (SourceAsset sourceAsset in source.EnumerateAsync(options, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceAsset.Reference.SourceId != catalogueSource.Id)
            {
                throw new InvalidOperationException(
                    "The source returned an asset owned by a different source identifier.");
            }

            supported++;
            switch (sourceAsset.Availability)
            {
                case AssetAvailability.Local:
                    local++;
                    break;
                case AssetAvailability.OnlineOnly:
                    onlineOnly++;
                    break;
                case AssetAvailability.Downloading:
                    downloading++;
                    break;
                case AssetAvailability.Unavailable:
                    unavailable++;
                    break;
                case AssetAvailability.Error:
                    availabilityErrors++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(sourceAsset.Availability),
                        sourceAsset.Availability,
                        "Unsupported archive availability state.");
            }

            Sha256Digest? contentHash = null;
            if (sourceAsset.Availability == AssetAvailability.Local)
            {
                await using Stream content = await source.OpenContentAsync(
                    sourceAsset.Reference,
                    cancellationToken);
                byte[] hash = await SHA256.HashDataAsync(content, cancellationToken);
                contentHash = new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant());
            }

            bool inserted = await SaveObservationAsync(
                catalogueSource,
                sourceAsset,
                contentHash,
                scannedAt,
                cancellationToken);
            if (contentHash is not null)
            {
                if (inserted)
                {
                    newRevisions++;
                }
                else
                {
                    unchanged++;
                }
            }
        }

        int deleted = await MarkMissingAssetsAsync(
            catalogueSource.Id,
            options.RelativeRoot,
            scannedAt,
            cancellationToken);
        return new ArchiveSourceCatalogueScanSummary(
            catalogueSource.Id,
            scannedAt,
            supported,
            local,
            onlineOnly,
            downloading,
            unavailable,
            availabilityErrors,
            newRevisions,
            unchanged,
            deleted);
    }

    private async Task<bool> SaveObservationAsync(
        CatalogueSource source,
        SourceAsset sourceAsset,
        Sha256Digest? contentHash,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await UpsertSourceAsync(connection, transaction, source, cancellationToken);

        AssetId proposedAssetId = AssetId.New();
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
            command.Parameters.AddWithValue("$created_at_utc", Format(scannedAtUtc));
            command.Parameters.AddWithValue("$last_seen_at_utc", Format(scannedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        AssetId assetId;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id
                FROM assets
                WHERE source_id = $source_id AND source_key = $source_key;
                """;
            command.Parameters.AddWithValue("$source_id", source.Id.ToString());
            command.Parameters.AddWithValue("$source_key", sourceAsset.Reference.ItemKey);
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            assetId = value is string id
                ? AssetId.From(Guid.Parse(id))
                : throw new InvalidOperationException("The archive asset was unavailable after it was persisted.");
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO archive_asset_availability (asset_id, availability, checked_at_utc)
                VALUES ($asset_id, $availability, $checked_at_utc)
                ON CONFLICT(asset_id) DO UPDATE SET
                    availability = excluded.availability,
                    checked_at_utc = excluded.checked_at_utc;
                """;
            command.Parameters.AddWithValue("$asset_id", assetId.ToString());
            command.Parameters.AddWithValue(
                "$availability",
                SqliteArchiveAvailabilityRepository.ToStorageValue(sourceAsset.Availability));
            command.Parameters.AddWithValue("$checked_at_utc", Format(scannedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        int inserted = 0;
        if (contentHash is Sha256Digest resolvedHash)
        {
            using SqliteCommand command = connection.CreateCommand();
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
            command.Parameters.AddWithValue("$content_sha256", resolvedHash.ToString());
            command.Parameters.AddWithValue("$size_bytes", sourceAsset.SizeBytes);
            command.Parameters.AddWithValue("$observed_at_utc", Format(scannedAtUtc));
            command.Parameters.AddWithValue("$media_type", sourceAsset.MediaType);
            inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return inserted == 1;
    }

    private async Task<int> MarkMissingAssetsAsync(
        SourceId sourceId,
        string? relativeRoot,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken)
    {
        string scope = ArchiveCoverage.NormalizeRelativeFolder(relativeRoot ?? string.Empty);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = scope.Length == 0
            ? """
              UPDATE assets
              SET deleted_at_utc = COALESCE(deleted_at_utc, $scanned_at_utc)
              WHERE source_id = $source_id
                AND (last_seen_at_utc IS NULL OR last_seen_at_utc <> $scanned_at_utc)
                AND deleted_at_utc IS NULL;
              """
            : """
              UPDATE assets
              SET deleted_at_utc = COALESCE(deleted_at_utc, $scanned_at_utc)
              WHERE source_id = $source_id
                AND substr(source_key, 1, length($scope_prefix)) = $scope_prefix
                AND (last_seen_at_utc IS NULL OR last_seen_at_utc <> $scanned_at_utc)
                AND deleted_at_utc IS NULL;
              """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$scanned_at_utc", Format(scannedAtUtc));
        if (scope.Length > 0)
        {
            command.Parameters.AddWithValue("$scope_prefix", scope + "/");
        }

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

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
