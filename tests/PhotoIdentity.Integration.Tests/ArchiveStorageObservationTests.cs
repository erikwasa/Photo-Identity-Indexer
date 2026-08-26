using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveStorageObservationTests
{
    [Fact]
    public async Task Logical_source_bytes_include_first_time_online_only_items_without_revisions()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset now = new(2026, 8, 9, 9, 15, 0, TimeSpan.Zero);
            CatalogueSource catalogueSource = new(SourceId.New(), "local-folder", directory, now);
            OnlineOnlySource source = new(catalogueSource.Id, "online.jpg", 600, now);

            ArchiveSourceCatalogueScanSummary scan = await new SqliteArchiveSourceCatalogueScanner(database).ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                now);
            Assert.Equal(1, scan.UnverifiedSourceCount);
            Assert.Equal(0, scan.NewRevisionCount);
            Assert.Equal(0, source.OpenCount);

            long logicalBytes = await new SqliteArchiveStorageRepository(database)
                .GetCurrentLogicalSourceBytesAsync(catalogueSource.Id);
            Assert.Equal(600, logicalBytes);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Stable_verified_local_file_reuses_revision_without_rehashing()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset firstObserved = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
            CatalogueSource catalogueSource = new(SourceId.New(), "local-folder", directory, firstObserved);
            MutableSource source = new(catalogueSource.Id, "photo.jpg", [1, 2, 3], firstObserved);
            SqliteArchiveSourceCatalogueScanner scanner = new(database);

            ArchiveSourceCatalogueScanSummary first = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                firstObserved);
            Assert.Equal(1, first.NewRevisionCount);
            Assert.Equal(1, first.Diagnostics.HashedFileCount);
            Assert.Equal(0, first.Diagnostics.MetadataReuseCount);
            Assert.Equal(1, source.OpenCount);

            ArchiveSourceCatalogueScanSummary repeat = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                firstObserved.AddMinutes(1));
            Assert.Equal(0, repeat.NewRevisionCount);
            Assert.Equal(1, repeat.UnchangedFileCount);
            Assert.Equal(0, repeat.Diagnostics.HashedFileCount);
            Assert.Equal(1, repeat.Diagnostics.MetadataReuseCount);
            Assert.Equal(1, source.OpenCount);

            source.SetContent([9, 8, 7], firstObserved.AddMinutes(2));
            ArchiveSourceCatalogueScanSummary changed = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                firstObserved.AddMinutes(2));
            Assert.Equal(1, changed.NewRevisionCount);
            Assert.Equal(1, changed.Diagnostics.HashedFileCount);
            Assert.Equal(0, changed.Diagnostics.MetadataReuseCount);
            Assert.Equal(2, source.OpenCount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Reappearing_online_only_file_requires_verification_even_when_old_metadata_matches()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();
            DateTimeOffset firstObserved = new(2026, 8, 26, 11, 0, 0, TimeSpan.Zero);
            CatalogueSource catalogueSource = new(SourceId.New(), "local-folder", directory, firstObserved);
            MutableSource source = new(catalogueSource.Id, "photo.jpg", [1, 2, 3], firstObserved);
            SqliteArchiveSourceCatalogueScanner scanner = new(database);

            ArchiveSourceCatalogueScanSummary first = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                firstObserved);
            Assert.Equal(1, first.NewRevisionCount);
            Assert.Equal(1, source.OpenCount);

            source.IsPresent = false;
            ArchiveSourceCatalogueScanSummary missing = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                firstObserved.AddMinutes(1));
            Assert.Equal(1, missing.MarkedDeletedCount);

            source.IsPresent = true;
            source.Availability = AssetAvailability.OnlineOnly;
            ArchiveSourceCatalogueScanSummary reappeared = await scanner.ScanAsync(
                source,
                catalogueSource,
                new SourceScanOptions(null, true),
                firstObserved.AddMinutes(2));
            Assert.Equal(1, reappeared.NeedsSourceVerificationCount);
            Assert.Equal(0, reappeared.VerifiedSourceCount);
            Assert.Equal(0, reappeared.Diagnostics.HashedFileCount);
            Assert.Equal(1, source.OpenCount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

    private sealed class OnlineOnlySource : IAssetSource
    {
        private readonly SourceId _sourceId;
        private readonly string _sourceKey;
        private readonly long _sizeBytes;
        private readonly DateTimeOffset _lastWriteTimeUtc;

        public OnlineOnlySource(
            SourceId sourceId,
            string sourceKey,
            long sizeBytes,
            DateTimeOffset lastWriteTimeUtc)
        {
            _sourceId = sourceId;
            _sourceKey = sourceKey;
            _sizeBytes = sizeBytes;
            _lastWriteTimeUtc = lastWriteTimeUtc;
        }

        public int OpenCount { get; private set; }

        public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
            SourceScanOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SourceAsset(
                new SourceAssetReference(_sourceId, _sourceKey),
                _sourceKey,
                "image/jpeg",
                _sizeBytes,
                _lastWriteTimeUtc,
                AssetAvailability.OnlineOnly);
            await Task.CompletedTask;
        }

        public Task<AssetAvailability> GetAvailabilityAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken) => Task.FromResult(AssetAvailability.OnlineOnly);

        public Task<Stream> OpenContentAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken)
        {
            OpenCount++;
            throw new InvalidOperationException("An online-only source must not be opened during scan.");
        }
    }

    private sealed class MutableSource : IAssetSource
    {
        private readonly SourceId _sourceId;
        private readonly string _sourceKey;
        private byte[] _content;
        private DateTimeOffset _lastWriteTimeUtc;

        public MutableSource(
            SourceId sourceId,
            string sourceKey,
            byte[] content,
            DateTimeOffset lastWriteTimeUtc)
        {
            _sourceId = sourceId;
            _sourceKey = sourceKey;
            _content = content;
            _lastWriteTimeUtc = lastWriteTimeUtc;
        }

        public bool IsPresent { get; set; } = true;
        public AssetAvailability Availability { get; set; } = AssetAvailability.Local;
        public int OpenCount { get; private set; }

        public void SetContent(byte[] content, DateTimeOffset lastWriteTimeUtc)
        {
            _content = content;
            _lastWriteTimeUtc = lastWriteTimeUtc;
        }

        public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
            SourceScanOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsPresent)
            {
                yield break;
            }

            yield return new SourceAsset(
                new SourceAssetReference(_sourceId, _sourceKey),
                _sourceKey,
                "image/jpeg",
                _content.LongLength,
                _lastWriteTimeUtc,
                Availability);
            await Task.CompletedTask;
        }

        public Task<AssetAvailability> GetAvailabilityAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken) => Task.FromResult(Availability);

        public Task<Stream> OpenContentAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            if (Availability != AssetAvailability.Local)
            {
                throw new InvalidOperationException("Only local mutable content may be opened.");
            }

            return Task.FromResult<Stream>(new MemoryStream(_content, writable: false));
        }
    }
}
