using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

public sealed record PhotoSlideshowExposureSummary(
    AssetRevisionId RevisionId,
    int ShowCount,
    DateTimeOffset? LastShownAtUtc);

public interface IPhotoSlideshowExposureRepository
{
    Task<bool> RecordPresentedAsync(
        Guid sessionId,
        SmartCollectionId collectionId,
        bool creative,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task<bool> RecordPresentedAsync(
        Guid sessionId,
        Guid collectionId,
        bool creative,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default) =>
        RecordPresentedAsync(
            sessionId,
            SmartCollectionId.From(collectionId),
            creative,
            revisionId,
            cancellationToken);

    Task<IReadOnlyDictionary<AssetRevisionId, PhotoSlideshowExposureSummary>> GetSummariesAsync(
        IEnumerable<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default);
}

public static class CreativeCollectionNoveltyPolicies
{
    public const string Disabled = "disabled";
    public const string BalancedV1 = "m26-slideshow-novelty-balanced-v1";
}
