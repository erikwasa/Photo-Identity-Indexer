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
    IReadOnlyList<ReviewActionResponse> Actions);

public sealed record CreatePersonRequest(string DisplayName);

public sealed record AssignFaceRequest(string PersonId, string Actor, string? Note = null);

public sealed record ReviewFaceActionRequest(string Actor, string? Note = null);

public sealed record ReviewSuggestionActionRequest(string Actor, string? Note = null);
