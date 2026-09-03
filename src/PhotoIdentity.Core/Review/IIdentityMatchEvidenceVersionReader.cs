using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

/// <summary>
/// Provider-neutral snapshot of the monotonic identity-affecting evidence counters used
/// to detect stale match-regeneration work for one exact embedding-model revision.
/// </summary>
public sealed record ReviewIdentityMatchEvidenceVersion(
    long ReviewActionId,
    long SuggestionReviewActionId,
    long PersonMergeActionId,
    long EmbeddingId);

/// <summary>
/// Reads the identity-affecting evidence version for one exact embedding-model revision.
/// </summary>
public interface IIdentityMatchEvidenceVersionReader
{
    Task<ReviewIdentityMatchEvidenceVersion> ReadAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default);
}

public static class ReviewIdentityMatchEvidenceVersions
{
    public static ReviewIdentityMatchEvidenceVersion ExpectedAfterAutomaticAssignments(
        ReviewIdentityMatchEvidenceVersion before,
        int automaticallyAssignedCount)
    {
        ArgumentNullException.ThrowIfNull(before);
        if (automaticallyAssignedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(automaticallyAssignedCount));
        }

        return before with
        {
            ReviewActionId = checked(before.ReviewActionId + automaticallyAssignedCount),
            SuggestionReviewActionId = checked(before.SuggestionReviewActionId + automaticallyAssignedCount),
        };
    }
}
