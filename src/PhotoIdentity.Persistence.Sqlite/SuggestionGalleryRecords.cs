using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public static class CatalogueSuggestionGallerySorts
{
    public const string CreatedDescending = "created-desc";
    public const string SuggestedPerson = "suggested-person";
    public const string ScoreMarginDescending = "margin-desc";
    public const string ScoreMarginAscending = "margin-asc";
    public const string ScoreDescending = "score-desc";
    public const string NoSuggestionFirst = "no-suggestion-first";
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
    DateTimeOffset GeneratedAtUtc);

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
    CatalogueSuggestionGalleryTopSuggestion? TopSuggestion);

public sealed record CatalogueSuggestionGalleryPage(
    IReadOnlyList<CatalogueSuggestionGalleryFace> Items,
    int Offset,
    int Limit,
    int Total);
