using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Imaging;

public interface IFaceReviewDerivativeRepository
{
    Task<FaceReviewDerivativeRecord?> GetAsync(FaceOccurrenceId faceOccurrenceId, string profileId, CancellationToken cancellationToken = default);
    Task<bool> IsRevisionCompleteAsync(AssetRevisionId revisionId, string profileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FaceReviewGeometry>> GetFacesAsync(AssetRevisionId revisionId, CancellationToken cancellationToken = default);
    /// <summary>Atomically records derivatives and completion; every face must belong to the requested revision.</summary>
    Task RecordRevisionCompletionAsync(AssetRevisionId revisionId, string profileId,
        IReadOnlyList<FaceReviewDerivativeRecord> derivatives, DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);
}

public interface IFaceReviewDerivativeBackfillRepository
{
    Task<AssetRevisionId?> GetNextPendingCurrentRevisionAsync(SourceId sourceId, string profileId, CancellationToken cancellationToken = default);
}
