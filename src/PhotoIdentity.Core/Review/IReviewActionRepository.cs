using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Review;

public static class ReviewActionKinds
{
    public const string Assign = "assign";
    public const string Unknown = "unknown";
    public const string Reject = "reject";
    public const string Undo = "undo";
}

public sealed record ReviewPerson(
    PersonId Id,
    string DisplayName);

public sealed record ReviewAction(
    long Id,
    FaceOccurrenceId FaceOccurrenceId,
    string Kind,
    PersonId? PersonId,
    string? PersonDisplayName,
    long? PersonLabelId,
    string Actor,
    string? Note,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReversedAtUtc,
    long? ReversesActionId);

/// <summary>
/// Provider-neutral canonical human review history for one face occurrence.
/// </summary>
public interface IReviewActionRepository
{
    Task<ReviewPerson> CreatePersonAsync(
        string displayName,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default);

    Task<ReviewAction> AssignAsync(
        FaceOccurrenceId faceOccurrenceId,
        PersonId personId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<ReviewAction> MarkUnknownAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<ReviewAction> RejectAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<ReviewAction?> UndoLatestAsync(
        FaceOccurrenceId faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewAction>> GetActionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default);
}
