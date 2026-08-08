using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class LocalArchiveSyncCoordinatorTests
{
    [Fact]
    public async Task Expanding_month_folders_to_year_reuses_existing_assets_and_discovers_new_children()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string archiveRoot = Path.Combine(directory, "Kamerabilder");
            string january = Path.Combine(archiveRoot, "1970", "01");
            string february = Path.Combine(archiveRoot, "1970", "02");
            string march = Path.Combine(archiveRoot, "1970", "03");
            Directory.CreateDirectory(january);
            Directory.CreateDirectory(february);
            Directory.CreateDirectory(march);
            await File.WriteAllBytesAsync(Path.Combine(january, "a.jpg"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(february, "b.jpg"), [2]);
            await File.WriteAllBytesAsync(Path.Combine(march, "c.jpg"), [3]);

            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            SqliteLocalBatchRepository repository = new(database);
            var catalogueSource = await repository.GetOrCreateLocalFolderSourceAsync(archiveRoot, Utc(10));
            LocalFolderAssetSource source = new(catalogueSource.Id, archiveRoot);
            LocalArchiveSyncCoordinator coordinator = new(database);
            SqliteSourceCatalogueScanner scanner = new(database);

            LocalArchiveSyncSummary januarySync = await coordinator.SyncAsync(
                source,
                catalogueSource,
                ["1970/01"],
                Utc(10));

            Assert.Equal(1, januarySync.SupportedFileCount);
            Assert.Equal(1, januarySync.NewRevisionCount);
            Assert.Single(await scanner.GetAssetsAsync(catalogueSource.Id, includeDeleted: false));

            await File.WriteAllBytesAsync(Path.Combine(january, "new.jpg"), [4]);
            LocalArchiveSyncSummary monthSync = await coordinator.SyncAsync(
                source,
                catalogueSource,
                ["1970/01", "1970/02"],
                Utc(11));

            Assert.Equal(3, monthSync.SupportedFileCount);
            Assert.Equal(2, monthSync.NewRevisionCount);
            Assert.Equal(3, (await scanner.GetAssetsAsync(catalogueSource.Id, includeDeleted: false)).Count);

            LocalArchiveSyncSummary yearSync = await coordinator.SyncAsync(
                source,
                catalogueSource,
                ["1970/01", "1970/02", "1970"],
                Utc(12));

            Assert.Equal(["1970"], yearSync.IncludedFolders);
            Assert.Equal(4, yearSync.SupportedFileCount);
            Assert.Equal(1, yearSync.NewRevisionCount);
            Assert.Equal(3, yearSync.UnchangedFileCount);
            Assert.Equal(4, (await scanner.GetAssetsAsync(catalogueSource.Id, includeDeleted: false)).Count);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 8, 8, hour, 0, 0, TimeSpan.Zero);

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
