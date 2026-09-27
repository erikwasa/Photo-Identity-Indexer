using System.Runtime.CompilerServices;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class ArchiveSourceCatalogueScannerTests
{
    [Fact]
    public async Task Large_scan_batches_exclusion_lookup_and_persistence_once()
    {
        SourceId sourceId = SourceId.New();
        SourceAsset[] assets = Enumerable.Range(0, 100)
            .Select(index => new SourceAsset(
                new SourceAssetReference(sourceId, $"folder/{index:D4}.jpg"),
                $"folder/{index:D4}.jpg",
                "image/jpeg",
                index + 1,
                Utc(10),
                AssetAvailability.OnlineOnly))
            .ToArray();
        string[] excluded = [
            assets[2].Reference.ItemKey,
            assets[37].Reference.ItemKey,
            assets[91].Reference.ItemKey,
        ];
        RecordingScanPersistence persistence = new();
        RecordingExclusions exclusions = new(excluded);
        ArchiveSourceCatalogueScanner scanner = new(
            new NoOpInitializer(),
            persistence,
            exclusions);

        ArchiveSourceCatalogueScanSummary summary = await scanner.ScanAsync(
            new StaticAssetSource(assets),
            new ArchiveCatalogueSource(sourceId, "local-folder", "test-root", Utc(9)),
            new SourceScanOptions("folder", Recursive: true),
            Utc(11));

        Assert.Equal(100, summary.SupportedFileCount);
        Assert.Equal(3, summary.Diagnostics.ExcludedFileCount);
        Assert.Equal(1, summary.Diagnostics.ExclusionBatchCount);
        Assert.Equal(97, summary.Diagnostics.ObservationWriteCount);
        Assert.Equal(1, summary.Diagnostics.PersistenceBatchCount);
        Assert.Equal(1, exclusions.BatchCallCount);
        Assert.Equal(0, exclusions.SingleCallCount);
        Assert.Equal(1, persistence.RecordBatchCallCount);
        Assert.Equal(97, persistence.LastWriteCount);
    }

    private static DateTimeOffset Utc(int hour) =>
        new(2026, 9, 27, hour, 0, 0, TimeSpan.Zero);

    private sealed class NoOpInitializer : ICatalogueStoreInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StaticAssetSource : IAssetSource
    {
        private readonly IReadOnlyList<SourceAsset> _assets;

        public StaticAssetSource(IReadOnlyList<SourceAsset> assets) => _assets = assets;

        public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
            SourceScanOptions options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            foreach (SourceAsset asset in _assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return asset;
            }
        }

        public Task<AssetAvailability> GetAvailabilityAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken) =>
            Task.FromResult(AssetAvailability.OnlineOnly);

        public Task<Stream> OpenContentAsync(
            SourceAssetReference asset,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Online-only test assets must not be opened.");
    }

    private sealed class RecordingScanPersistence : IArchiveSourceScanPersistence
    {
        public int RecordBatchCallCount { get; private set; }
        public int LastWriteCount { get; private set; }

        public Task<IReadOnlyDictionary<string, ArchiveSourceScanBaseline>> GetBaselinesAsync(
            SourceId sourceId,
            string? relativeRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, ArchiveSourceScanBaseline>>(
                new Dictionary<string, ArchiveSourceScanBaseline>(StringComparer.Ordinal));

        public Task<IReadOnlyList<ArchiveSourceObservationPersistenceResult>> RecordBatchAsync(
            ArchiveCatalogueSource source,
            IReadOnlyList<ArchiveSourceScanWrite> writes,
            IReadOnlyDictionary<string, ArchiveSourceScanBaseline> baselines,
            DateTimeOffset scannedAtUtc,
            CancellationToken cancellationToken = default)
        {
            RecordBatchCallCount++;
            LastWriteCount = writes.Count;
            IReadOnlyList<ArchiveSourceObservationPersistenceResult> results = writes
                .Select(static _ => new ArchiveSourceObservationPersistenceResult(
                    AssetId.New(),
                    null,
                    NewRevision: false,
                    ArchiveSourceObservationVerificationState.Unverified))
                .ToArray();
            return Task.FromResult(results);
        }

        public Task<int> MarkMissingAssetsAsync(
            SourceId sourceId,
            string? relativeRoot,
            DateTimeOffset scannedAtUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class RecordingExclusions : ISourceCopyExclusionRepository
    {
        private readonly IReadOnlySet<string> _excluded;

        public RecordingExclusions(IEnumerable<string> excluded) =>
            _excluded = new HashSet<string>(excluded, StringComparer.Ordinal);

        public int BatchCallCount { get; private set; }
        public int SingleCallCount { get; private set; }

        public Task<IReadOnlySet<string>> RecordObservedAndGetExcludedAsync(
            SourceId sourceId,
            IReadOnlyCollection<string> sourceKeys,
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken = default)
        {
            BatchCallCount++;
            return Task.FromResult(_excluded);
        }

        public Task<bool> RecordObservedIfExcludedAsync(
            SourceId sourceId,
            string sourceKey,
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken = default)
        {
            SingleCallCount++;
            throw new InvalidOperationException("The scanner must use the batch exclusion boundary.");
        }

        public Task<SourceCopyExclusionState?> GetAsync(SourceId sourceId, string sourceKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SourceCopyExclusionState>> ListAsync(SourceId? sourceId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SourceCopyExclusionState> ExcludeAsync(SourceId sourceId, string sourceKey, DateTimeOffset excludedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RestoreAsync(SourceId sourceId, string sourceKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetPurgeStateAsync(SourceId sourceId, string sourceKey, string purgeState, string? errorCode, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsAssetExcludedAsync(AssetId assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsRevisionExcludedAsync(AssetRevisionId revisionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsFaceOccurrenceExcludedAsync(FaceOccurrenceId faceOccurrenceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
