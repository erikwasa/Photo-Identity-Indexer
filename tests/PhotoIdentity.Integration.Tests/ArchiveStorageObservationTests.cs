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
}
