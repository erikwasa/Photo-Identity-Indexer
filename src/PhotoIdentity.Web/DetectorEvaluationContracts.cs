namespace PhotoIdentity.Web.Contracts;

public sealed record DetectorEvaluationRunResponse(
    string Id,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int PhotoCount,
    int DetectionCount);

public sealed record DetectorEvaluationBoundingBoxResponse(
    double X,
    double Y,
    double Width,
    double Height);

public sealed record DetectorEvaluationDetectionResponse(
    string Id,
    int FaceNumber,
    double Confidence,
    DetectorEvaluationBoundingBoxResponse BoundingBox);

public sealed record DetectorEvaluationPhotoResponse(
    string RevisionId,
    string PhotoName,
    string MediaType,
    int? Width,
    int? Height,
    string RevisionHashPrefix,
    string JobStatus,
    string ContentUrl,
    IReadOnlyList<DetectorEvaluationDetectionResponse> Detections);

public sealed record DetectorEvaluationPhotoPageResponse(
    IReadOnlyList<DetectorEvaluationPhotoResponse> Items,
    int Offset,
    int Limit,
    int Total);

public sealed record DetectorEvaluationManifestEntryRequest(
    string SampleId,
    string ImageName,
    string SampleGroup,
    string SourceGroup,
    string PrimaryCategory,
    int CountableFaces,
    string? SourceSha256);

public sealed record CreateDetectorEvaluationSessionRequest(
    string Name,
    string ProcessingRunId,
    IReadOnlyList<DetectorEvaluationManifestEntryRequest> Photos);

public sealed record DetectorEvaluationSessionSummaryResponse(
    string Id,
    string Name,
    string ProcessingRunId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int PhotoCount,
    int CompletedPhotoCount);

public sealed record DetectorEvaluationSessionDetectionResponse(
    string Id,
    int FaceNumber,
    double Confidence,
    DetectorEvaluationBoundingBoxResponse BoundingBox,
    string? Disposition);

public sealed record DetectorEvaluationMissedFaceResponse(
    string Id,
    DetectorEvaluationBoundingBoxResponse BoundingBox);

public sealed record DetectorEvaluationSessionPhotoResponse(
    string RevisionId,
    string PhotoName,
    string MediaType,
    int? Width,
    int? Height,
    string RevisionHashPrefix,
    string ContentUrl,
    string SampleId,
    string SampleGroup,
    string SourceGroup,
    string PrimaryCategory,
    int CountableFaces,
    IReadOnlyList<DetectorEvaluationSessionDetectionResponse> Detections,
    IReadOnlyList<DetectorEvaluationMissedFaceResponse> MissedFaces,
    string? MissReason,
    string? Notes,
    int CorrectDetections,
    int BackgroundUnknownDetections,
    int FalseDetections,
    int DuplicateDetections,
    bool IsComplete);

public sealed record DetectorEvaluationSessionResponse(
    string Id,
    string Name,
    string ProcessingRunId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int CompletedPhotoCount,
    IReadOnlyList<DetectorEvaluationSessionPhotoResponse> Photos);

public sealed record DetectorEvaluationDetectionJudgementRequest(
    string DetectionId,
    string? Disposition);

public sealed record DetectorEvaluationMissedFaceRequest(
    string Id,
    DetectorEvaluationBoundingBoxResponse BoundingBox);

public sealed record SaveDetectorEvaluationPhotoReviewRequest(
    IReadOnlyList<DetectorEvaluationDetectionJudgementRequest> DetectionJudgements,
    IReadOnlyList<DetectorEvaluationMissedFaceRequest> MissedFaces,
    string? MissReason,
    string? Notes);

public sealed record DetectorEvaluationGroundTruthSummaryResponse(
    string BaselineSessionId,
    string Name,
    DateTimeOffset FrozenAtUtc,
    int PhotoCount,
    int FaceCount);

public sealed record CreateDetectorEvaluationComparisonRequest(
    string Name,
    string BaselineSessionId,
    string CandidateProcessingRunId,
    double? IouThreshold);

public sealed record DetectorEvaluationComparisonSummaryResponse(
    string Id,
    string Name,
    string BaselineSessionId,
    string BaselineName,
    string CandidateProcessingRunId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int PhotoCount,
    int ExceptionPhotoCount,
    int ResolvedExceptionPhotoCount,
    string GateStatus);

