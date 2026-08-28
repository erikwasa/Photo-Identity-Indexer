using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

/// <summary>
/// Resolves the browser target rectangle for persisted face observations without opening originals.
/// Exact photo dimensions are preferred. Existing catalogues that predate persisted dimensions may
/// use the configured whole-photo review proxy as an aspect-ratio surrogate because that derivative
/// preserves source aspect ratio. Proxy dimensions are never exposed as original photo dimensions.
/// </summary>
public sealed class ReviewFaceTargetResolver
{
    private readonly SqliteArchiveReviewProxyRepository _proxyRepository;
    private readonly ReviewProxyServingConfiguration _configuration;

    public ReviewFaceTargetResolver(
        SqliteArchiveReviewProxyRepository proxyRepository,
        ReviewProxyServingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(proxyRepository);
        ArgumentNullException.ThrowIfNull(configuration);
        _proxyRepository = proxyRepository;
        _configuration = configuration;
    }

    public Task<IReadOnlyDictionary<FaceOccurrenceId, ReviewFaceTargetResponse>> ResolveAsync(
        IReadOnlyList<CatalogueReviewFace> faces,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(
            faces.Select(face => new TargetSource(
                face.Id,
                face.RevisionId,
                face.BoundingBoxJson,
                face.PhotoWidth,
                face.PhotoHeight)).ToArray(),
            cancellationToken);

    public Task<IReadOnlyDictionary<FaceOccurrenceId, ReviewFaceTargetResponse>> ResolveAsync(
        IReadOnlyList<CatalogueSuggestionGalleryFace> faces,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(
            faces.Select(face => new TargetSource(
                face.Id,
                face.RevisionId,
                face.BoundingBoxJson,
                face.PhotoWidth,
                face.PhotoHeight)).ToArray(),
            cancellationToken);

    private async Task<IReadOnlyDictionary<FaceOccurrenceId, ReviewFaceTargetResponse>> ResolveAsync(
        IReadOnlyList<TargetSource> faces,
        CancellationToken cancellationToken)
    {
        Dictionary<FaceOccurrenceId, ReviewFaceTargetResponse> targets = [];
        List<TargetSource> fallback = [];

        foreach (TargetSource face in faces)
        {
            NormalizedBoundingBox? target = ReviewFacePreviewResolver.CalculateTargetBoundingBox(
                face.BoundingBoxJson,
                face.PhotoWidth,
                face.PhotoHeight);
            if (target is NormalizedBoundingBox exactTarget)
            {
                targets[face.FaceId] = ToResponse(exactTarget);
                continue;
            }

            if ((face.PhotoWidth is not > 0 || face.PhotoHeight is not > 0) &&
                !string.IsNullOrWhiteSpace(face.BoundingBoxJson))
            {
                fallback.Add(face);
            }
        }

        if (fallback.Count == 0 || !_configuration.IsConfigured)
        {
            return targets;
        }

        IReadOnlyDictionary<AssetRevisionId, ArchiveReviewProxyRecord> proxies =
            await _proxyRepository.GetManyAsync(
                fallback.Select(face => face.RevisionId).Distinct().ToArray(),
                _configuration.ProfileId!,
                cancellationToken);

        foreach (TargetSource face in fallback)
        {
            if (!proxies.TryGetValue(face.RevisionId, out ArchiveReviewProxyRecord? proxy))
            {
                continue;
            }

            NormalizedBoundingBox? target =
                ReviewFacePreviewResolver.CalculateTargetBoundingBoxFromNormalizedObservation(
                    face.BoundingBoxJson,
                    proxy.Width,
                    proxy.Height);
            if (target is NormalizedBoundingBox fallbackTarget)
            {
                targets[face.FaceId] = ToResponse(fallbackTarget);
            }
        }

        return targets;
    }

    private static ReviewFaceTargetResponse ToResponse(NormalizedBoundingBox box) =>
        new(box.X, box.Y, box.Width, box.Height);

    private sealed record TargetSource(
        FaceOccurrenceId FaceId,
        AssetRevisionId RevisionId,
        string? BoundingBoxJson,
        int? PhotoWidth,
        int? PhotoHeight);
}
