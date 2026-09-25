using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Api;

public sealed class ExclusionAwareCollectionQueryRepository : ICollectionQueryRepository
{
    private readonly ICollectionQueryRepository _inner;
    private readonly ISourceCopyExclusionRepository _exclusions;

    public ExclusionAwareCollectionQueryRepository(
        ICollectionQueryRepository inner,
        ISourceCopyExclusionRepository exclusions)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _exclusions = exclusions ?? throw new ArgumentNullException(nameof(exclusions));
    }

    public async Task<CollectionPhotoPage> QueryPhotosAsync(
        IReadOnlyCollection<PersonId> personIds,
        string matchMode = CollectionMatchModes.All,
        CollectionSuggestionPolicy? suggestionPolicy = null,
        string? reviewState = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        double? minimumConfidence = null,
        int offset = 0,
        int limit = 40,
        CancellationToken cancellationToken = default)
    {
        CollectionPhotoPage page = await _inner.QueryPhotosAsync(
            personIds,
            matchMode,
            suggestionPolicy,
            reviewState,
            fromUtc,
            toUtc,
            minimumConfidence,
            offset,
            limit,
            cancellationToken);

        List<CollectionPhoto> visible = new(page.Items.Count);
        foreach (CollectionPhoto photo in page.Items)
        {
            if (!await _exclusions.IsRevisionExcludedAsync(photo.RevisionId, cancellationToken))
            {
                visible.Add(photo);
            }
        }
        return page with { Items = visible };
    }
}

public sealed class ExclusionAwarePhotoDetailsRepository : IPhotoDetailsRepository
{
    private readonly IPhotoDetailsRepository _inner;
    private readonly ISourceCopyExclusionRepository _exclusions;

    public ExclusionAwarePhotoDetailsRepository(
        IPhotoDetailsRepository inner,
        ISourceCopyExclusionRepository exclusions)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _exclusions = exclusions ?? throw new ArgumentNullException(nameof(exclusions));
    }

    public async Task<PhotoDetails?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        if (await _exclusions.IsRevisionExcludedAsync(revisionId, cancellationToken))
        {
            return null;
        }
        return await _inner.GetAsync(revisionId, cancellationToken);
    }
}

public sealed class ExclusionAwareReviewFaceRepository : IReviewFaceRepository
{
    private readonly IReviewFaceRepository _inner;
    private readonly ISourceCopyExclusionRepository _exclusions;

    public ExclusionAwareReviewFaceRepository(
        IReviewFaceRepository inner,
        ISourceCopyExclusionRepository exclusions)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _exclusions = exclusions ?? throw new ArgumentNullException(nameof(exclusions));
    }

    public async Task<CatalogueReviewFace?> GetFaceAsync(
        FaceOccurrenceId id,
        CancellationToken cancellationToken = default)
    {
        if (await _exclusions.IsFaceOccurrenceExcludedAsync(id, cancellationToken))
        {
            return null;
        }
        return await _inner.GetFaceAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<CatalogueReviewPerson>> GetPeopleAsync(
        CancellationToken cancellationToken = default) =>
        _inner.GetPeopleAsync(cancellationToken);
}

public sealed class ExclusionAwareReviewFilterRepository : IReviewFilterRepository
{
    private readonly IReviewFilterRepository _inner;
    private readonly ISourceCopyExclusionRepository _exclusions;

    public ExclusionAwareReviewFilterRepository(
        IReviewFilterRepository inner,
        ISourceCopyExclusionRepository exclusions)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _exclusions = exclusions ?? throw new ArgumentNullException(nameof(exclusions));
    }

    public async Task<CatalogueReviewFacePage> GetFacesAsync(
        int offset = 0,
        int limit = 40,
        string state = CatalogueReviewStates.Unreviewed,
        ProcessingRunId? processingRunId = null,
        ModelId? modelId = null,
        Sha256Digest? modelHash = null,
        string sort = CatalogueReviewSorts.CreatedDescending,
        CancellationToken cancellationToken = default)
    {
        CatalogueReviewFacePage page = await _inner.GetFacesAsync(
            offset,
            limit,
            state,
            processingRunId,
            modelId,
            modelHash,
            sort,
            cancellationToken);

        List<CatalogueReviewFace> visible = new(page.Items.Count);
        foreach (CatalogueReviewFace face in page.Items)
        {
            if (!await _exclusions.IsFaceOccurrenceExcludedAsync(face.Id, cancellationToken))
            {
                visible.Add(face);
            }
        }
        return page with { Items = visible };
    }

    public async Task<CatalogueReviewFaceNavigation?> GetNavigationAsync(
        FaceOccurrenceId faceOccurrenceId,
        string state = "all",
        ProcessingRunId? processingRunId = null,
        ModelId? modelId = null,
        Sha256Digest? modelHash = null,
        string sort = CatalogueReviewSorts.CreatedDescending,
        CancellationToken cancellationToken = default)
    {
        if (await _exclusions.IsFaceOccurrenceExcludedAsync(faceOccurrenceId, cancellationToken))
        {
            return null;
        }

        CatalogueReviewFaceNavigation? navigation = await _inner.GetNavigationAsync(
            faceOccurrenceId,
            state,
            processingRunId,
            modelId,
            modelHash,
            sort,
            cancellationToken);
        if (navigation is null)
        {
            return null;
        }

        FaceOccurrenceId? previous = navigation.PreviousFaceId;
        if (previous is FaceOccurrenceId previousId &&
            await _exclusions.IsFaceOccurrenceExcludedAsync(previousId, cancellationToken))
        {
            previous = null;
        }

        FaceOccurrenceId? next = navigation.NextFaceId;
        if (next is FaceOccurrenceId nextId &&
            await _exclusions.IsFaceOccurrenceExcludedAsync(nextId, cancellationToken))
        {
            next = null;
        }

        return navigation with { PreviousFaceId = previous, NextFaceId = next };
    }

    public Task<CatalogueReviewFilterOptions> GetOptionsAsync(
        CancellationToken cancellationToken = default) =>
        _inner.GetOptionsAsync(cancellationToken);
}
