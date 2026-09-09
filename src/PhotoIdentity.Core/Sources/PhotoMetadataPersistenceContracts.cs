using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Sources;

public sealed record CatalogueExtendedPhotoMetadata(
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    string? Orientation,
    string? ExposureTime,
    string? Aperture,
    string? Iso,
    string? FocalLength,
    string? FocalLength35Mm,
    string? Flash,
    string? GpsAltitude,
    IReadOnlyList<PhotoMetadataTag> RawTags);

public interface IExtendedPhotoMetadataRepository
{
    Task SaveAsync(
        AssetRevisionId revisionId,
        PhotoCaptureMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<CatalogueExtendedPhotoMetadata?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);
}

public sealed record CataloguePhotoMetadataInspection(
    int ExtractionContractVersion,
    DateTimeOffset InspectedAtUtc);

public interface IPhotoMetadataInspectionRepository
{
    Task<CataloguePhotoMetadataInspection?> GetAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task<bool> IsCurrentAsync(
        AssetRevisionId revisionId,
        int currentVersion,
        CancellationToken cancellationToken = default);

    Task MarkAsync(
        AssetRevisionId revisionId,
        int extractionContractVersion,
        DateTimeOffset inspectedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed record PhotoMetadataBackfillRefreshCandidate(
    AssetRevisionId RevisionId,
    Sha256Digest ContentHash,
    long SizeBytes,
    string RootLocator,
    string SourceKey,
    string? MediaType,
    bool HasCaptureMetadata,
    int? ExtractionContractVersion)
{
    public bool IsNew => !HasCaptureMetadata;

    public bool IsStale(int currentVersion) =>
        HasCaptureMetadata &&
        (ExtractionContractVersion ?? PhotoMetadataExtractionContract.LegacyVersion) < currentVersion;
}

public interface IPhotoMetadataBackfillRepository
{
    Task<IReadOnlyList<PhotoMetadataBackfillRefreshCandidate>> GetRefreshCandidatesAsync(
        int limit,
        int offset,
        int currentVersion,
        bool force,
        CancellationToken cancellationToken = default);
}
