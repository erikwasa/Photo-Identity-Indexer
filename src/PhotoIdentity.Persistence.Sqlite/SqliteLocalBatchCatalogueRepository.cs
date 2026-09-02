using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Adapts the existing SQLite local-source registration and scanner APIs to the neutral local-batch boundary.
/// </summary>
public sealed class SqliteLocalBatchCatalogueRepository : ILocalBatchCatalogueRepository
{
    private readonly SqliteLocalBatchRepository _batch;
    private readonly SqliteSourceCatalogueScanner _scanner;

    public SqliteLocalBatchCatalogueRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _batch = new SqliteLocalBatchRepository(database);
        _scanner = new SqliteSourceCatalogueScanner(database);
    }

    public async Task<LocalBatchCatalogueSource> GetOrCreateLocalFolderSourceAsync(
        string rootLocator,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        CatalogueSource source = await _batch.GetOrCreateLocalFolderSourceAsync(
            rootLocator,
            createdAtUtc,
            cancellationToken);

        return new LocalBatchCatalogueSource(
            source.Id,
            source.Kind,
            source.RootLocator,
            source.CreatedAtUtc);
    }

    public async Task<LocalBatchCatalogueScanSummary> ScanAsync(
        IAssetSource source,
        LocalBatchCatalogueSource catalogueSource,
        SourceScanOptions options,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogueSource);

        CatalogueSource sqliteSource = new(
            catalogueSource.SourceId,
            catalogueSource.Kind,
            catalogueSource.RootLocator,
            catalogueSource.CreatedAtUtc);

        SourceCatalogueScanSummary summary = await _scanner.ScanAsync(
            source,
            sqliteSource,
            options,
            scannedAtUtc,
            cancellationToken);

        return new LocalBatchCatalogueScanSummary(
            summary.SourceId,
            summary.ScannedAtUtc,
            summary.SupportedFileCount,
            summary.NewRevisionCount,
            summary.UnchangedFileCount,
            summary.MarkedDeletedCount);
    }

    public Task<IReadOnlyList<AssetRevisionId>> GetCurrentRevisionIdsAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default) =>
        _batch.GetCurrentRevisionIdsAsync(sourceId, cancellationToken);
}
