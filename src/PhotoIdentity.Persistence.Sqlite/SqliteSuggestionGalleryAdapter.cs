using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed class SqliteSuggestionGalleryAdapter : ISuggestionGalleryRepository
{
    private readonly SqliteSuggestionGalleryRepository _repository;

    public SqliteSuggestionGalleryAdapter(SqliteSuggestionGalleryRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<ReviewSuggestionGalleryPage> GetFacesAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int offset,
        int limit,
        string state,
        ProcessingRunId? processingRunId,
        string sort,
        string confidenceGroup,
        PersonId? suggestedPersonId,
        CancellationToken cancellationToken = default)
    {
        CatalogueSuggestionGalleryPage page = await _repository.GetFacesAsync(
            modelId,
            modelHash,
            offset,
            limit,
            state,
            processingRunId,
            sort,
            confidenceGroup,
            suggestedPersonId,
            cancellationToken);

        return new ReviewSuggestionGalleryPage(
            page.Items.Select(Map).ToArray(),
            page.Offset,
            page.Limit,
            page.Total);
    }

    public async Task<ReviewSuggestionGalleryNavigation?> GetNavigationAsync(
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash,
        string state,
        ProcessingRunId? processingRunId,
        string sort,
        string confidenceGroup,
        PersonId? suggestedPersonId,
        CancellationToken cancellationToken = default)
    {
        CatalogueReviewFaceNavigation? navigation = await _repository.GetNavigationAsync(
            faceOccurrenceId,
            modelId,
            modelHash,
            state,
            processingRunId,
            sort,
            confidenceGroup,
            suggestedPersonId,
            cancellationToken);

        return navigation is null
            ? null
            : new ReviewSuggestionGalleryNavigation(
                navigation.PreviousFaceId,
                navigation.NextFaceId,
                navigation.Position,
                navigation.Total,
                navigation.Sort);
    }

    private static ReviewSuggestionGalleryFace Map(CatalogueSuggestionGalleryFace face) => new(
        face.Id,
        face.Ordinal,
        face.CreatedAtUtc,
        face.PhotoName,
        face.MediaType,
        face.PhotoWidth,
        face.PhotoHeight,
        face.RevisionHash,
        face.CropStoragePath,
        face.Confidence,
        face.State,
        face.Person is null
            ? null
            : new ReviewSuggestionGalleryPerson(face.Person.Id, face.Person.DisplayName),
        face.TopSuggestion is null
            ? null
            : new ReviewSuggestionGalleryTopSuggestion(
                face.TopSuggestion.Id,
                new ReviewSuggestionGalleryPerson(
                    face.TopSuggestion.Person.Id,
                    face.TopSuggestion.Person.DisplayName),
                face.TopSuggestion.ModelId,
                face.TopSuggestion.ModelHash,
                face.TopSuggestion.Rank,
                face.TopSuggestion.Score,
                face.TopSuggestion.ScoreMargin,
                face.TopSuggestion.Status,
                face.TopSuggestion.GeneratedAtUtc,
                face.TopSuggestion.ConfidenceGroup),
        face.RevisionId,
        face.BoundingBoxJson);
}
