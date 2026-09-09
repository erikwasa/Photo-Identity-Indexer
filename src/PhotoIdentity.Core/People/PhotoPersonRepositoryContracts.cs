using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.People;

public sealed record CatalogueManualPhotoPerson(
    PersonId PersonId,
    string DisplayName,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc);

public interface IPhotoPersonRepository
{
    Task<IReadOnlyList<CatalogueManualPhotoPerson>> GetManualPeopleAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogueManualPhotoPerson>> AddManualPersonAsync(
        AssetRevisionId revisionId,
        PersonId personId,
        string actor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogueManualPhotoPerson>> RemoveManualPersonAsync(
        AssetRevisionId revisionId,
        PersonId personId,
        string actor,
        CancellationToken cancellationToken = default);
}
