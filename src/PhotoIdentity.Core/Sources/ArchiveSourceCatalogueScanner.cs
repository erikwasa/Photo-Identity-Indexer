using System.Diagnostics;
using System.Security.Cryptography;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Sources;

public sealed record ArchiveSourceCatalogueScanDiagnostics(
    int MetadataReuseCount,
    TimeSpan BaselineReadElapsed,
    int ExcludedFileCount,
    int ExclusionBatchCount,
    TimeSpan ExclusionElapsed,
    int HashedFileCount,
    long HashedBytes,
    TimeSpan HashingElapsed,
    int ObservationWriteCount,
    int PersistenceBatchCount,
    TimeSpan ObservationPersistenceElapsed,
    TimeSpan MissingReconciliationElapsed,
    TimeSpan TotalElapsed);

public sealed record ArchiveSourceCatalogueScanSummary(
    SourceId SourceId,
    DateTimeOffset ScannedAtUtc,
    int SupportedFileCount,
    int LocalFileCount,
    int OnlineOnlyFileCount,
    int DownloadingFileCount,
    int UnavailableFileCount,
    int AvailabilityErrorCount,
    int NewRevisionCount,
    int UnchangedFileCount,
    int VerifiedSourceCount,
    int NeedsSourceVerificationCount,
    int UnverifiedSourceCount,
    int MarkedDeletedCount,
    ArchiveSourceCatalogueScanDiagnostics Diagnostics);

/// <summary>
/// Catalogues permanent-archive presence and availability without opening OneDrive placeholders.
/// Lightweight size/last-write observations are retained for every item. A previously verified
/// local file reuses its immutable revision when size, last-write timestamp and media type still
/// match the verified baseline; otherwise local content is SHA-256 verified before persistence.
/// Excluded locators are observed only through their privacy tombstone and are never opened,
/// hashed or written back into normal catalogue/analysis state.
/// </summary>
public sealed class ArchiveSourceCatalogueScanner
{
    private readonly ICatalogueStoreInitializer _store;
    private readonly IArchiveSourceScanPersistence _persistence;
    private readonly ISourceCopyExclusionRepository? _exclusions;

    public ArchiveSourceCatalogueScanner(
        ICatalogueStoreInitializer store,
        IArchiveSourceScanPersistence persistence,
        ISourceCopyExclusionRepository? exclusions = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _exclusions = exclusions;
    }

    public async Task<ArchiveSourceCatalogueScanSummary> ScanAsync(
        IAssetSource source,
        ArchiveCatalogueSource catalogueSource,
        SourceScanOptions options,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogueSource);
        ArgumentNullException.ThrowIfNull(options);

        Stopwatch totalStopwatch = Stopwatch.StartNew();
        await _store.InitializeAsync(cancellationToken);
        DateTimeOffset scannedAt = scannedAtUtc.ToUniversalTime();

        Stopwatch baselineStopwatch = Stopwatch.StartNew();
        IReadOnlyDictionary<string, ArchiveSourceScanBaseline> baselines = await _persistence.GetBaselinesAsync(
            catalogueSource.SourceId,
            options.RelativeRoot,
            cancellationToken);
        baselineStopwatch.Stop();

        int supported = 0;
        int local = 0;
        int onlineOnly = 0;
        int downloading = 0;
        int unavailable = 0;
        int availabilityErrors = 0;
        int newRevisions = 0;
        int unchanged = 0;
        int verified = 0;
        int needsVerification = 0;
        int unverified = 0;
        int metadataReuse = 0;
        int hashedFiles = 0;
        long hashedBytes = 0;
        TimeSpan hashingElapsed = TimeSpan.Zero;
        List<SourceAsset> sourceAssets = [];
        List<ArchiveSourceScanWrite> writes = [];

        await foreach (SourceAsset sourceAsset in source.EnumerateAsync(options, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceAsset.Reference.SourceId != catalogueSource.SourceId)
            {
                throw new InvalidOperationException(
                    "The source returned an asset owned by a different source identifier.");
            }

            supported++;
            switch (sourceAsset.Availability)
            {
                case AssetAvailability.Local:
                    local++;
                    break;
                case AssetAvailability.OnlineOnly:
                    onlineOnly++;
                    break;
                case AssetAvailability.Downloading:
                    downloading++;
                    break;
                case AssetAvailability.Unavailable:
                    unavailable++;
                    break;
                case AssetAvailability.Error:
                    availabilityErrors++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(sourceAsset.Availability),
                        sourceAsset.Availability,
                        "Unsupported archive availability state.");
            }

