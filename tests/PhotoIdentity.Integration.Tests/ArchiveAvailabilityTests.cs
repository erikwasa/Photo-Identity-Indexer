using System.Runtime.CompilerServices;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveAvailabilityTests
{
    [Fact]
    public async Task Online_only_archive_item_is_not_opened_or_scheduled_until_local_again()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string root = Path.Combine(directory, "Kamerabilder");
            Directory.CreateDirectory(root);
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            CatalogueSource catalogueSource = await new SqliteLocalBatchRepository(database)
                .GetOrCreateLocalFolderSourceAsync(root, Utc(10));
            MutableArchiveSource source = new(catalogueSource.Id, "1970/01/photo.jpg", [1, 2, 3]);
            SqliteArchiveSourceCatalogueScanner scanner = new(database);
            Sha256Digest profileHash = new(new string('a', 64));

            ArchiveSourceCatalogueScanSummary local = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("1970", Recursive: true),
                Utc(10));
            Assert.Equal(1, local.LocalFileCount);
            Assert.Equal(1, local.NewRevisionCount);
            Assert.Equal(1, source.OpenContentCalls);
            Assert.Single(await new SqliteArchiveAnalysisRepository(database)
                .GetPendingCurrentRevisionIdsAsync(catalogueSource.Id, profileHash));

            source.Availability = AssetAvailability.OnlineOnly;
            ArchiveSourceCatalogueScanSummary onlineOnly = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("1970", Recursive: true),
                Utc(11));
            Assert.Equal(1, onlineOnly.OnlineOnlyFileCount);
            Assert.Equal(0, onlineOnly.NewRevisionCount);
            Assert.Equal(1, source.OpenContentCalls);
            Assert.Empty(await new SqliteArchiveAnalysisRepository(database)
                .GetPendingCurrentRevisionIdsAsync(catalogueSource.Id, profileHash));

            SqliteArchiveStatusRepository statusRepository = new(database);
            CatalogueArchiveFolderStatus status = await statusRepository.GetStatusAsync(
                catalogueSource.Id,
                "1970",
                profileHash);
            Assert.Equal(1, status.CurrentImages);
            Assert.Equal(0, status.LocalImages);
            Assert.Equal(1, status.OnlineOnlyImages);
            // Pending is an analysis dimension, independent from current OneDrive availability.
            // The legacy local-only scheduler above must still refuse to schedule/open this item.
            Assert.Equal(1, status.PendingImages);
            CatalogueArchiveItemPage unavailable = await statusRepository.GetItemsAsync(
                catalogueSource.Id,
                "1970",
                profileHash,
                "unavailable",
                0,
                50);
            CatalogueArchiveItemStatus item = Assert.Single(unavailable.Items);
            Assert.Equal("1970/01/photo.jpg", item.RelativePath);
            Assert.Equal("online-only", item.Availability);
            Assert.Equal("unavailable", item.AnalysisState);

            CatalogueArchiveItemPage orthogonalPending = await new SqliteArchiveItemFilterRepository(database)
                .GetItemsAsync(
                    catalogueSource.Id,
                    "1970",
                    profileHash,
                    availability: "online-only",
                    verification: "verified",
                    analysis: "pending",
                    offset: 0,
                    limit: 50);
            CatalogueArchiveItemStatus pendingItem = Assert.Single(orthogonalPending.Items);
            Assert.Equal("1970/01/photo.jpg", pendingItem.RelativePath);
            Assert.Equal("online-only", pendingItem.Availability);
            Assert.Equal("verified", pendingItem.SourceVerificationState);
            Assert.Equal("pending", pendingItem.AnalysisState);

            source.Availability = AssetAvailability.Local;
            ArchiveSourceCatalogueScanSummary hydrated = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions("1970", Recursive: true),
                Utc(12));
            Assert.Equal(1, hydrated.LocalFileCount);
            Assert.Equal(0, hydrated.NewRevisionCount);
            Assert.Equal(1, hydrated.UnchangedFileCount);
            Assert.Equal(2, source.OpenContentCalls);
            Assert.Single(await new SqliteArchiveAnalysisRepository(database)
                .GetPendingCurrentRevisionIdsAsync(catalogueSource.Id, profileHash));
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

    private sealed class MutableArchiveSource : IAssetSource
    {
        private readonly string _relativePath;
        private readonly byte[] _content;

        public MutableArchiveSource(SourceId sourceId, string relativePath, byte[] content)
        {
            SourceId = sourceId;
            _relativePath = relativePath;
            _content = content;
        }

        public SourceId SourceId { get; }
        public AssetAvailability Availability { get; set; } = AssetAvailability.Local;
        public int OpenContentCalls { get; private set; }

        public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
            SourceScanOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SourceAsset(
                new SourceAssetReference(SourceId, _relativePath),
                _relativePath,
                "image/jpeg",
                _content.Length,
                Utc(9),
                Availability);
            await Task.CompletedTask;
        }

        public Task<AssetAvailability> GetAvailabilityAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Availability);
        }

        public Task<Stream> OpenContentAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenContentCalls++;
            if (Availability != AssetAvailability.Local)
            {
                throw new InvalidOperationException("A non-local archive item must never be opened by the scanner.");
            }

            return Task.FromResult<Stream>(new MemoryStream(_content, writable: false));
        }
    }
}
