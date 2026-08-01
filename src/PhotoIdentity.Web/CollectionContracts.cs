namespace PhotoIdentity.Web.Contracts;

public sealed record CollectionPersonMatchResponse(
    string Id,
    string DisplayName,
    int ConfirmedFaceCount,
    int SuggestedFaceCount,
    double? MaximumSuggestionScore);

public sealed record CollectionPhotoResponse(
    string RevisionId,
    string AssetId,
    DateTimeOffset ObservedAtUtc,
    string? MediaType,
    int? Width,
    int? Height,
    IReadOnlyList<CollectionPersonMatchResponse> People);

public sealed record CollectionSuggestionPolicyResponse(
    string ModelId,
    string ModelHash,
    double MinimumScore);

public sealed record CollectionQueryResponse(
    IReadOnlyList<string> PersonIds,
    string MatchMode,
    string ReviewState,
    bool ConfirmedOnly,
    CollectionSuggestionPolicyResponse? SuggestionPolicy,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    double? MinimumConfidence);

public sealed record CollectionPhotoPageResponse(
    IReadOnlyList<CollectionPhotoResponse> Items,
    int Offset,
    int Limit,
    int Total,
    CollectionQueryResponse Query);