            sourceAssets.Add(sourceAsset);
        }

        Stopwatch exclusionStopwatch = Stopwatch.StartNew();
        IReadOnlySet<string> excludedKeys = new HashSet<string>(StringComparer.Ordinal);
        int exclusionBatchCount = 0;
        if (_exclusions is not null && sourceAssets.Count > 0)
        {
            excludedKeys = await _exclusions.RecordObservedAndGetExcludedAsync(
                catalogueSource.SourceId,
                sourceAssets
                    .Select(static asset => asset.Reference.ItemKey)
                    .ToArray(),
                scannedAt,
                cancellationToken);
            exclusionBatchCount = 1;
        }
        exclusionStopwatch.Stop();

        foreach (SourceAsset sourceAsset in sourceAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (excludedKeys.Contains(sourceAsset.Reference.ItemKey))
            {
                continue;
            }

            Sha256Digest? contentHash = null;
            bool reuseVerifiedRevision = sourceAsset.Availability == AssetAvailability.Local &&
                baselines.TryGetValue(sourceAsset.Reference.ItemKey, out ArchiveSourceScanBaseline? baseline) &&
                baseline.CanReuseVerifiedRevision(sourceAsset);

            if (sourceAsset.Availability == AssetAvailability.Local && !reuseVerifiedRevision)
            {
                Stopwatch hashStopwatch = Stopwatch.StartNew();
                await using Stream content = await source.OpenContentAsync(
                    sourceAsset.Reference,
                    cancellationToken);
                byte[] hash = await SHA256.HashDataAsync(content, cancellationToken);
                hashStopwatch.Stop();
                hashingElapsed += hashStopwatch.Elapsed;
                hashedFiles++;
                hashedBytes += sourceAsset.SizeBytes;
                contentHash = new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant());
            }
            else if (reuseVerifiedRevision)
            {
                metadataReuse++;
            }

            writes.Add(new ArchiveSourceScanWrite(sourceAsset, contentHash));
        }

        Stopwatch persistenceStopwatch = Stopwatch.StartNew();
        IReadOnlyList<ArchiveSourceObservationPersistenceResult> results = await _persistence.RecordBatchAsync(
            catalogueSource,
            writes,
            baselines,
            scannedAt,
            cancellationToken);
        persistenceStopwatch.Stop();

        for (int index = 0; index < results.Count; index++)
        {
            ArchiveSourceObservationPersistenceResult result = results[index];
            ArchiveSourceScanWrite write = writes[index];
            if (write.SourceAsset.Availability == AssetAvailability.Local)
            {
                if (result.NewRevision)
                {
                    newRevisions++;
                }
                else
                {
                    unchanged++;
                }
            }

            switch (result.VerificationState)
            {
                case ArchiveSourceObservationVerificationState.Verified:
                    verified++;
                    break;
                case ArchiveSourceObservationVerificationState.NeedsSourceVerification:
                    needsVerification++;
                    break;
                case ArchiveSourceObservationVerificationState.Unverified:
                    unverified++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        Stopwatch missingStopwatch = Stopwatch.StartNew();
        int deleted = await _persistence.MarkMissingAssetsAsync(
            catalogueSource.SourceId,
            options.RelativeRoot,
            writes.Select(static write => write.SourceAsset.Reference.ItemKey).ToArray(),
            scannedAt,
            cancellationToken);
        missingStopwatch.Stop();
        totalStopwatch.Stop();

        ArchiveSourceCatalogueScanDiagnostics diagnostics = new(
            metadataReuse,
            baselineStopwatch.Elapsed,
            excludedKeys.Count,
            exclusionBatchCount,
            exclusionStopwatch.Elapsed,
            hashedFiles,
            hashedBytes,
            hashingElapsed,
            writes.Count,
            writes.Count == 0 ? 0 : 1,
            persistenceStopwatch.Elapsed,
            missingStopwatch.Elapsed,
            totalStopwatch.Elapsed);

        return new ArchiveSourceCatalogueScanSummary(
            catalogueSource.SourceId,
            scannedAt,
            supported,
            local,
            onlineOnly,
            downloading,
            unavailable,
            availabilityErrors,
            newRevisions,
            unchanged,
            verified,
            needsVerification,
            unverified,
            deleted,
            diagnostics);
    }
}