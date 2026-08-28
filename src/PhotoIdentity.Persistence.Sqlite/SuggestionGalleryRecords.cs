using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public static class CatalogueSuggestionGallerySorts
{
    public const string CreatedDescending = "created-desc";
    public const string SuggestedPerson = "suggested-person";
    public const string ConfidenceGroup = "confidence-group";
    public const string ScoreMarginDescending = "margin-desc";
    public const string ScoreMarginAscending = "margin-asc";
    public const string ScoreDescending = "score-desc";
    public const string NoSuggestionFirst = "no-suggestion-first";
}

public static class CatalogueSuggestionConfidenceFilters
{
    public const string All = "all";
    public const string High = IdentitySuggestionConfidenceGroups.High;
    public const string Medium = IdentitySuggestionConfidenceGroups.Medium;
    public const string Low = IdentitySuggestionConfidenceGroups.Low;
}

public sealed record CatalogueSuggestionGalleryTopSuggestion(
    long Id,
    CatalogueReviewPerson Person,
    ModelId ModelId,
    Sha256Digest ModelHash,
    int Rank,
    double Score,
    double? ScoreMargin,
    string Status,
    DateTimeOffset GeneratedAtUtc,
    string ConfidenceGroup = "");

public sealed record CatalogueSuggestionGalleryFace(
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
    CatalogueSuggestionGalleryTopSuggestion? TopSuggestion,
    AssetRevisionId RevisionId,
    string? BoundingBoxJson = null);

public sealed record CatalogueSuggestionGalleryPage(
    IReadOnlyList<CatalogueSuggestionGalleryFace> Items,
    int Offset,
    int Limit,
    int Total);
