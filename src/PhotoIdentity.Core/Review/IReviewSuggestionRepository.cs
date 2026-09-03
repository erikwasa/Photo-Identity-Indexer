using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

public static class ReviewSuggestionStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
}

public static class ReviewSuggestionActionKinds
{
    public const string Accept = "accept";
    public const string Reject = "reject";
}

public sealed record ReviewSuggestionAction(
    long Id,
    string Kind,
    string Actor,
    string? Note,
    DateTimeOffset CreatedAtUtc,
    long? ReviewActionId);

public sealed record ReviewIdentitySuggestion(
    long Id,
    ReviewPerson Person,
    ModelId ModelId,
    Sha256Digest ModelHash,
    int Rank,
    double Score,
    double? ScoreMargin,
    string Status,
    DateTimeOffset GeneratedAtUtc,
    ReviewSuggestionAction? LatestAction);

/// <summary>
/// Provider-neutral ranked identity suggestions and explicit human accept/reject decisions.
/// </summary>
public interface IReviewSuggestionRepository
{
    Task<IReadOnlyList<ReviewIdentitySuggestion>> GetSuggestionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default);

    Task<ReviewIdentitySuggestion> AcceptAsync(
        FaceOccurrenceId faceOccurrenceId,
        long suggestionId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<ReviewIdentitySuggestion> RejectAsync(
        FaceOccurrenceId faceOccurrenceId,
        long suggestionId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);
}
