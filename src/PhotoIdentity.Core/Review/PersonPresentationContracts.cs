using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Review;

public sealed record CataloguePersonRepresentativeFace(
    PersonId PersonId,
    FaceOccurrenceId FaceId,
    bool IsExplicit);

public interface IPersonPhotoCountRepository
{
    Task<IReadOnlyDictionary<PersonId, int>> GetActivePhotoCountsAsync(
        CancellationToken cancellationToken = default);
}

public interface IFavoritePeopleRepository
{
    Task<IReadOnlySet<PersonId>> GetFavoritePersonIdsAsync(
        CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(
        PersonId personId,
        bool isFavorite,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
}

public interface IPersonSmartCollectionVisibilityRepository
{
    Task<IReadOnlySet<PersonId>> GetHiddenPersonIdsAsync(
        CancellationToken cancellationToken = default);

    Task SetHiddenAsync(
        PersonId personId,
        bool hiddenFromSmartCollections,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
}

public interface IPersonFeaturedFaceRepository
{
    Task<CataloguePersonRepresentativeFace?> ResolveAsync(
        PersonId personId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<PersonId, CataloguePersonRepresentativeFace>> ResolveAllAsync(
        CancellationToken cancellationToken = default);

    Task SetFeaturedFaceAsync(
        PersonId personId,
        FaceOccurrenceId faceId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    Task ClearFeaturedFaceAsync(
        PersonId personId,
        CancellationToken cancellationToken = default);
}
