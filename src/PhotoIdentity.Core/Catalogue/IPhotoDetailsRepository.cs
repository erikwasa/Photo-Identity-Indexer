using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Core.Catalogue;

public sealed record PhotoDetailsPerson(
    PersonId PersonId,
    string DisplayName,
    int ConfirmedFaceCount,
    bool ManualPresence);

public sealed record PhotoDetails(
    AssetRevisionId RevisionId,
    string SourceKey,
    IReadOnlyList<PhotoDetailsPerson> People,
    PhotoCaptureMetadata? CaptureMetadata,
    CatalogueExtendedPhotoMetadata? ExtendedMetadata);

public interface IPhotoDetailsRepository
{
    Task<PhotoDetails?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);
}
