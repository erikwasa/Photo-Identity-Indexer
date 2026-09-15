using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

public sealed record ReviewIdentityMultiEvidenceAutoAssignmentPolicy(
    string Version,
    double MinimumTargetScore,
    double MinimumTargetMargin,
    int MinimumIndependentReferenceCount,
    double MinimumReferenceScore,
    string ClusterPolicyVersion,
    bool ClusterIncludeUnknown)
{
    public static ReviewIdentityMultiEvidenceAutoAssignmentPolicy Initial { get; } =
        new ReviewIdentityMultiEvidenceAutoAssignmentPolicy(
            Version: "m25-multi-evidence-auto-v1",
            MinimumTargetScore: 0.50,
            MinimumTargetMargin: 0.05,
            MinimumIndependentReferenceCount: 2,
            MinimumReferenceScore: 0.50,
            ClusterPolicyVersion: ProvisionalFaceClusterPolicies.InitialDbscan.Version,
            ClusterIncludeUnknown: true).Validate();

    public ReviewIdentityMultiEvidenceAutoAssignmentPolicy Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
        ValidateScore(MinimumTargetScore, nameof(MinimumTargetScore));
        if (!double.IsFinite(MinimumTargetMargin)
            || MinimumTargetMargin < 0
            || MinimumTargetMargin > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumTargetMargin));
        }

        if (MinimumIndependentReferenceCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumIndependentReferenceCount));
        }

        ValidateScore(MinimumReferenceScore, nameof(MinimumReferenceScore));
        ArgumentException.ThrowIfNullOrWhiteSpace(ClusterPolicyVersion);
        return this;
    }

    public bool Qualifies(
        double targetScore,
        double? targetMargin,
        int independentReferenceSupportCount,
        bool strongClusterAgreement,
        double configuredMediumScoreThreshold)
    {
        ValidateScore(configuredMediumScoreThreshold, nameof(configuredMediumScoreThreshold));
        if (!double.IsFinite(targetScore))
        {
            throw new ArgumentOutOfRangeException(nameof(targetScore));
        }

        if (targetMargin is double margin && (!double.IsFinite(margin) || margin < 0 || margin > 2))
        {
            throw new ArgumentOutOfRangeException(nameof(targetMargin));
        }

        double effectiveTargetScore = Math.Max(MinimumTargetScore, configuredMediumScoreThreshold);
        return targetScore >= effectiveTargetScore
            && targetMargin is double candidateMargin
            && candidateMargin >= MinimumTargetMargin
            && independentReferenceSupportCount >= MinimumIndependentReferenceCount
            && strongClusterAgreement;
    }

    private static void ValidateScore(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Score threshold must be between 0 and 1.");
        }
    }
}

public sealed record ReviewIdentityMultiEvidenceAutoAssignmentConfiguration(
    int Version,
    bool Enabled,
    string AlgorithmPolicyVersion,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc)
{
    public void Validate()
    {
        if (Version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Version));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(AlgorithmPolicyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(UpdatedBy);
    }
}

public interface IIdentityMultiEvidenceAutoAssignmentPolicyRepository
{
    Task<ReviewIdentityMultiEvidenceAutoAssignmentConfiguration> GetAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default);

    Task<ReviewIdentityMultiEvidenceAutoAssignmentConfiguration> UpdateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        bool enabled,
        string actor,
        CancellationToken cancellationToken = default);
}
