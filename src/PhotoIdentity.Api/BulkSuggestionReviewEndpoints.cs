using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class BulkSuggestionReviewEndpoints
{
    public static IEndpointRouteBuilder MapBulkSuggestionReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/review/bulk-suggestions");
        group.MapPost("/preview", PreviewAsync);
        group.MapPost("/commit", CommitAsync);
        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        BulkSuggestionPreviewRequest request,
        IBulkSuggestionReviewRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryModelRevision(request.ModelId, request.ModelHash, out ModelId modelId, out Sha256Digest modelHash))
        {
            return BadRequest("A valid exact suggestion model revision is required.");
        }

        try
        {
            BulkSuggestionPreview preview = await repository.PreviewAsync(
                request.SuggestionIds,
                modelId,
                modelHash,
                cancellationToken);
            return Results.Ok(ToResponse(preview));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> CommitAsync(
        BulkSuggestionCommitRequest request,
        IBulkSuggestionReviewRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!request.Confirm)
        {
            return BadRequest("Confirm the previewed affected count before accepting the suggestion group.");
        }

        if (!TryModelRevision(request.ModelId, request.ModelHash, out ModelId modelId, out Sha256Digest modelHash))
        {
            return BadRequest("A valid exact suggestion model revision is required.");
        }

        try
        {
            BulkSuggestionResult result = await repository.CommitAsync(
                request.SuggestionIds,
                modelId,
                modelHash,
                request.ExpectedAffectedCount,
                request.PreviewToken,
                request.Actor,
                timeProvider.GetUtcNow(),
                request.Note,
                cancellationToken);
            return Results.Ok(ToResponse(result));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static BulkSuggestionPreviewResponse ToResponse(BulkSuggestionPreview preview) => new(
        preview.RequestedCount,
        preview.AffectedCount,
        preview.SkippedCount,
        preview.PreviewToken,
        ToResponse(preview.Person),
        preview.ModelId.ToString(),
        preview.ModelHash.ToString());

    private static BulkSuggestionCommitResponse ToResponse(BulkSuggestionResult result) => new(
        result.RequestedCount,
        result.AffectedCount,
        ToResponse(result.Person),
        result.ModelId.ToString(),
        result.ModelHash.ToString(),
        result.CreatedAtUtc);

    private static ReviewPersonResponse ToResponse(ReviewPerson person) =>
        new(person.Id.ToString(), person.DisplayName);

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
