using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

public sealed record PhotoListCollectionCaptureTime(
    AssetRevisionId RevisionId,
    DateTime? EffectiveTakenAtLocal);

public interface IPhotoListCollectionCaptureTimeRepository
{
    Task<IReadOnlyList<PhotoListCollectionCaptureTime>> GetCaptureTimesAsync(
        IReadOnlyList<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default);
}
