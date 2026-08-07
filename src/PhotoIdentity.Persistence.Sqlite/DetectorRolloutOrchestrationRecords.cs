using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueDetectorRolloutOccurrenceAnchor(
    FaceOccurrenceId FaceOccurrenceId,
    int Ordinal,
    NormalizedBoundingBox BoundingBox,
    NormalizedFaceLandmarks Landmarks);

public sealed record CatalogueDetectorRolloutPendingReview(
    ProcessingRunId ProcessingRunId,
    AssetRevisionId AssetRevisionId,
    CatalogueDetectorReconciliationReview Review);

public sealed record CatalogueDetectorRolloutSummary(
    ProcessingRunId ProcessingRunId,
    int RevisionCount,
    int CandidateCount,
    int AppliedCount,
    int AmbiguousCount,
    int AwaitingReviewCount,
    int ReadyToApplyCount,
    int DeferredCount,
    int UnmatchedExistingCount);

public sealed record CatalogueDetectorRolloutApplyResult(
    ProcessingRunId ProcessingRunId,
    int ConsideredCount,
    int AppliedCount,
    int DeferredCount,
    int AwaitingReviewCount);
