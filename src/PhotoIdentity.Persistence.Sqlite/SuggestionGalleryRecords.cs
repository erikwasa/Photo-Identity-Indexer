using PhotoIdentity.Core.Review;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

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
