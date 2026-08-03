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
