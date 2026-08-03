using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueDetectorEvaluationRun(
    ProcessingRunId Id,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int PhotoCount,
    int DetectionCount);

public sealed record CatalogueDetectorEvaluationDetection(
    FaceOccurrenceId Id,
    int Ordinal,
    double Confidence,
    NormalizedBoundingBox BoundingBox);

public sealed record CatalogueDetectorEvaluationPhoto(
    AssetRevisionId RevisionId,
    string PhotoName,
    string MediaType,
    int? Width,
    int? Height,
    Sha256Digest RevisionHash,
    string JobStatus,
    IReadOnlyList<CatalogueDetectorEvaluationDetection> Detections);

public sealed record CatalogueDetectorEvaluationPhotoPage(
    IReadOnlyList<CatalogueDetectorEvaluationPhoto> Items,
    int Offset,
    int Limit,
    int Total);
