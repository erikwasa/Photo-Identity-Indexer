using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Clustering;

public sealed record ProvisionalFaceNotSameConstraint(
    FaceOccurrenceId LeftFaceOccurrenceId,
    FaceOccurrenceId RightFaceOccurrenceId)
{
    public static ProvisionalFaceNotSameConstraint Create(
        FaceOccurrenceId first,
        FaceOccurrenceId second)
    {
        if (first == second)
        {
            throw new ArgumentException("A face cannot be constrained against itself.", nameof(second));
        }

        return first.Value.CompareTo(second.Value) < 0
            ? new(first, second)
            : new(second, first);
    }
}

public sealed record ProvisionalFaceClusterReviewGroup(
    Guid RunId,
    string DerivedClusterKey,
    int MemberCount,
    int CoreCount,
    int BorderCount,
    IReadOnlyList<FaceOccurrenceId> RepresentativeFaceIds)
{
    public double CoreShare => MemberCount == 0 ? 0 : (double)CoreCount / MemberCount;
}

public sealed record ProvisionalFaceClusterReviewGroupPage(
    IReadOnlyList<ProvisionalFaceClusterReviewGroup> Items,
    int Offset,
    int Limit,
    int Total);

public sealed record ProvisionalFaceClusterReviewMember(
    Guid RunId,
    string DerivedClusterKey,
    FaceOccurrenceId FaceOccurrenceId,
    ProvisionalFaceClusterMemberRole Role);

public interface IProvisionalFaceClusterConstraintSource
{
    Task<IReadOnlyList<ProvisionalFaceNotSameConstraint>> ListNotSameConstraintsAsync(
        IReadOnlyCollection<FaceOccurrenceId> eligibleFaceOccurrenceIds,
        int maximumConstraints = ProvisionalFaceClusterPolicies.MaximumNotSameConstraintsPerRun,
        CancellationToken cancellationToken = default);
}

public interface IProvisionalFaceClusterReviewRepository : IProvisionalFaceClusterConstraintSource
{
    Task<ProvisionalFaceClusterReviewGroupPage> ListCurrentGroupsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProvisionalFaceClusterReviewMember>> ListCurrentGroupMembersAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        string derivedClusterKey,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<int> RecordNotSameAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        string derivedClusterKey,
        FaceOccurrenceId anchorFaceOccurrenceId,
        IReadOnlyCollection<FaceOccurrenceId> otherFaceOccurrenceIds,
        string actor,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default);
}
