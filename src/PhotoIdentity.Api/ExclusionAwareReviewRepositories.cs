using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Api;

/// <summary>
/// Prevents callers holding a face identifier from reading or mutating review history after the
/// owning source copy has crossed the durable privacy boundary.
/// </summary>
public sealed class ExclusionAwareReviewActionRepository : IReviewActionRepository
{
    private readonly IReviewActionRepository _inner;
    private readonly ISourceCopyExclusionRepository _exclusions;

    public ExclusionAwareReviewActionRepository(
        IReviewActionRepository inner,
        ISourceCopyExclusionRepository exclusions)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _exclusions = exclusions ?? throw new ArgumentNullException(nameof(exclusions));
    }

    public Task<ReviewPerson> CreatePersonAsync(
        string displayName,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default) =>
        _inner.CreatePersonAsync(displayName, createdAtUtc, cancellationToken);

    public async Task<ReviewAction> AssignAsync(
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        await RequireAvailableAsync(faceOccurrenceId, cancellationToken);
        return await _inner.AssignAsync(faceOccurrenceId, personId, actor, createdAtUtc, note, cancellationToken);
    }

    public async Task<ReviewAction> MarkUnknownAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        await RequireAvailableAsync(faceOccurrenceId, cancellationToken);
        return await _inner.MarkUnknownAsync(faceOccurrenceId, actor, createdAtUtc, note, cancellationToken);
    }

    public async Task<ReviewAction> RejectAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        await RequireAvailableAsync(faceOccurrenceId, cancellationToken);
        return await _inner.RejectAsync(faceOccurrenceId, actor, createdAtUtc, note, cancellationToken);
    }

    public async Task<ReviewAction?> UndoLatestAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        await RequireAvailableAsync(faceOccurrenceId, cancellationToken);
        return await _inner.UndoLatestAsync(faceOccurrenceId, actor, createdAtUtc, note, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewAction>> GetActionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        if (await _exclusions.IsFaceOccurrenceExcludedAsync(faceOccurrenceId, cancellationToken))
        {
            return [];
        }
        return await _inner.GetActionsAsync(faceOccurrenceId, cancellationToken);
    }

    private async Task RequireAvailableAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        if (await _exclusions.IsFaceOccurrenceExcludedAsync(faceOccurrenceId, cancellationToken))
        {
            throw new KeyNotFoundException("The face occurrence is unavailable.");
        }
    }
}

public sealed class ExclusionAwareReviewSuggestionRepository : IReviewSuggestionRepository
{
    private readonly IReviewSuggestionRepository _inner;
    private readonly ISourceCopyExclusionRepository _exclusions;

    public ExclusionAwareReviewSuggestionRepository(
        IReviewSuggestionRepository inner,
        ISourceCopyExclusionRepository exclusions)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _exclusions = exclusions ?? throw new ArgumentNullException(nameof(exclusions));
    }

    public async Task<IReadOnlyList<ReviewIdentitySuggestion>> GetSuggestionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        if (await _exclusions.IsFaceOccurrenceExcludedAsync(faceOccurrenceId, cancellationToken))
        {
            return [];
        }
        return await _inner.GetSuggestionsAsync(faceOccurrenceId, cancellationToken);
    }

    public async Task<ReviewIdentitySuggestion> AcceptAsync(
        FaceOccurrenceId faceOccurrenceId,
        long suggestionId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        await RequireAvailableAsync(faceOccurrenceId, cancellationToken);
        return await _inner.AcceptAsync(faceOccurrenceId, suggestionId, actor, createdAtUtc, note, cancellationToken);
    }

    public async Task<ReviewIdentitySuggestion> RejectAsync(
        FaceOccurrenceId faceOccurrenceId,
        long suggestionId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        await RequireAvailableAsync(faceOccurrenceId, cancellationToken);
        return await _inner.RejectAsync(faceOccurrenceId, suggestionId, actor, createdAtUtc, note, cancellationToken);
    }

    private async Task RequireAvailableAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        if (await _exclusions.IsFaceOccurrenceExcludedAsync(faceOccurrenceId, cancellationToken))
        {
            throw new KeyNotFoundException("The face occurrence is unavailable.");
        }
    }
}

public sealed class ExclusionAwareSuggestionGalleryRepository : ISuggestionGalleryRepository
{
    private readonly ISuggestionGalleryRepository _inner;
    private readonly ISourceCopyExclusionRepository _exclusions;

    public ExclusionAwareSuggestionGalleryRepository(
        ISuggestionGalleryRepository inner,
        ISourceCopyExclusionRepository exclusions)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _exclusions = exclusions ?? throw new ArgumentNullException(nameof(exclusions));
    }

    public async Task<ReviewSuggestionGalleryPage> GetFacesAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int offset,
        int limit,
        string state,
        ProcessingRunId? processingRunId,
        string sort,
        string confidenceGroup,
        PersonId? suggestedPersonId,
        CancellationToken cancellationToken = default)
    {
        ReviewSuggestionGalleryPage page = await _inner.GetFacesAsync(
            modelId,
            modelHash,
            offset,
            limit,
            state,
            processingRunId,
            sort,
            confidenceGroup,
            suggestedPersonId,
            cancellationToken);

        List<ReviewSuggestionGalleryFace> visible = new(page.Items.Count);
        foreach (ReviewSuggestionGalleryFace face in page.Items)
        {
            if (!await _exclusions.IsFaceOccurrenceExcludedAsync(face.Id, cancellationToken))
            {
                visible.Add(face);
            }
        }
        return page with { Items = visible };
    }

    public async Task<ReviewSuggestionGalleryNavigation?> GetNavigationAsync(
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash,
        string state,
        ProcessingRunId? processingRunId,
        string sort,
        string confidenceGroup,
        PersonId? suggestedPersonId,
        CancellationToken cancellationToken = default)
    {
        if (await _exclusions.IsFaceOccurrenceExcludedAsync(faceOccurrenceId, cancellationToken))
        {
            return null;
        }

        ReviewSuggestionGalleryNavigation? navigation = await _inner.GetNavigationAsync(
            faceOccurrenceId,
            modelId,
            modelHash,
            state,
            processingRunId,
            sort,
            confidenceGroup,
            suggestedPersonId,
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
}
