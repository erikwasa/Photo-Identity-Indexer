using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

/// <summary>
/// Finds review-eligible faces that are nearest to one source face under one exact embedding revision.
/// Similarity is advisory derived evidence and never creates or changes a canonical identity decision.
/// </summary>
public interface ISimilarFaceRepository
{
    Task<CatalogueSimilarFaceQueryResult?> FindSimilarAsync(
        FaceOccurrenceId sourceFaceId,
        ModelId modelId,
        Sha256Digest modelHash,
        bool includeUnknown = false,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

public sealed record CatalogueSimilarFace(
    CatalogueReviewFace Face,
    double Similarity);

public sealed record CatalogueSimilarFaceQueryResult(
    FaceOccurrenceId SourceFaceId,
    ModelId ModelId,
    Sha256Digest ModelHash,
    bool IncludeUnknown,
    int ScannedFaceCount,
    long ElapsedMilliseconds,
    IReadOnlyList<CatalogueSimilarFace> Items);
