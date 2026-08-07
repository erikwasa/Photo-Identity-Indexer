namespace PhotoIdentity.Web.Contracts;

public sealed record DetectorRolloutBoxResponse(
    double X,
    double Y,
    double Width,
    double Height);

public sealed record DetectorRolloutOptionResponse(
    string FaceOccurrenceId,
    int Ordinal,
    DetectorRolloutBoxResponse BoundingBox,
    string CropImageUrl,
    string ReviewState,
    string? PersonDisplayName);

public sealed record DetectorRolloutResolutionResponse(
    string Kind,
    string? FaceOccurrenceId,
    string Actor,
    string? Note,
    DateTimeOffset CreatedAtUtc);

public sealed record DetectorRolloutPendingReviewResponse(
    string ProcessingRunId,
    string AssetRevisionId,
    int CandidateIndex,
    DetectorRolloutBoxResponse CandidateBoundingBox,
    string SourceImageUrl,
    string CandidateImageUrl,
    IReadOnlyList<DetectorRolloutOptionResponse> Options,
    DetectorRolloutResolutionResponse? LatestResolution);

public sealed record DetectorRolloutRunResponse(
    string ProcessingRunId,
    string ProcessingStatus,
    int RevisionCount,
    int SucceededRevisionCount,
    int FailedRevisionCount,
    int CandidateCount,
    int AppliedCount,
    int AmbiguousCount,
    int AwaitingReviewCount,
    int ReadyToApplyCount,
    int DeferredCount,
    int UnmatchedExistingCount,
    bool RolloutComplete);

public sealed record SaveDetectorRolloutResolutionRequest(
    string Action,
    string? FaceOccurrenceId,
    string Actor,
    string? Note = null);
