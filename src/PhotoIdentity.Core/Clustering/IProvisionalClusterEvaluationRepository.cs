using PhotoIdentity.Core.Identifiers;
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

    /// <summary>
    /// Reads the exact-model confirmed reference population used by production identity matching.
    /// This includes both current reviewed assignments and legacy confirmed labels that have no
    /// review action, while excluding merged People just as the production scorer does.
    /// </summary>
    Task<IReadOnlyList<ProvisionalClusterEvaluationFace>> ReadConfirmedReferenceSampleAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int maximumFaces = 20000,
        CancellationToken cancellationToken = default);
}
