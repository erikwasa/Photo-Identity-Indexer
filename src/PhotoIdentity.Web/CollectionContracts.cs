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
    string ThumbnailUrl,
    string PreviewUrl,
    string OriginalUrl,
    DateTimeOffset ObservedAtUtc,
    string? MediaType,
    int? Width,
    int? Height,
    IReadOnlyList<CollectionPersonMatchResponse> People)
{
    // Compatibility alias for the pre-v2 collection page contract.
    public string ContentUrl => ThumbnailUrl;
}

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

public sealed record CollectionManifestPhotoResponse(
    string RevisionId,
    string AssetId,
    string ThumbnailUrl,
    string PreviewUrl,
    string OriginalUrl,
    string? MediaType,
    int? Width,
    int? Height,
    IReadOnlyList<CollectionPersonMatchResponse> People)
{
    // Manifest v1 named the authoritative original URL "contentUrl".
    public string ContentUrl => OriginalUrl;
}

public sealed record CollectionManifestResponse(
    string Format,
    int Version,
    int Total,
    CollectionQueryResponse Query,
    IReadOnlyList<CollectionManifestPhotoResponse> Photos);
