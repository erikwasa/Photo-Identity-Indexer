namespace PhotoIdentity.Web.Contracts;

public sealed record CollectionPersonMatchResponse(
    string Id,
    string DisplayName,
    int ConfirmedFaceCount);

public sealed record CollectionPhotoResponse(
    string RevisionId,
    string AssetId,
    DateTimeOffset ObservedAtUtc,
    string? MediaType,
    int? Width,
    int? Height,
    IReadOnlyList<CollectionPersonMatchResponse> People);

public sealed record CollectionQueryResponse(
    IReadOnlyList<string> PersonIds,
    string MatchMode,
    bool ConfirmedOnly,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    double? MinimumConfidence);

public sealed record CollectionPhotoPageResponse(
    IReadOnlyList<CollectionPhotoResponse> Items,
    int Offset,
    int Limit,
    int Total,
    CollectionQueryResponse Query);
