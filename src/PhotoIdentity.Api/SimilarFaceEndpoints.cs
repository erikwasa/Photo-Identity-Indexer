using Microsoft.AspNetCore.Mvc;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class SimilarFaceEndpoints
{
    public static IEndpointRouteBuilder MapSimilarFaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/review/faces/{id}/similar", GetSimilarAsync);
        return endpoints;
    }

    private static async Task<IResult> GetSimilarAsync(
        string id,
        [FromServices] ISimilarFaceRepository repository,
        string? modelId,
        string? modelHash,
        bool includeUnknown = false,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!TryFaceOccurrenceId(id, out FaceOccurrenceId faceOccurrenceId))
        {
            return BadRequest("The face occurrence identifier is invalid.");
        }

        if (!TryModelRevision(modelId, modelHash, out ModelId parsedModelId, out Sha256Digest parsedModelHash))
        {
            return BadRequest("An exact embedding model revision is required.");
        }

        if (limit is < 1 or > 200)
        {
            return BadRequest("Similar-face result count must be between 1 and 200.");
        }

        try
        {
            CatalogueSimilarFaceQueryResult? result = await repository.FindSimilarAsync(
                faceOccurrenceId,
                parsedModelId,
                parsedModelHash,
                includeUnknown,
                limit,
                cancellationToken);
            if (result is null)
            {
                return Results.NotFound(new
                {
                    error = "The source face is rejected, missing, or has no embedding for the selected exact model revision.",
                });
            }

            return Results.Ok(new SimilarFacePageResponse(
                result.SourceFaceId.ToString(),
                result.ModelId.ToString(),
                result.ModelHash.ToString(),
                result.IncludeUnknown,
                result.ScannedFaceCount,
                result.ElapsedMilliseconds,
                result.Items
                    .Select(item => new SimilarFaceResponse(ToResponse(item.Face), item.Similarity))
                    .ToArray()));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static ReviewFaceResponse ToResponse(CatalogueReviewFace face) => new(
        face.Id.ToString(),
        $"/api/review/faces/{face.Id}/image",
        face.PhotoName,
        face.Ordinal,
        face.Confidence,
        face.State,
        face.Person is null
            ? null
            : new ReviewPersonResponse(face.Person.Id.ToString(), face.Person.DisplayName),
        face.CreatedAtUtc,
        TopSuggestion: null,
        TargetBox: null);

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
