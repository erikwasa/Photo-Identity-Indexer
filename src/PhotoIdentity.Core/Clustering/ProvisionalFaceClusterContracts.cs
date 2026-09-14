using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Clustering;

/// <summary>
/// Contract for derived, exact-model provisional face clustering. A provisional
/// cluster is advisory evidence only and must never be treated as a canonical person.
/// </summary>
public sealed record ProvisionalFaceClusterPolicy(
    string Version,
    string Algorithm,
    int MinimumClusterSize,
    int MinimumSamples,
    double? DistanceThreshold = null,
    int? MutualNeighborCount = null,
    int? MinimumSharedNeighbors = null)
{
    public ProvisionalFaceClusterPolicy Validate()
    {
        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new ArgumentException("Cluster policy version is required.", nameof(Version));
        }

        if (string.IsNullOrWhiteSpace(Algorithm))
        {
            throw new ArgumentException("Cluster algorithm is required.", nameof(Algorithm));
        }

        if (MinimumClusterSize < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumClusterSize));
        }

        if (MinimumSamples < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSamples));
        }

        if (DistanceThreshold is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(DistanceThreshold));
        }

        if (MutualNeighborCount is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MutualNeighborCount));
        }

        if (MinimumSharedNeighbors is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumSharedNeighbors));
        }

        return this;
    }
}

public sealed record ProvisionalFaceClusterProvenance(
    ModelId EmbeddingModelId,
    Sha256Digest EmbeddingModelHash,
    string PolicyVersion,
    string Algorithm,
    DateTimeOffset GeneratedAtUtc);

public enum ProvisionalFaceClusterMemberRole
{
    Core,
    Border,
    Noise,
}

public sealed record ProvisionalFaceClusterMember(
    FaceOccurrenceId FaceOccurrenceId,
    ProvisionalFaceClusterMemberRole Role,
    double? MembershipStrength = null);

public sealed record ProvisionalFaceCluster(
    string DerivedClusterKey,
    ProvisionalFaceClusterProvenance Provenance,
    IReadOnlyList<ProvisionalFaceClusterMember> Members);

public static class ProvisionalFaceClusterSemantics
{
    public const string CanonicalDefinition =
        "Provisional face clusters are exact-model, policy-versioned, regenerable derived evidence. " +
        "Cluster membership never creates or rewrites a canonical Person, identity assignment, Unknown decision, rejection, or review-history entry.";
}

/// <summary>
/// One private evaluation row. PersonId is intentionally opaque and is replaced
/// with a local synthetic label by the export tool before writing a sample file.
/// ContentHash is used only to pseudonymize exact-content groups so duplicate source
/// copies cannot be mistaken for independent evidence during local evaluation.
/// </summary>
public sealed record ProvisionalClusterEvaluationFace(
    FaceOccurrenceId FaceOccurrenceId,
    AssetRevisionId AssetRevisionId,
    Sha256Digest ContentHash,
    string ReviewState,
    PersonId? PersonId,
    EmbeddingVector Embedding);