using PhotoIdentity.Core.Identifiers;

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
    double? Longitude);

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

    Task<SmartCollectionSlideshowSnapshot?> CreateSlideshowSnapshotAsync(
        SmartCollectionId collectionId,
        CancellationToken cancellationToken = default);
}
