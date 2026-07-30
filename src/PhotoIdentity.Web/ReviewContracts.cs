namespace PhotoIdentity.Web.Contracts;

public sealed record ReviewPersonResponse(string Id, string DisplayName);

public sealed record ReviewFaceResponse(
    string Id,
    string ImageUrl,
    string PhotoName,
    int FaceOrdinal,
    double? Confidence,
    string State,
    ReviewPersonResponse? Person,
    DateTimeOffset CreatedAtUtc);

public sealed record ReviewFacePageResponse(
    IReadOnlyList<ReviewFaceResponse> Items,
    int Offset,
    int Limit,
    int Total);

public sealed record ReviewFaceNavigationResponse(
    string? PreviousFaceId,
    string? NextFaceId,
    int Position,
    int Total,
    string Sort);

public sealed record ReviewProcessingRunFilterResponse(
    string Id,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int FaceCount);

public sealed record ReviewModelRevisionFilterResponse(
    string ModelId,
    string ModelHash,
    DateTimeOffset GeneratedAtUtc,
    int FaceCount);

public sealed record ReviewFilterOptionsResponse(
    IReadOnlyList<ReviewProcessingRunFilterResponse> ProcessingRuns,
    IReadOnlyList<ReviewModelRevisionFilterResponse> ModelRevisions);

public sealed record BulkReviewPreviewRequest(
    IReadOnlyList<string> FaceIds,
    string Action,
    string? PersonId = null);

public sealed record BulkReviewCommitRequest(
    IReadOnlyList<string> FaceIds,
    string Action,
    string? PersonId,
    int ExpectedAffectedCount,
    string PreviewToken,
    bool Confirm,
    string Actor,
    string? Note = null);

public sealed record BulkReviewPreviewResponse(
    string Action,
    int RequestedCount,
    int AffectedCount,
    int SkippedCount,
    string PreviewToken,
    ReviewPersonResponse? Person);

public sealed record BulkReviewCommitResponse(
    string Action,
    int RequestedCount,
    int AffectedCount,
    ReviewPersonResponse? Person,
    DateTimeOffset CreatedAtUtc);

public sealed record ReviewActionResponse(
    long Id,
    string Kind,
    ReviewPersonResponse? Person,
    string Actor,
    string? Note,
    DateTimeOffset CreatedAtUtc,
    bool Reversed,
    long? ReversesActionId);

public sealed record ReviewSuggestionActionResponse(
    long Id,
    string Kind,
    string Actor,
    string? Note,
    DateTimeOffset CreatedAtUtc,
    long? ReviewActionId);

public sealed record ReviewIdentitySuggestionResponse(
    long Id,
    ReviewPersonResponse Person,
    string ModelId,
    string ModelHash,
    int Rank,
    double Score,
    double? ScoreMargin,
    string Status,
    DateTimeOffset GeneratedAtUtc,
    ReviewSuggestionActionResponse? LatestAction);

public sealed record ReviewFaceDetailsResponse(
    ReviewFaceResponse Face,
    string MediaType,
    int? PhotoWidth,
    int? PhotoHeight,
    string RevisionHashPrefix,
    IReadOnlyList<ReviewActionResponse> Actions,
    ReviewFaceNavigationResponse? Navigation);

public sealed record PersonMaintenancePersonResponse(
    string Id,
    string DisplayName,
    int LabelCount,
    int SuggestionCount);

public sealed record PersonMaintenanceActionResponse(
    long Id,
    string Kind,
    string PersonId,
    string PreviousDisplayName,
    string? TargetPersonId,
    string NewDisplayName,
    string Actor,
    string? Note,
    DateTimeOffset CreatedAtUtc,
    bool Reversible);

public sealed record CreatePersonRequest(string DisplayName);

public sealed record RenamePersonRequest(string DisplayName, string Actor, string? Note = null);

public sealed record MergePersonRequest(
    string TargetPersonId,
    bool ConfirmIrreversible,
    string Actor,
    string? Note = null);

public sealed record AssignFaceRequest(string PersonId, string Actor, string? Note = null);

public sealed record ReviewFaceActionRequest(string Actor, string? Note = null);

public sealed record ReviewSuggestionActionRequest(string Actor, string? Note = null);