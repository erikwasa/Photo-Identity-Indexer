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
