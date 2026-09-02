using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class LocalBatchCatalogueRepositoryContractTests
{
    [Fact]
    public async Task Sqlite_adapter_preserves_source_scan_and_current_revision_selection_through_contract()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sourceRoot = Path.Combine(directory, "source");
            Directory.CreateDirectory(sourceRoot);
            string photoPath = Path.Combine(sourceRoot, "photo.jpg");
            await File.WriteAllBytesAsync(photoPath, [1, 2, 3]);

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            ICatalogueStoreInitializer store = database;
            ILocalBatchCatalogueRepository repository = new SqliteLocalBatchCatalogueRepository(database);
            DateTimeOffset firstScanAt = new(2026, 9, 2, 19, 0, 0, TimeSpan.Zero);

            await store.InitializeAsync();
            LocalBatchCatalogueSource firstSource = await repository.GetOrCreateLocalFolderSourceAsync(
                sourceRoot,
                firstScanAt);
            LocalBatchCatalogueSource repeatedSource = await repository.GetOrCreateLocalFolderSourceAsync(
                sourceRoot,
                firstScanAt.AddMinutes(1));

            Assert.Equal(firstSource, repeatedSource);
            Assert.Equal("local-folder", firstSource.Kind);
            Assert.Equal(Path.GetFullPath(sourceRoot), firstSource.RootLocator);

            LocalFolderAssetSource source = new(firstSource.SourceId, sourceRoot);
            LocalBatchCatalogueScanSummary first = await repository.ScanAsync(
                source,
                firstSource,
                new SourceScanOptions(),
                firstScanAt);
            AssetRevisionId firstRevision = Assert.Single(
                await repository.GetCurrentRevisionIdsAsync(firstSource.SourceId));

            await File.WriteAllBytesAsync(photoPath, [9, 8, 7, 6]);
            DateTimeOffset secondScanAt = firstScanAt.AddMinutes(2);
            LocalBatchCatalogueScanSummary second = await repository.ScanAsync(
                source,
                firstSource,
                new SourceScanOptions(),
                secondScanAt);
            AssetRevisionId secondRevision = Assert.Single(
                await repository.GetCurrentRevisionIdsAsync(firstSource.SourceId));

            Assert.Equal(1, first.SupportedFileCount);
            Assert.Equal(1, first.NewRevisionCount);
            Assert.Equal(1, second.SupportedFileCount);
            Assert.Equal(1, second.NewRevisionCount);
            Assert.NotEqual(firstRevision, secondRevision);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
