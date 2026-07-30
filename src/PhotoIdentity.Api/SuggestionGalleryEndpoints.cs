using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class SuggestionGalleryEndpoints
{
    public static IEndpointRouteBuilder MapSuggestionGalleryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/review/suggestion-faces");
        group.MapGet("", GetFacesAsync);
        group.MapGet("/{id}", GetFaceAsync);
        return endpoints;
    }

    private static async Task<IResult> GetFacesAsync(
        SqliteSuggestionGalleryRepository repository,
        string? modelId,
        string? modelHash,
        int offset = 0,
        int limit = 40,
        string state = CatalogueReviewStates.Unreviewed,
        string? processingRunId = null,
        string sort = CatalogueSuggestionGallerySorts.CreatedDescending,
        CancellationToken cancellationToken = default)
    {
        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash) ||
            !TryProcessingRunId(processingRunId, out ProcessingRunId? parsedRunId))
        {
            return BadRequest("An exact suggestion model revision and a valid processing run are required.");
        }

        try
        {
            CatalogueSuggestionGalleryPage page = await repository.GetFacesAsync(
                parsedModelId,
                parsedModelHash,
                offset,
                limit,
                state,
                parsedRunId,
                sort,
                cancellationToken);
            return Results.Ok(new ReviewFacePageResponse(
                page.Items.Select(ToResponse).ToArray(),
                page.Offset,
                page.Limit,
                page.Total));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> GetFaceAsync(
        string id,
        SqliteReviewRepository reviewRepository,
        SqliteSuggestionGalleryRepository suggestionRepository,
        string? modelId,
        string? modelHash,
        string state = CatalogueReviewStates.Unreviewed,
        string? processingRunId = null,
        string sort = CatalogueSuggestionGallerySorts.CreatedDescending,
        CancellationToken cancellationToken = default)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId) ||
            !TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash) ||
            !TryProcessingRunId(processingRunId, out ProcessingRunId? parsedRunId))
        {
            return BadRequest("The face, exact suggestion model revision, or processing run is invalid.");
        }

        CatalogueReviewFace? face = await reviewRepository.GetFaceAsync(faceOccurrenceId, cancellationToken);
        if (face is null)
        {
            return Results.NotFound();
        }

        try
        {
            IReadOnlyList<CatalogueReviewAction> actions = await reviewRepository.GetActionsAsync(
                faceOccurrenceId,
                cancellationToken);
            CatalogueReviewFaceNavigation? navigation = await suggestionRepository.GetNavigationAsync(
                faceOccurrenceId,
                parsedModelId,
                parsedModelHash,
                state,
                parsedRunId,
                sort,
                cancellationToken);
            return Results.Ok(new ReviewFaceDetailsResponse(
                ToResponse(face),
                face.MediaType,
                face.PhotoWidth,
                face.PhotoHeight,
                face.RevisionHash.ToString()[..12],
                actions.Select(ToResponse).ToArray(),
                navigation is null
                    ? null
                    : new ReviewFaceNavigationResponse(
                        navigation.PreviousFaceId?.ToString(),
                        navigation.NextFaceId?.ToString(),
                        navigation.Position,
                        navigation.Total,
                        navigation.Sort)));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static ReviewFaceResponse ToResponse(CatalogueSuggestionGalleryFace face) => new(
        face.Id.ToString(),
        $"/api/review/faces/{face.Id}/image",
        face.PhotoName,
        face.Ordinal,
        face.Confidence,
        face.State,
        face.Person is null ? null : ToResponse(face.Person),
        face.CreatedAtUtc,
        face.TopSuggestion is null ? null : ToResponse(face.TopSuggestion));

    private static ReviewFaceResponse ToResponse(CatalogueReviewFace face) => new(
        face.Id.ToString(),
        $"/api/review/faces/{face.Id}/image",
        face.PhotoName,
        face.Ordinal,
        face.Confidence,
        face.State,
        face.Person is null ? null : ToResponse(face.Person),
        face.CreatedAtUtc);

    private static ReviewTopSuggestionResponse ToResponse(
        CatalogueSuggestionGalleryTopSuggestion suggestion) => new(
        suggestion.Id,
        ToResponse(suggestion.Person),
        suggestion.ModelId.ToString(),
        suggestion.ModelHash.ToString(),
        suggestion.Rank,
        suggestion.Score,
        suggestion.ScoreMargin,
        suggestion.Status,
        suggestion.GeneratedAtUtc);

    private static ReviewPersonResponse ToResponse(CatalogueReviewPerson person) =>
        new(person.Id.ToString(), person.DisplayName);

    private static ReviewActionResponse ToResponse(CatalogueReviewAction action) => new(
        action.Id,
        action.Kind,
        action.PersonId is PersonId personId && action.PersonDisplayName is string displayName
            ? new ReviewPersonResponse(personId.ToString(), displayName)
            : null,
        action.Actor,
        action.Note,
        action.CreatedAtUtc,
        action.ReversedAtUtc is not null,
        action.ReversesActionId);

    private static bool TryFaceOccurrenceId(string value, out FaceOccurrenceId id)
    {
        id = default;
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        id = FaceOccurrenceId.From(parsed);
        return true;
    }

    private static bool TryProcessingRunId(string? value, out ProcessingRunId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        id = ProcessingRunId.From(parsed);
        return true;
    }

    private static bool TryModelRevision(
        string? modelId,
        string? modelHash,
        out ModelId parsedModelId,
        out Sha256Digest parsedModelHash)
    {
        parsedModelId = default;
        parsedModelHash = default;
        if (string.IsNullOrWhiteSpace(modelId) || string.IsNullOrWhiteSpace(modelHash))
        {
            return false;
        }

        try
        {
            parsedModelId = new ModelId(modelId);
            parsedModelHash = new Sha256Digest(modelHash);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });
}
