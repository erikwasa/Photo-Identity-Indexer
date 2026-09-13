namespace PhotoIdentity.Web.Contracts;

public sealed record ReviewSuggestedPersonGroupResponse(
    ReviewPersonResponse Person,
    int PendingCount,
    int HighCount,
    int MediumCount,
    int LowCount,
    double StrongestScore,
    double? StrongestMargin,
    bool IsFavorite,
    IReadOnlyList<string> RepresentativeFaceIds,
    IReadOnlyList<string> RepresentativeImageUrls);

public sealed record ReviewSuggestedPersonGroupPageResponse(
    IReadOnlyList<ReviewSuggestedPersonGroupResponse> Items,
    int Offset,
    int Limit,
    int Total);
