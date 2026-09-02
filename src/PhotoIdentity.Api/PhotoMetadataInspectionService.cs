using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

/// <summary>
/// Owns the durable metadata-inspection write boundary. Callers are responsible for supplying
/// an already-local stream whose bytes have been verified against the immutable revision.
/// Extended and capture metadata are written before the extraction-version marker, so an
/// interrupted refresh remains stale and is safely retried later.
/// </summary>
public sealed class PhotoMetadataInspectionService
{
    private readonly IPhotoCaptureMetadataRepository _catalogue;
    private readonly SqliteExtendedPhotoMetadataRepository _extendedMetadata;
    private readonly SqlitePhotoMetadataInspectionRepository _inspections;
    private readonly IPhotoMetadataReader _reader;
    private readonly TimeProvider _timeProvider;

    public PhotoMetadataInspectionService(
        IPhotoCaptureMetadataRepository catalogue,
        SqliteExtendedPhotoMetadataRepository extendedMetadata,
        SqlitePhotoMetadataInspectionRepository inspections,
        IPhotoMetadataReader reader,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(extendedMetadata);
        ArgumentNullException.ThrowIfNull(inspections);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _catalogue = catalogue;
        _extendedMetadata = extendedMetadata;
        _inspections = inspections;
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async Task<bool> IsInspectedAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        if (!await _inspections.IsCurrentAsync(
                revisionId,
                PhotoMetadataExtractionContract.CurrentVersion,
                cancellationToken))
        {
            return false;
        }

        // The version marker is written last, so a current marker should always have its capture
        // row. Treat a missing row as incomplete/corrupt rather than allowing archive advancement
        // to skip repair.
        return await _catalogue.GetPhotoMetadataAsync(revisionId, cancellationToken) is not null;
    }

    public async Task<PhotoCaptureMetadata> InspectVerifiedAsync(
        AssetRevisionId revisionId,
        Stream content,
        string? mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        PhotoCaptureMetadata metadata = await _reader.ReadAsync(content, mediaType, cancellationToken);
        DateTimeOffset inspectedAtUtc = _timeProvider.GetUtcNow();

        await _extendedMetadata.SaveAsync(revisionId, metadata, cancellationToken);
        await _catalogue.SavePhotoMetadataAsync(
            revisionId,
            metadata,
            inspectedAtUtc,
            cancellationToken);
        await _inspections.MarkAsync(
            revisionId,
            PhotoMetadataExtractionContract.CurrentVersion,
            inspectedAtUtc,
            cancellationToken);
        return metadata;
    }
}
