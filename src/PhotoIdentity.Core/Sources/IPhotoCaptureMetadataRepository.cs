using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

/// <summary>
/// Database-neutral persistence boundary for capture metadata attached to an immutable asset revision.
/// </summary>
public interface IPhotoCaptureMetadataRepository
{
    Task<PhotoCaptureMetadata?> GetPhotoMetadataAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task SavePhotoMetadataAsync(
        AssetRevisionId revisionId,
        PhotoCaptureMetadata metadata,
        DateTimeOffset extractedAtUtc,
        CancellationToken cancellationToken = default);
}