public sealed record DetectorEvaluationComparisonGroundTruthFaceResponse(
    string Id,
    DetectorEvaluationBoundingBoxResponse BoundingBox,
    bool IsBackgroundUnknown,
    string Origin);

public sealed record DetectorEvaluationComparisonCandidateDetectionResponse(
    string Id,
    int FaceNumber,
    double Confidence,
    DetectorEvaluationBoundingBoxResponse BoundingBox);

public sealed record DetectorEvaluationComparisonExceptionComponentResponse(
    string Id,
    string Kind,
    IReadOnlyList<DetectorEvaluationComparisonGroundTruthFaceResponse> GroundTruthFaces,
    IReadOnlyList<DetectorEvaluationComparisonCandidateDetectionResponse> CandidateDetections);

public sealed record DetectorEvaluationComparisonManualMatchResponse(
    string GroundTruthFaceId,
    string CandidateDetectionId);

public sealed record DetectorEvaluationComparisonCorrectionResponse(
    IReadOnlyList<DetectorEvaluationComparisonManualMatchResponse> Matches,
    IReadOnlyList<string> FalseCandidateDetectionIds,
    IReadOnlyList<string> DuplicateCandidateDetectionIds,
    IReadOnlyList<string> MissedGroundTruthFaceIds,
    string? Notes)
{
    public IReadOnlyList<string> NeutralCandidateDetectionIds { get; init; } = [];
}

public sealed record DetectorEvaluationComparisonPhotoResponse(
    string CandidateRevisionId,
    string PhotoName,
    string RevisionHashPrefix,
    string ContentUrl,
    string SampleId,
    string SampleGroup,
    string SourceGroup,
    string PrimaryCategory,
    int CountableFaces,
    int AutomaticMatchCount,
    IReadOnlyList<DetectorEvaluationComparisonExceptionComponentResponse> ExceptionComponents,
    DetectorEvaluationComparisonCorrectionResponse Correction,
    bool IsResolved);

public sealed record DetectorEvaluationComparisonMetricsResponse(
    int PhotoCount,
    int CountableFaces,
    int MatchedFaces,
    int MissedFaces,
    int UnresolvedGroundTruthFaces,
    double Recall,
    int FalseDetections,
    int DuplicateDetections,
    int UnresolvedCandidateDetections)
{
    public int NeutralDetections { get; init; }
}

public sealed record DetectorEvaluationComparisonGroupSummaryResponse(
    string Group,
    DetectorEvaluationComparisonMetricsResponse Metrics);

public sealed record DetectorEvaluationM16GateResponse(
    string Status,
    bool IsComparisonComplete,
    bool? MaterialCategoryFailure,
    string? Notes,
    double OverallRecallTarget,
    double FivePlusRecallTarget,
    int FalseOrDuplicateLimit,
    bool OverallRecallPass,
    bool FivePlusRecallPass,
    bool FalseOrDuplicatePass,
    bool? MaterialCategoryPass);

public sealed record DetectorEvaluationComparisonResponse(
    string Id,
    string Name,
    string BaselineSessionId,
    string BaselineName,
    DateTimeOffset GroundTruthFrozenAtUtc,
    string CandidateProcessingRunId,
    double IouThreshold,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DetectorEvaluationComparisonMetricsResponse Overall,
    DetectorEvaluationComparisonMetricsResponse FivePlusFaces,
    IReadOnlyList<DetectorEvaluationComparisonGroupSummaryResponse> SourceGroups,
    IReadOnlyList<DetectorEvaluationComparisonGroupSummaryResponse> Categories,
    DetectorEvaluationM16GateResponse M16Gate,
    IReadOnlyList<DetectorEvaluationComparisonPhotoResponse> ExceptionPhotos);

public sealed record DetectorEvaluationComparisonManualMatchRequest(
    string GroundTruthFaceId,
    string CandidateDetectionId);

public sealed record SaveDetectorEvaluationComparisonPhotoRequest(
    IReadOnlyList<DetectorEvaluationComparisonManualMatchRequest> Matches,
    IReadOnlyList<string> FalseCandidateDetectionIds,
    IReadOnlyList<string> DuplicateCandidateDetectionIds,
    IReadOnlyList<string> MissedGroundTruthFaceIds,
    string? Notes)
{
    public IReadOnlyList<string> NeutralCandidateDetectionIds { get; init; } = [];
}

public sealed record SaveDetectorEvaluationM16GateRequest(
    bool? MaterialCategoryFailure,
    string? Notes);
