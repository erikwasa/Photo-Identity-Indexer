using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Sources;

public enum ArchiveSourceObservationVerificationState
{
    Verified,
    NeedsSourceVerification,
    Unverified,
}

public sealed record ArchiveCatalogueSource
{
    public ArchiveCatalogueSource(
        SourceId sourceId,
        string kind,
        string rootLocator,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLocator);

        SourceId = sourceId;
        Kind = kind.Trim();
        RootLocator = rootLocator.Trim();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public SourceId SourceId { get; }
    public string Kind { get; }
    public string RootLocator { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}

public sealed record ArchiveSourceObservationSnapshot(
    AssetId AssetId,
    SourceId SourceId,
    string RootLocator,
    string SourceKey,
    long ObservedSizeBytes,
    DateTimeOffset ObservedLastWriteTimeUtc,
    string MediaType,
    DateTimeOffset ObservedAtUtc,
    AssetAvailability Availability,
    ArchiveSourceObservationVerificationState VerificationState,
    AssetRevisionId? VerifiedRevisionId,
    DateTimeOffset? VerifiedAtUtc);

public sealed record ArchiveSourceObservationPersistenceResult(
    AssetId AssetId,
    AssetRevisionId? RevisionId,
    bool NewRevision,
    ArchiveSourceObservationVerificationState VerificationState);

public sealed record ArchiveSourceVerificationPersistenceResult(
    AssetRevisionId RevisionId,
    bool NewRevision);

/// <summary>
/// Persists lightweight archive source observations independently of immutable content revisions.
/// Metadata changes may require source verification, but metadata alone never establishes new content.
/// </summary>
public interface IArchiveSourceObservationRepository
{
    Task<ArchiveSourceObservationPersistenceResult> RecordScanObservationAsync(
        ArchiveCatalogueSource source,
        SourceAsset sourceAsset,
        Sha256Digest? verifiedContentHash,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ArchiveSourceObservationSnapshot?> GetNextPendingAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default);

    Task<ArchiveSourceObservationSnapshot?> GetAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default);

    Task<ArchiveSourceVerificationPersistenceResult> RecordVerifiedContentAsync(
        AssetId assetId,
        Sha256Digest contentHash,
        long sizeBytes,
        DateTimeOffset lastWriteTimeUtc,
        string mediaType,
        DateTimeOffset verifiedAtUtc,
        CancellationToken cancellationToken = default);
}
