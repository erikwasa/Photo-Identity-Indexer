namespace PhotoIdentity.Web.Contracts;

public sealed record AssignmentAuditFaceResponse(
    string Id,
    string ImageUrl,
    string PhotoName,
    int FaceOrdinal,
    double? Confidence,
    DateTimeOffset FaceCreatedAtUtc,
    DateTimeOffset AssignedAtUtc,
    long AssignmentActionId,
    string AssignmentActor,
    ReviewPersonResponse AssignedPerson,
    ReviewTopSuggestionResponse? AcceptedSuggestion,
    ReviewTopSuggestionResponse? CurrentTopSuggestion,
    bool CurrentSuggestionDisagrees);

public sealed record AssignmentAuditPageResponse(
    IReadOnlyList<AssignmentAuditFaceResponse> Items,
    int Offset,
    int Limit,
    int Total,
    string Source,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);
