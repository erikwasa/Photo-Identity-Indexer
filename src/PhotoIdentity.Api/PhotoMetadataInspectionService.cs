using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

/// <summary>
/// Owns the durable metadata-inspection write boundary. Callers are responsible for supplying
/// an already-local stream whose bytes have been verified against the immutable revision.
/// The WI-0050 capture row is written last and therefore remains the durable "inspection complete"
/// marker if a process interruption occurs between extended and capture metadata persistence.
/// </summary>
public sealed class PhotoMetadataInspectionService
{
    private readonly SqliteAssetCatalogueRepository _catalogue;
    private readonly SqliteExtendedPhotoMetadataRepository _extendedMetadata;
    private readonly IPhotoMetadataReader _reader;
    private readonly TimeProvider _timeProvider;

    public PhotoMetadataInspectionService(
        SqliteAssetCatalogueRepository catalogue,
        SqliteExtendedPhotoMetadataRepository extendedMetadata,
        IPhotoMetadataReader reader,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(extendedMetadata);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _catalogue = catalogue;
        _extendedMetadata = extendedMetadata;
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async Task<bool> IsInspectedAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default) =>
        await _catalogue.GetPhotoMetadataAsync(revisionId, cancellationToken) is not null;

    public async Task<PhotoCaptureMetadata> InspectVerifiedAsync(
        AssetRevisionId revisionId,
        Stream content,
        string? mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        PhotoCaptureMetadata metadata = await _reader.ReadAsync(content, mediaType, cancellationToken);
        await _extendedMetadata.SaveAsync(revisionId, metadata, cancellationToken);
        await _catalogue.SavePhotoMetadataAsync(
            revisionId,
            metadata,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        return metadata;
    }
}
