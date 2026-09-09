using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
namespace PhotoIdentity.Core.Sources;
public sealed record ArchiveSourceScanBaseline(
    string SourceKey,
    bool WasDeleted,
    ArchiveSourceObservationVerificationState? VerificationState,
    AssetRevisionId? VerifiedRevisionId,
    long? VerifiedSizeBytes,
    DateTimeOffset? VerifiedLastWriteTimeUtc,
    string? VerifiedMediaType,
    DateTimeOffset? VerifiedAtUtc,
    AssetRevisionId? LatestRevisionId)
{
    public bool CanReuseVerifiedRevision(SourceAsset sourceAsset)
    {
        ArgumentNullException.ThrowIfNull(sourceAsset);
        return !WasDeleted &&
            VerificationState == ArchiveSourceObservationVerificationState.Verified &&
            VerifiedRevisionId is not null &&
            VerifiedSizeBytes == sourceAsset.SizeBytes &&
            VerifiedLastWriteTimeUtc == sourceAsset.LastWriteTimeUtc.ToUniversalTime() &&
            string.Equals(VerifiedMediaType, sourceAsset.MediaType, StringComparison.Ordinal);
    }
}

public sealed record ArchiveSourceScanWrite(
    SourceAsset SourceAsset,
    Sha256Digest? VerifiedContentHash);
