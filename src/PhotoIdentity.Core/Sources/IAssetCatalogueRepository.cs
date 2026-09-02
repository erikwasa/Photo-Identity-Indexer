using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

/// <summary>
/// Database-neutral persistence boundary for source, asset and immutable revision records.
/// Provider adapters preserve stable identifiers and source/revision uniqueness semantics.
/// </summary>
public interface IAssetCatalogueRepository
{
    Task<CatalogueAssetRevision> SaveRevisionAsync(
        CatalogueSource source,
        CatalogueAsset asset,
        CatalogueAssetRevision revision,
        CancellationToken cancellationToken = default);

    Task<CatalogueSource?> GetSourceAsync(
        SourceId id,
        CancellationToken cancellationToken = default);

    Task<CatalogueSource?> FindSourceAsync(
        string kind,
        string rootLocator,
        CancellationToken cancellationToken = default);

    Task<CatalogueAsset?> GetAssetAsync(
        AssetId id,
        CancellationToken cancellationToken = default);

    Task<CatalogueAsset?> FindAssetAsync(
        SourceId sourceId,
        string sourceKey,
        CancellationToken cancellationToken = default);

    Task<CatalogueAssetRevision?> GetRevisionAsync(
        AssetRevisionId id,
        CancellationToken cancellationToken = default);

    Task<CatalogueAssetRevision?> GetLatestRevisionAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default);

    Task<PhotoCaptureMetadata?> GetPhotoMetadataAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task SavePhotoMetadataAsync(
        AssetRevisionId revisionId,
        PhotoCaptureMetadata metadata,
        DateTimeOffset extractedAtUtc,
        CancellationToken cancellationToken = default);
}
