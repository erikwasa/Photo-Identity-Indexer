using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Tags;

public sealed record CatalogueManualPhotoTag(
    long TagId,
    string NormalizedValue,
    string Value,
    string Name,
    long? ParentTagId,
    string? ParentValue,
    string? Color,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public sealed record CataloguePhotoTagDefinition(
    long TagId,
    string NormalizedValue,
    string Value,
    string Name,
    long? ParentTagId,
    string? ParentValue,
    string? Color);

public interface IPhotoTagRepository
{
    Task<IReadOnlyList<CataloguePhotoTagDefinition>> GetCanonicalTagsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogueManualPhotoTag>> GetManualTagsAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogueManualPhotoTag>> AddManualTagAsync(
        AssetRevisionId revisionId,
        string tagValue,
        string actor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogueManualPhotoTag>> RemoveManualTagAsync(
        AssetRevisionId revisionId,
        string tagValue,
        string actor,
        CancellationToken cancellationToken = default);
}
