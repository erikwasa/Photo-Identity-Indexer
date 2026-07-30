using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class PersonAuditEndpoints
{
    public static IEndpointRouteBuilder MapPersonAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/review/people/{id}/assigned-faces", GetFacesAsync);
        return endpoints;
    }

    private static async Task<IResult> GetFacesAsync(
        string id,
        SqlitePersonAuditRepository repository,
        string? modelId = null,
        string? modelHash = null,
        int offset = 0,
        int limit = 40,
        bool disagreementsOnly = false,
        string sort = CataloguePersonAuditSorts.AssignedDescending,
        CancellationToken cancellationToken = default)
    {
        if (!TryPersonId(id, out PersonId personId) ||
            !TryModelRevision(modelId, modelHash, out ModelId? parsedModelId, out Sha256Digest? parsedModelHash))
        {
            return BadRequest("The person identifier or suggestion model revision is invalid.");
        }

        try
        {
            CataloguePersonAuditPage? page = await repository.GetFacesAsync(
                personId,
                parsedModelId,
                parsedModelHash,
                offset,
                limit,
                disagreementsOnly,
                sort,
                cancellationToken);
            if (page is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new PersonAuditPageResponse(
                ToResponse(page.Person),
                page.Items.Select(ToResponse).ToArray(),
                page.Offset,
                page.Limit,
                page.Total,
                page.DisagreementCount,
                page.Sort));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static PersonAuditFaceResponse ToResponse(CataloguePersonAuditFace face) => new(
        face.Id.ToString(),
        $"/api/review/faces/{face.Id}/image",
        face.PhotoName,
        face.Ordinal,
        face.Confidence,
        face.FaceCreatedAtUtc,
        face.AssignedAtUtc,
        face.AssignmentActionId,
        ToResponse(face.AssignedPerson),
        face.TopSuggestion is null ? null : ToResponse(face.TopSuggestion),
        face.SuggestionDisagrees);

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

    private static bool TryPersonId(string value, out PersonId id)
    {
        id = default;
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        id = PersonId.From(parsed);
        return true;
    }

    private static bool TryModelRevision(
        string? modelId,
        string? modelHash,
        out ModelId? parsedModelId,
        out Sha256Digest? parsedModelHash)
    {
        parsedModelId = null;
        parsedModelHash = null;
        if (string.IsNullOrWhiteSpace(modelId) && string.IsNullOrWhiteSpace(modelHash))
        {
            return true;
        }

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
