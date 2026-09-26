using System.Security.Cryptography;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Testing.Postgres;

public sealed record SourceCatalogueScanSummary(
    SourceId SourceId,
    DateTimeOffset ScannedAtUtc,
    int SupportedFileCount,
    int NewRevisionCount,
    int UnchangedFileCount,
    int MarkedDeletedCount);

/// <summary>
/// PostgreSQL implementation of the historical source-scanner fixture surface.
/// </summary>
public sealed class PostgresSourceCatalogueScannerCompatibility
{
    private readonly PostgresTestCatalogueDatabase _database;

    public PostgresSourceCatalogueScannerCompatibility(PostgresTestCatalogueDatabase database)
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
                throw new InvalidOperationException("The source returned an asset owned by a different source identifier.");
            }
            if (sourceAsset.Availability != AssetAvailability.Local)
            {
                throw new InvalidOperationException($"Asset '{sourceAsset.RelativePath}' is not locally available for cataloguing.");
            }

            Sha256Digest contentHash;
            await using (Stream content = await source.OpenContentAsync(sourceAsset.Reference, cancellationToken))
            {
                byte[] hash = await SHA256.HashDataAsync(content, cancellationToken);
                contentHash = new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant());
            }

            bool inserted = await SaveObservationAsync(catalogueSource, sourceAsset, contentHash, scannedAt, cancellationToken);
            supported++;
            if (inserted)
            {
                newRevisions++;
            }
        }

        int deleted = await MarkMissingAssetsAsync(catalogueSource.Id, options.RelativeRoot, scannedAt, cancellationToken);
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
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = includeDeleted
            ? """
              SELECT id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc
              FROM assets
              WHERE source_id = @source_id
              ORDER BY source_key;
              """
            : """
              SELECT id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc
              FROM assets
              WHERE source_id = @source_id AND deleted_at_utc IS NULL
              ORDER BY source_key;
              """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);

        List<CatalogueAsset> assets = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            DateTimeOffset created = reader.GetFieldValue<DateTimeOffset>(3);
            assets.Add(new CatalogueAsset(
                AssetId.From(reader.GetGuid(0)),
                SourceId.From(reader.GetGuid(1)),
                reader.GetString(2),
                created,
                reader.IsDBNull(4) ? created : reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        }
        return assets;
    }

    public async Task<IReadOnlyList<CatalogueAssetRevision>> GetRevisionsAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height
            FROM asset_revisions
            WHERE asset_id = @asset_id
            ORDER BY observed_at_utc, id;
            """;
        command.Parameters.AddWithValue("asset_id", assetId.Value);

        List<CatalogueAssetRevision> revisions = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(new CatalogueAssetRevision(
                AssetRevisionId.From(reader.GetGuid(0)),
                AssetId.From(reader.GetGuid(1)),
                new Sha256Digest(reader.GetString(2)),
                reader.GetInt64(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7)));
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
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES (@id, @kind, @root_locator, @created_at_utc)
                ON CONFLICT(id) DO UPDATE SET kind = excluded.kind, root_locator = excluded.root_locator;
                """;
            command.Parameters.AddWithValue("id", source.Id.Value);
            command.Parameters.AddWithValue("kind", source.Kind);
            command.Parameters.AddWithValue("root_locator", source.RootLocator);
            command.Parameters.AddWithValue("created_at_utc", source.CreatedAtUtc.ToUniversalTime());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        AssetId proposedAssetId = AssetId.New();
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc)
                VALUES (@id, @source_id, @source_key, @created_at_utc, @last_seen_at_utc, NULL)
                ON CONFLICT(source_id, source_key) DO UPDATE SET
                    last_seen_at_utc = excluded.last_seen_at_utc,
                    deleted_at_utc = NULL;
                """;
            command.Parameters.AddWithValue("id", proposedAssetId.Value);
            command.Parameters.AddWithValue("source_id", source.Id.Value);
            command.Parameters.AddWithValue("source_key", sourceAsset.Reference.ItemKey);
            command.Parameters.AddWithValue("created_at_utc", scannedAtUtc);
            command.Parameters.AddWithValue("last_seen_at_utc", scannedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        AssetId assetId;
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM assets WHERE source_id = @source_id AND source_key = @source_key;";
            command.Parameters.AddWithValue("source_id", source.Id.Value);
            command.Parameters.AddWithValue("source_key", sourceAsset.Reference.ItemKey);
            assetId = AssetId.From((Guid)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("The asset was unavailable after persistence.")));
        }

        int inserted;
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO asset_revisions (
                    id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
                VALUES (@id, @asset_id, @content_sha256, @size_bytes, @observed_at_utc, @media_type, NULL, NULL)
                ON CONFLICT(asset_id, content_sha256) DO NOTHING;
                """;
            command.Parameters.AddWithValue("id", AssetRevisionId.New().Value);
            command.Parameters.AddWithValue("asset_id", assetId.Value);
            command.Parameters.AddWithValue("content_sha256", contentHash.ToString());
            command.Parameters.AddWithValue("size_bytes", sourceAsset.SizeBytes);
            command.Parameters.AddWithValue("observed_at_utc", scannedAtUtc);
            command.Parameters.AddWithValue("media_type", (object?)sourceAsset.MediaType ?? DBNull.Value);
            inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted == 1;
    }

    private async Task<int> MarkMissingAssetsAsync(
        SourceId sourceId,
        string? relativeRoot,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken)
    {
        string scope = ArchiveCoverage.NormalizeRelativeFolder(relativeRoot ?? string.Empty);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = scope.Length == 0
            ? """
              UPDATE assets
              SET deleted_at_utc = COALESCE(deleted_at_utc, @scanned_at_utc)
              WHERE source_id = @source_id
                AND (last_seen_at_utc IS NULL OR last_seen_at_utc <> @scanned_at_utc)
                AND deleted_at_utc IS NULL;
              """
            : """
              UPDATE assets
              SET deleted_at_utc = COALESCE(deleted_at_utc, @scanned_at_utc)
              WHERE source_id = @source_id
                AND substr(source_key, 1, length(@scope_prefix)) = @scope_prefix
                AND (last_seen_at_utc IS NULL OR last_seen_at_utc <> @scanned_at_utc)
                AND deleted_at_utc IS NULL;
              """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);
        command.Parameters.AddWithValue("scanned_at_utc", scannedAtUtc);
        if (scope.Length > 0)
        {
            command.Parameters.AddWithValue("scope_prefix", scope + "/");
        }
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
