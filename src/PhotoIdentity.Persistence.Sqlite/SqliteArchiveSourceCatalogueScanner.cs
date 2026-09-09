using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>SQLite compatibility composition for the provider-neutral archive scanner.</summary>
public sealed class SqliteArchiveSourceCatalogueScanner
{
    private readonly ArchiveSourceCatalogueScanner _scanner;

    public SqliteArchiveSourceCatalogueScanner(SqliteCatalogueDatabase database)
    {
        _scanner = new ArchiveSourceCatalogueScanner(database, new SqliteArchiveSourceScanBatchRepository(database));
    }

    public Task<ArchiveSourceCatalogueScanSummary> ScanAsync(IAssetSource source, CatalogueSource catalogueSource,
        SourceScanOptions options, DateTimeOffset scannedAtUtc, CancellationToken cancellationToken = default) =>
        _scanner.ScanAsync(source, new ArchiveCatalogueSource(catalogueSource.Id, catalogueSource.Kind,
            catalogueSource.RootLocator, catalogueSource.CreatedAtUtc), options, scannedAtUtc, cancellationToken);
}
