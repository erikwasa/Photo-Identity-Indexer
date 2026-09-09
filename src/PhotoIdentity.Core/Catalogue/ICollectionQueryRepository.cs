using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.People;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Catalogue;

public static class CollectionMatchModes
{
    public const string Any = "any";
    public const string All = "all";
}

public static class CollectionReviewStates
{
    public const string Assigned = "assigned";
    public const string Unreviewed = "unreviewed";
    public const string All = "all";
}

public sealed record CollectionSuggestionPolicy(
    ModelId ModelId,
    Sha256Digest ModelHash,
    double MinimumScore);

public sealed record CollectionPersonMatch(
    PersonId PersonId,
    string DisplayName,
    int ConfirmedFaceCount,
    int SuggestedFaceCount,
    double? MaximumSuggestionScore);

public sealed record CollectionPhoto(
    AssetRevisionId RevisionId,
    AssetId AssetId,
    DateTimeOffset ObservedAtUtc,
    string? MediaType,
    int? Width,
    int? Height,
    IReadOnlyList<CollectionPersonMatch> People);

public sealed record CollectionPhotoPage(
    IReadOnlyList<CollectionPhoto> Items,
    int Offset,
    int Limit,
    int Total,
    string MatchMode,
    string ReviewState,
    CollectionSuggestionPolicy? SuggestionPolicy);

public interface ICollectionQueryRepository
{
    Task<CollectionPhotoPage> QueryPhotosAsync(
        IReadOnlyCollection<PersonId> personIds,
        string matchMode = CollectionMatchModes.All,
        CollectionSuggestionPolicy? suggestionPolicy = null,
        string? reviewState = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        double? minimumConfidence = null,
        int offset = 0,
        int limit = 40,
        CancellationToken cancellationToken = default);
}
