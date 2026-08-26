using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public static class CatalogueReviewStates
{
    public const string Unreviewed = "unreviewed";
    public const string Assigned = "assigned";
    public const string Unknown = "unknown";
    public const string Rejected = "rejected";
}

public static class CatalogueReviewActionKinds
{
    public const string Assign = "assign";
    public const string Unknown = "unknown";
    public const string Reject = "reject";
    public const string Undo = "undo";
}

public static class CatalogueReviewSorts
{
    public const string CreatedDescending = "created-desc";
}

public sealed record CatalogueReviewPerson(PersonId Id, string DisplayName);

public sealed record CatalogueReviewAction(
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

public sealed record CatalogueReviewFace(
    FaceOccurrenceId Id,
    int Ordinal,
    DateTimeOffset CreatedAtUtc,
    string PhotoName,
    string MediaType,
    int? PhotoWidth,
    int? PhotoHeight,
    Sha256Digest RevisionHash,
    string? CropStoragePath,
    double? Confidence,
    string State,
    CatalogueReviewPerson? Person,
    long? ActiveActionId,
    string? BoundingBoxJson = null);

public sealed record CatalogueReviewFacePage(
    IReadOnlyList<CatalogueReviewFace> Items,
    int Offset,
    int Limit,
    int Total);

public sealed record CatalogueReviewFaceNavigation(
    FaceOccurrenceId? PreviousFaceId,
    FaceOccurrenceId? NextFaceId,
    int Position,
    int Total,
    string Sort);