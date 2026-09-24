using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Core.Collections;

public sealed record SmartCollectionPhoto(
    AssetRevisionId RevisionId,
    AssetId AssetId,
    DateTimeOffset ObservedAtUtc,
    string? MediaType,
    int? Width,
    int? Height,
    DateTime? TakenAtLocal,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<string>? PeopleKeys = null,
    PhotoCaptureDateRange? EffectiveCaptureDate = null,
    string? CaptureDateSource = null);

public sealed record SmartCollectionPhotoPage(
    IReadOnlyList<SmartCollectionPhoto> Items,
    int Offset,
    int Limit,
    int Total,
    SmartCollectionFilter Filter);

public sealed record SmartCollectionPhotoNavigation(
    bool Found,
    int? Index,
    int Total,
    AssetRevisionId? PreviousRevisionId,
    AssetRevisionId? NextRevisionId);

public sealed record SmartCollectionSlideshowSnapshot(
    SmartCollectionId CollectionId,
    string CollectionName,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<AssetRevisionId> RevisionIds);

public interface ISmartCollectionQueryRepository
{
    Task<SmartCollectionPhotoPage> QueryAsync(
        SmartCollectionFilter filter,
        int offset = 0,
        int limit = 40,
        CancellationToken cancellationToken = default);

    async Task<SmartCollectionPhotoNavigation> GetNavigationAsync(
        SmartCollectionFilter filter,
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        const int pageSize = 200;
        AssetRevisionId? previousRevisionId = null;
        int offset = 0;
        int total = 0;

        while (true)
        {
            SmartCollectionPhotoPage page = await QueryAsync(
                filter,
                offset,
                pageSize,
                cancellationToken);
            total = page.Total;

            for (int index = 0; index < page.Items.Count; index++)
            {
                SmartCollectionPhoto photo = page.Items[index];
                if (photo.RevisionId != revisionId)
                {
                    previousRevisionId = photo.RevisionId;
                    continue;
                }

                AssetRevisionId? nextRevisionId = index + 1 < page.Items.Count
                    ? page.Items[index + 1].RevisionId
                    : null;
                if (nextRevisionId is null && offset + page.Items.Count < page.Total)
                {
                    SmartCollectionPhotoPage nextPage = await QueryAsync(
                        filter,
                        offset + page.Items.Count,
                        1,
                        cancellationToken);
                    nextRevisionId = nextPage.Items.Count == 0
                        ? null
                        : nextPage.Items[0].RevisionId;
                }

                return new SmartCollectionPhotoNavigation(
                    true,
                    offset + index,
                    page.Total,
                    previousRevisionId,
                    nextRevisionId);
            }

            offset += page.Items.Count;
            if (page.Items.Count == 0 || offset >= page.Total)
            {
                break;
            }
        }

        return new SmartCollectionPhotoNavigation(
            false,
            null,
            total,
            null,
            null);
    }

    async Task<IReadOnlyList<SmartCollectionPhoto>> QueryAllAsync(
        SmartCollectionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        const int pageSize = 200;
        List<SmartCollectionPhoto> items = [];
        int offset = 0;

        while (true)
        {
            SmartCollectionPhotoPage page = await QueryAsync(
                filter,
                offset,
                pageSize,
                cancellationToken);
            items.AddRange(page.Items);
            offset += page.Items.Count;

            if (page.Items.Count == 0 || offset >= page.Total)
            {
                break;
            }
        }

        return items;
    }

    Task<SmartCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
        SmartCollectionId collectionId,
        CancellationToken cancellationToken = default);
}
