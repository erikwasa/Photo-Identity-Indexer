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
    string? CaptureDateSource = null,
    string? EffectivePlace = null);

public sealed record SmartCollectionPhotoPage(
    IReadOnlyList<SmartCollectionPhoto> Items,
    int Offset,
    int Limit,
    int Total,
    SmartCollectionFilter Filter);

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
