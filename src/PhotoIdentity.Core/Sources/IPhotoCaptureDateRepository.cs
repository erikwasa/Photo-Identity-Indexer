using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

/// <summary>
/// Revision-bound manual capture-date overrides. Extracted metadata remains independent source evidence.
/// </summary>
public interface IPhotoCaptureDateRepository
{
    Task<PhotoCaptureDateState> GetStateAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default);

    Task<PhotoCaptureDateState> SetManualAsync(
        AssetRevisionId revisionId,
        PhotoCaptureDateValue value,
        string actor,
        CancellationToken cancellationToken = default);

    Task<PhotoCaptureDateState> ClearManualAsync(
        AssetRevisionId revisionId,
        string actor,
        CancellationToken cancellationToken = default);
}
