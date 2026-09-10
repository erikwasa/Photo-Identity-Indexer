using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Api;

/// <summary>
/// Resolves a face occurrence to the opaque immutable asset revision that contains it.
/// Browser-facing callers receive only the revision identifier, never a source path.
/// </summary>
public sealed class ReviewFaceRevisionResolver
{
    private readonly IReviewFaceRepository _repository;

    public ReviewFaceRevisionResolver(IReviewFaceRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<AssetRevisionId?> ResolveAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        CatalogueReviewFace? face = await _repository.GetFaceAsync(faceOccurrenceId, cancellationToken);
        return face?.RevisionId;
    }
}
