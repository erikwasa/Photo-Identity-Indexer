using PhotoIdentity.Core.Review;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class ReviewIdentityMultiEvidenceAutoAssignmentPolicyTests
{
    [Fact]
    public void Initial_policy_matches_accepted_WI_0117_candidate()
    {
        ReviewIdentityMultiEvidenceAutoAssignmentPolicy policy =
            ReviewIdentityMultiEvidenceAutoAssignmentPolicy.Initial;

        Assert.Equal("m25-multi-evidence-auto-v1", policy.Version);
        Assert.Equal(0.50, policy.MinimumTargetScore);
        Assert.Equal(0.05, policy.MinimumTargetMargin);
        Assert.Equal(2, policy.MinimumIndependentReferenceCount);
        Assert.Equal(0.50, policy.MinimumReferenceScore);
        Assert.True(policy.ClusterIncludeUnknown);
    }

    [Theory]
    [InlineData(0.50, 0.05, 2, true, 0.50, true)]
    [InlineData(0.49, 0.05, 2, true, 0.50, false)]
    [InlineData(0.50, 0.049, 2, true, 0.50, false)]
    [InlineData(0.50, 0.05, 1, true, 0.50, false)]
    [InlineData(0.50, 0.05, 2, false, 0.50, false)]
    [InlineData(0.59, 0.05, 2, true, 0.60, false)]
    [InlineData(0.60, 0.05, 2, true, 0.60, true)]
    public void Qualifies_requires_every_independent_evidence_gate(
        double targetScore,
        double targetMargin,
        int referenceSupportCount,
        bool strongClusterAgreement,
        double configuredMediumThreshold,
        bool expected)
    {
        bool actual = ReviewIdentityMultiEvidenceAutoAssignmentPolicy.Initial.Qualifies(
            targetScore,
            targetMargin,
            referenceSupportCount,
            strongClusterAgreement,
            configuredMediumThreshold);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Qualifies_requires_a_rank_gap()
    {
        Assert.False(ReviewIdentityMultiEvidenceAutoAssignmentPolicy.Initial.Qualifies(
            targetScore: 0.70,
            targetMargin: null,
            independentReferenceSupportCount: 10,
            strongClusterAgreement: true,
            configuredMediumScoreThreshold: 0.50));
    }
}
