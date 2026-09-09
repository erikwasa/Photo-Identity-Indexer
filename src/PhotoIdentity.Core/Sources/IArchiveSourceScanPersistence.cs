using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

public interface IArchiveSourceScanPersistence
{
    Task<IReadOnlyDictionary<string, ArchiveSourceScanBaseline>> GetBaselinesAsync(
        SourceId sourceId, string? relativeRoot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArchiveSourceObservationPersistenceResult>> RecordBatchAsync(
        ArchiveCatalogueSource source, IReadOnlyList<ArchiveSourceScanWrite> writes,
        IReadOnlyDictionary<string, ArchiveSourceScanBaseline> baselines,
        DateTimeOffset scannedAtUtc, CancellationToken cancellationToken = default);
    Task<int> MarkMissingAssetsAsync(SourceId sourceId, string? relativeRoot,
        DateTimeOffset scannedAtUtc, CancellationToken cancellationToken = default);
}
