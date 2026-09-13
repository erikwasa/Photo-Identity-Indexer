using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

public sealed record ReviewSuggestedPersonGroup(
    ReviewSuggestionGalleryPerson Person,
    int PendingCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    double StrongestScore,
    double? StrongestMargin,
    bool IsFavorite,
    IReadOnlyList<FaceOccurrenceId> RepresentativeFaceIds);

public sealed record ReviewSuggestedPersonGroupPage(
    IReadOnlyList<ReviewSuggestedPersonGroup> Items,
    int Offset,
    int Limit,
    int Total);

/// <summary>
/// Returns bounded groups of currently unreviewed faces whose exact-model rank-one identity
/// suggestion is still pending. Group membership is advisory review evidence only; it never
/// changes canonical identity state by itself.
/// </summary>
public interface ISuggestedPersonGroupRepository
{
    Task<ReviewSuggestedPersonGroupPage> GetGroupsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);
}
