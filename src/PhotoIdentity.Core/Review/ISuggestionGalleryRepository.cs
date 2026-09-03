using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

public sealed record ReviewSuggestionGalleryPerson(
    PersonId Id,
    string DisplayName);

public sealed record ReviewSuggestionGalleryTopSuggestion(
    long Id,
    ReviewSuggestionGalleryPerson Person,
    ModelId ModelId,
    Sha256Digest ModelHash,
    int Rank,
    double Score,
    double? ScoreMargin,
    string Status,
    DateTimeOffset GeneratedAtUtc,
    string ConfidenceGroup = "");

public sealed record ReviewSuggestionGalleryFace(
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
    ReviewSuggestionGalleryPerson? Person,
    ReviewSuggestionGalleryTopSuggestion? TopSuggestion,
    AssetRevisionId RevisionId,
    string? BoundingBoxJson = null);

public sealed record ReviewSuggestionGalleryPage(
    IReadOnlyList<ReviewSuggestionGalleryFace> Items,
    int Offset,
    int Limit,
    int Total);

public sealed record ReviewSuggestionGalleryNavigation(
    FaceOccurrenceId? PreviousFaceId,
    FaceOccurrenceId? NextFaceId,
    int Position,
    int Total,
    string Sort);

public interface ISuggestionGalleryRepository
{
    Task<ReviewSuggestionGalleryPage> GetFacesAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int offset,
        int limit,
        string state,
        ProcessingRunId? processingRunId,
        string sort,
        string confidenceGroup,
        PersonId? suggestedPersonId,
        CancellationToken cancellationToken = default);

    Task<ReviewSuggestionGalleryNavigation?> GetNavigationAsync(
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash,
        string state,
        ProcessingRunId? processingRunId,
        string sort,
        string confidenceGroup,
        PersonId? suggestedPersonId,
        CancellationToken cancellationToken = default);
}
