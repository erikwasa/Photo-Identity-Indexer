using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Clustering;

public interface IProvisionalClusterEvaluationRepository
{
    Task<IReadOnlyList<ProvisionalClusterEvaluationFace>> ReadReviewedSampleAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int maximumFaces = 5000,
        bool includeUnknown = true,
        CancellationToken cancellationToken = default);
}
