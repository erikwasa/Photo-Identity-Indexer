using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

public static class BulkReviewActionKinds
{
    public const string Assign = "assign";
    public const string Unknown = "unknown";
    public const string Reject = "reject";
}

public static class BulkReviewLimits
{
    public const int MaximumFacesPerRequest = 200;
    public const int MaximumSuggestionsPerRequest = 200;
}

public sealed record BulkReviewPreview(
    string Action,
    int RequestedCount,
    int AffectedCount,
    int SkippedCount,
    string PreviewToken,
    ReviewPerson? Person);

public sealed record BulkReviewResult(
    string Action,
    int RequestedCount,
    int AffectedCount,
    ReviewPerson? Person,
    DateTimeOffset CreatedAtUtc);

public interface IBulkReviewRepository
{
    Task<BulkReviewPreview> PreviewAsync(
        IReadOnlyCollection<FaceOccurrenceId> faceOccurrenceIds,
        string action,
        PersonId? personId,
        CancellationToken cancellationToken = default);

    Task<BulkReviewResult> CommitAsync(
        IReadOnlyCollection<FaceOccurrenceId> faceOccurrenceIds,
        string action,
        PersonId? personId,
        int expectedAffectedCount,
        string previewToken,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);
}

public sealed record BulkSuggestionPreview(
    int RequestedCount,
    int AffectedCount,
    int SkippedCount,
    string PreviewToken,
    ReviewPerson Person,
    ModelId ModelId,
    Sha256Digest ModelHash);

public sealed record BulkSuggestionResult(
    int RequestedCount,
    int AffectedCount,
    ReviewPerson Person,
    ModelId ModelId,
    Sha256Digest ModelHash,
    DateTimeOffset CreatedAtUtc);

public interface IBulkSuggestionReviewRepository
{
    Task<BulkSuggestionPreview> PreviewAsync(
        IReadOnlyCollection<long> suggestionIds,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default);

    Task<BulkSuggestionResult> CommitAsync(
        IReadOnlyCollection<long> suggestionIds,
        ModelId modelId,
        Sha256Digest modelHash,
        int expectedAffectedCount,
        string previewToken,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);
}
