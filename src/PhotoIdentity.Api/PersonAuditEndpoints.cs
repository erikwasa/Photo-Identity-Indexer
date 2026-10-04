using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class PersonAuditEndpoints
{
    public static IEndpointRouteBuilder MapPersonAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/review/people/{id}/assigned-faces", GetFacesAsync);
        endpoints.MapGet("/api/review/assigned-faces/audit", GetAssignmentAuditAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAssignmentAuditAsync(
        PostgresCatalogueDatabase database,
        string source = AssignmentAuditSources.Automatic,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? modelId = null,
        string? modelHash = null,
        int offset = 0,
        int limit = 120,
        CancellationToken cancellationToken = default)
    {
        if (!TryModelRevision(
                modelId,
                modelHash,
                out ModelId? parsedModelId,
                out Sha256Digest? parsedModelHash))
        {
            return BadRequest("The suggestion model revision is invalid.");
        }

        try
        {
            IAssignmentAuditRepository repository =
                new PostgresAssignmentAuditRepository(database);
            AssignmentAuditPage page = await repository.GetAssignmentsAsync(
                source,
                fromUtc,
                toUtc,
                parsedModelId,
                parsedModelHash,
                offset,
                limit,
                cancellationToken);

            return Results.Ok(new AssignmentAuditPageResponse(
                page.Items.Select(ToResponse).ToArray(),
                page.Offset,
                page.Limit,
                page.Total,
                page.Source,
                page.FromUtc,
                page.ToUtc));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> GetFacesAsync(
        string id,
        IPersonAuditRepository repository,
        string? modelId = null,
        string? modelHash = null,
        int offset = 0,
        int limit = 40,
        bool disagreementsOnly = false,
        string sort = PersonAuditSorts.AssignedDescending,
        CancellationToken cancellationToken = default)
    {
        if (!TryPersonId(id, out PersonId personId) ||
            !TryModelRevision(modelId, modelHash, out ModelId? parsedModelId, out Sha256Digest? parsedModelHash))
        {
            return BadRequest("The person identifier or suggestion model revision is invalid.");
        }

        try
        {
            PersonAuditPage? page = await repository.GetFacesAsync(
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

    private static AssignmentAuditFaceResponse ToResponse(AssignmentAuditFace face) => new(
        face.Id.ToString(),
        $"/api/review/faces/{face.Id}/image",
        face.PhotoName,
        face.Ordinal,
        face.Confidence,
        face.FaceCreatedAtUtc,
        face.AssignedAtUtc,
        face.AssignmentActionId,
        face.AssignmentActor,
        ToResponse(face.AssignedPerson),
        face.AcceptedSuggestion is null ? null : ToResponse(face.AcceptedSuggestion),
        face.CurrentTopSuggestion is null ? null : ToResponse(face.CurrentTopSuggestion),
        face.CurrentSuggestionDisagrees);

    private static PersonAuditFaceResponse ToResponse(PersonAuditFace face) => new(
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
        PersonAuditTopSuggestion suggestion) => new(
        suggestion.Id,
        ToResponse(suggestion.Person),
        suggestion.ModelId.ToString(),
        suggestion.ModelHash.ToString(),
        suggestion.Rank,
        suggestion.Score,
        suggestion.ScoreMargin,
        suggestion.Status,
        suggestion.GeneratedAtUtc);

    private static ReviewPersonResponse ToResponse(ReviewPerson person) =>
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
