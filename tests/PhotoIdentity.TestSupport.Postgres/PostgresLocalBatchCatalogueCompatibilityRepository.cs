using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// PostgreSQL-backed compatibility adapter for the neutral local-batch catalogue boundary.
/// </summary>
public sealed class PostgresLocalBatchCatalogueCompatibilityRepository : ILocalBatchCatalogueRepository
{
    private readonly PostgresLocalBatchCompatibilityRepository _batch;
    private readonly PostgresSourceCatalogueScannerCompatibility _scanner;

    public PostgresLocalBatchCatalogueCompatibilityRepository(PostgresTestCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _batch = new PostgresLocalBatchCompatibilityRepository(database);
        _scanner = new PostgresSourceCatalogueScannerCompatibility(database);
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
        return new LocalBatchCatalogueSource(source.Id, source.Kind, source.RootLocator, source.CreatedAtUtc);
    }

    public async Task<LocalBatchCatalogueScanSummary> ScanAsync(
        IAssetSource source,
        LocalBatchCatalogueSource catalogueSource,
        SourceScanOptions options,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogueSource);
        CatalogueSource compatibilitySource = new(
            catalogueSource.SourceId,
            catalogueSource.Kind,
            catalogueSource.RootLocator,
            catalogueSource.CreatedAtUtc);
        SourceCatalogueScanSummary summary = await _scanner.ScanAsync(
            source,
            compatibilitySource,
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
