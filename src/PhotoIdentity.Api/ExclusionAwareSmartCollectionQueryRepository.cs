using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Api;

/// <summary>
/// Defense-in-depth query boundary: even if a provider query forgets the exclusion predicate,
/// excluded revisions cannot leave the Smart Collection/slideshow repository contract.
/// </summary>
public sealed class ExclusionAwareSmartCollectionQueryRepository : ISmartCollectionQueryRepository
{
    private readonly ISmartCollectionQueryRepository _inner;
    private readonly ISourceCopyExclusionRepository _exclusions;

    public ExclusionAwareSmartCollectionQueryRepository(
        ISmartCollectionQueryRepository inner,
        ISourceCopyExclusionRepository exclusions)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(exclusions);
        _inner = inner;
        _exclusions = exclusions;
    }

    public async Task<SmartCollectionPhotoPage> QueryAsync(
        SmartCollectionFilter filter,
        int offset = 0,
        int limit = 40,
        CancellationToken cancellationToken = default)
    {
        SmartCollectionPhotoPage page = await _inner.QueryAsync(filter, offset, limit, cancellationToken);
        IReadOnlyList<SmartCollectionPhoto> visible = await FilterPhotosAsync(page.Items, cancellationToken);
        // Total is intentionally conservative here. It may temporarily include excluded rows from
        // later pages, but no excluded item is ever returned. QueryAll and slideshow materialization
        // perform complete filtering over the provider result.
        return page with { Items = visible };
    }

    public async Task<IReadOnlyList<SmartCollectionPhoto>> QueryAllAsync(
        SmartCollectionFilter filter,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SmartCollectionPhoto> values = await _inner.QueryAllAsync(filter, cancellationToken);
        return await FilterPhotosAsync(values, cancellationToken);
    }

    public async Task<SmartCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
        SmartCollectionId collectionId,
        CancellationToken cancellationToken = default)
    {
        SmartCollectionSlideshowSnapshot? snapshot =
            await _inner.CreateSlideshowSnapshotAsync(collectionId, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        List<PhotoIdentity.Core.Identifiers.AssetRevisionId> visible = [];
        foreach (PhotoIdentity.Core.Identifiers.AssetRevisionId revisionId in snapshot.RevisionIds)
        {
            if (!await _exclusions.IsRevisionExcludedAsync(revisionId, cancellationToken))
            {
                visible.Add(revisionId);
            }
        }

        return snapshot with { RevisionIds = visible };
    }

    private async Task<IReadOnlyList<SmartCollectionPhoto>> FilterPhotosAsync(
        IReadOnlyList<SmartCollectionPhoto> values,
        CancellationToken cancellationToken)
    {
        List<SmartCollectionPhoto> visible = new(values.Count);
        foreach (SmartCollectionPhoto value in values)
        {
            if (!await _exclusions.IsRevisionExcludedAsync(value.RevisionId, cancellationToken))
            {
                visible.Add(value);
            }
        }
        return visible;
    }
}
