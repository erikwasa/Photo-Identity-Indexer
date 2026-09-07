using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

/// <summary>
/// Provider-neutral target scoring and derived ranking maintenance for one exact model revision.
/// </summary>
public interface IIdentityMatchRegenerationScorer
{
    Task<int> ScoreTargetAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default);

    Task RemoveObsoleteRankingsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        Guid runId,
        CancellationToken cancellationToken = default);
}

public sealed record ReviewIdentityAutoAssignmentSummary(
    int CandidateCount,
    int AssignedCount,
    int SkippedCount);

/// <summary>
/// Provider-neutral promotion of persisted High rank-one identity suggestions through the
/// canonical suggestion-acceptance boundary.
/// </summary>
public interface IIdentityAutoAssignmentService
{
    Task<ReviewIdentityAutoAssignmentSummary> ApplyAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        ReviewIdentitySuggestionPolicy policy,
        CancellationToken cancellationToken = default);
}
