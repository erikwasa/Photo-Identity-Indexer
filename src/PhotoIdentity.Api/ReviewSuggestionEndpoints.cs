using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class ReviewSuggestionEndpoints
{
    public static IEndpointRouteBuilder MapReviewSuggestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/review/faces/{id}/suggestions", GetSuggestionsAsync);
        endpoints.MapPost(
            "/api/review/faces/{id}/suggestions/{suggestionId:long}/accept",
            AcceptSuggestionAsync);
        endpoints.MapPost(
            "/api/review/faces/{id}/suggestions/{suggestionId:long}/reject",
            RejectSuggestionAsync);
        return endpoints;
    }

    private static async Task<IResult> GetSuggestionsAsync(
        string id,
        SqliteReviewRepository reviewRepository,
        SqliteReviewSuggestionRepository suggestionRepository,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        CatalogueReviewFace? face = await reviewRepository.GetFaceAsync(
            faceOccurrenceId,
            cancellationToken);
        if (face is null)
        {
            return Results.NotFound();
        }

        IReadOnlyList<CatalogueReviewIdentitySuggestion> suggestions =
            await suggestionRepository.GetSuggestionsAsync(faceOccurrenceId, cancellationToken);
        return Results.Ok(suggestions.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> AcceptSuggestionAsync(
        string id,
        long suggestionId,
        ReviewSuggestionActionRequest request,
        SqliteReviewSuggestionRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        try
        {
            CatalogueReviewIdentitySuggestion suggestion = await repository.AcceptAsync(
                faceOccurrenceId,
                suggestionId,
                request.Actor,
                timeProvider.GetUtcNow(),
                request.Note,
                cancellationToken);
            return Results.Ok(ToResponse(suggestion));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
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

    private static async Task<IResult> RejectSuggestionAsync(
        string id,
        long suggestionId,
        ReviewSuggestionActionRequest request,
        SqliteReviewSuggestionRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        try
        {
            CatalogueReviewIdentitySuggestion suggestion = await repository.RejectAsync(
                faceOccurrenceId,
                suggestionId,
                request.Actor,
                timeProvider.GetUtcNow(),
                request.Note,
                cancellationToken);
            return Results.Ok(ToResponse(suggestion));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
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

    private static ReviewIdentitySuggestionResponse ToResponse(
        CatalogueReviewIdentitySuggestion suggestion) => new(
            suggestion.Id,
            new ReviewPersonResponse(
                suggestion.Person.Id.ToString(),
                suggestion.Person.DisplayName),
            suggestion.ModelId.ToString(),
            suggestion.ModelHash.ToString(),
            suggestion.Rank,
            suggestion.Score,
            suggestion.ScoreMargin,
            suggestion.Status,
            suggestion.GeneratedAtUtc,
            suggestion.LatestAction is null
                ? null
                : new ReviewSuggestionActionResponse(
                    suggestion.LatestAction.Id,
                    suggestion.LatestAction.Kind,
                    suggestion.LatestAction.Actor,
                    suggestion.LatestAction.Note,
                    suggestion.LatestAction.CreatedAtUtc,
                    suggestion.LatestAction.ReviewActionId));

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });

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
}
