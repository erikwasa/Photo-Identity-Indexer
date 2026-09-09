using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Recognition;

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

public interface IDetectorEvaluationCatalogueRepository
{
    Task<IReadOnlyList<CatalogueDetectorEvaluationRun>> GetRunsAsync(
        CancellationToken cancellationToken = default);

    Task<CatalogueDetectorEvaluationPhotoPage> GetPhotosAsync(
        ProcessingRunId processingRunId,
        int offset = 0,
        int limit = 8,
        CancellationToken cancellationToken = default);
}
