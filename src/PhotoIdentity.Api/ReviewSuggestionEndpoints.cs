using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class ReviewSuggestionEndpoints
{
    public static IEndpointRouteBuilder MapReviewSuggestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/review/faces/{id}/suggestions", GetSuggestionsAsync);
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
            return Results.BadRequest(new { error = "The face occurrence identifier is invalid." });
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
            suggestion.GeneratedAtUtc);

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
