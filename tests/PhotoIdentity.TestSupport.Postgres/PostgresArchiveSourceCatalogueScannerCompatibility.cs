using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// PostgreSQL composition wrapper matching the historical integration-fixture scanner shape.
/// It keeps source scanning behavior on the provider-neutral scanner while removing the SQLite
/// adapter from active tests.
/// </summary>
public sealed class PostgresArchiveSourceCatalogueScannerCompatibility
{
    private readonly ArchiveSourceCatalogueScanner _scanner;

    public PostgresArchiveSourceCatalogueScannerCompatibility(PostgresTestCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _scanner = new ArchiveSourceCatalogueScanner(
            database,
            new PostgresArchiveSourceScanBatchRepository(database));
    }

    public Task<ArchiveSourceCatalogueScanSummary> ScanAsync(
        IAssetSource source,
        CatalogueSource catalogueSource,
        SourceScanOptions options,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default) =>
        _scanner.ScanAsync(
            source,
            new ArchiveCatalogueSource(
                catalogueSource.Id,
                catalogueSource.Kind,
                catalogueSource.RootLocator,
                catalogueSource.CreatedAtUtc),
            options,
            scannedAtUtc,
            cancellationToken);
}
