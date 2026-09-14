using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class ProvisionalFaceClusterKnownPersonAdvisoryTests
{
    private static readonly ModelId ModelId = new("sface-test");
    private static readonly Sha256Digest ModelHash = new(new string('a', 64));
    private static readonly PersonId Alice = PersonId.From(Guid.Parse("00000000-0000-0000-0000-000000000101"));
    private static readonly PersonId Bob = PersonId.From(Guid.Parse("00000000-0000-0000-0000-000000000102"));

    [Fact]
    public void Multiple_independent_medium_votes_can_create_strong_advisory_support()
    {
        ProvisionalFaceClusterKnownPersonAdvisory result = Evaluate(
            memberCount: 4,
            coreCount: 4,
            internalConflictCount: 0,
            Evidence(1, Alice, "Alice", 0.66, 0.08),
            Evidence(2, Alice, "Alice", 0.64, 0.06),
            Evidence(3, Alice, "Alice", 0.61, 0.04),
            Evidence(4, Bob, "Bob", 0.55, 0.02));

        Assert.Equal(ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Strong, result.Status);
        Assert.NotNull(result.Candidate);
        Assert.Equal(Alice, result.Candidate!.PersonId);
        Assert.Equal(3, result.Candidate.SupportCount);
        Assert.Equal(0.75, result.Candidate.SupportShare, 6);
        Assert.Equal(0, result.Candidate.OrdinaryHighCount);
        Assert.Equal(3, result.Candidate.OrdinaryMediumCount);
        Assert.False(result.CanonicalAssignmentAllowed);
    }

    [Fact]
    public void One_strong_member_never_causes_cluster_identity_inheritance()
    {
        ProvisionalFaceClusterKnownPersonAdvisory result = Evaluate(
            memberCount: 4,
            coreCount: 4,
            internalConflictCount: 0,
            Evidence(1, Alice, "Alice", 0.95, 0.50),
            Evidence(2, Bob, "Bob", 0.20, 0.01),
            Evidence(3, Bob, "Bob", 0.18, 0.01),
            Evidence(4, Bob, "Bob", 0.15, 0.01));

        Assert.Equal(ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Insufficient, result.Status);
        Assert.Equal(Alice, result.Candidate!.PersonId);
        Assert.Equal(1, result.Candidate.SupportCount);
    }

    [Fact]
    public void Mixed_cluster_with_competing_qualifying_votes_fails_closed()
    {
        ProvisionalFaceClusterKnownPersonAdvisory result = Evaluate(
            memberCount: 5,
            coreCount: 5,
            internalConflictCount: 0,
            Evidence(1, Alice, "Alice", 0.68, 0.08),
            Evidence(2, Alice, "Alice", 0.65, 0.06),
            Evidence(3, Alice, "Alice", 0.63, 0.05),
            Evidence(4, Bob, "Bob", 0.62, 0.05),
            Evidence(5, Bob, "Bob", 0.60, 0.04));

        Assert.Equal(ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Ambiguous, result.Status);
        Assert.Equal(Alice, result.Candidate!.PersonId);
        Assert.Equal(Bob, result.CompetingCandidate!.PersonId);
        Assert.Contains("Competing support", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_internal_cluster_conflict_prevents_strong_advisory_classification()
    {
        ProvisionalFaceClusterKnownPersonAdvisory result = Evaluate(
            memberCount: 3,
            coreCount: 3,
            internalConflictCount: 1,
            Evidence(1, Alice, "Alice", 0.66, 0.08),
            Evidence(2, Alice, "Alice", 0.64, 0.06),
            Evidence(3, Alice, "Alice", 0.61, 0.04));

        Assert.Equal(ProvisionalFaceClusterKnownPersonAdvisoryStatuses.Ambiguous, result.Status);
        Assert.Equal(1, result.InternalConflictCount);
        Assert.Contains("not-same", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    private static ProvisionalFaceClusterKnownPersonAdvisory Evaluate(
        int memberCount,
        int coreCount,
        int internalConflictCount,
        params ProvisionalFaceClusterKnownPersonMemberEvidence[] evidence) =>
        ProvisionalFaceClusterKnownPersonAdvisoryPolicy.Initial.Evaluate(
            Guid.Parse("00000000-0000-0000-0000-000000000201"),
            ModelId,
            ModelHash,
            "m25-dbscan-v1",
            includeUnknown: false,
            "cluster-001",
            memberCount,
            coreCount,
            internalConflictCount,
            IdentityPolicy(),
            evidence,
            new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));

    private static ProvisionalFaceClusterKnownPersonMemberEvidence Evidence(
        int face,
        PersonId person,
        string displayName,
        double score,
        double? margin) => new(
            FaceOccurrenceId.From(Guid.Parse($"00000000-0000-0000-0000-{face:000000000000}")),
            person,
            displayName,
            score,
            margin);

    private static ReviewIdentitySuggestionPolicy IdentityPolicy() => new(
        Version: 7,
        AutoAssignEnabled: true,
        HighScoreThreshold: 0.70,
        HighMarginThreshold: 0.10,
        MediumScoreThreshold: 0.50,
        UpdatedBy: "test",
        UpdatedAtUtc: DateTimeOffset.UnixEpoch);
}
