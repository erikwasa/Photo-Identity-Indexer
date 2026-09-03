using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Review;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class BulkReviewEndpoints
{
    public static IEndpointRouteBuilder MapBulkReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/review/bulk");
        group.MapPost("/preview", PreviewAsync);
        group.MapPost("/commit", CommitAsync);
        return endpoints;
    }

    private static async Task<IResult> PreviewAsync(
        BulkReviewPreviewRequest request,
        IBulkReviewRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryFaceOccurrenceIds(request.FaceIds, out FaceOccurrenceId[] faceOccurrenceIds) ||
            !TryOptionalPersonId(request.PersonId, out PersonId? personId))
        {
            return BadRequest("One or more face or person identifiers are invalid.");
        }

        try
        {
            BulkReviewPreview preview = await repository.PreviewAsync(
                faceOccurrenceIds,
                request.Action,
                personId,
                cancellationToken);
            return Results.Ok(ToResponse(preview));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private static async Task<IResult> CommitAsync(
        BulkReviewCommitRequest request,
        IBulkReviewRepository repository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!request.Confirm)
        {
            return BadRequest("Confirm the previewed affected count before committing the bulk action.");
        }

        if (!TryFaceOccurrenceIds(request.FaceIds, out FaceOccurrenceId[] faceOccurrenceIds) ||
            !TryOptionalPersonId(request.PersonId, out PersonId? personId))
        {
            return BadRequest("One or more face or person identifiers are invalid.");
        }

        try
        {
            BulkReviewResult result = await repository.CommitAsync(
                faceOccurrenceIds,
                request.Action,
                personId,
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

    private static BulkReviewPreviewResponse ToResponse(BulkReviewPreview preview) => new(
        preview.Action,
        preview.RequestedCount,
        preview.AffectedCount,
        preview.SkippedCount,
        preview.PreviewToken,
        preview.Person is null ? null : ToResponse(preview.Person));

    private static BulkReviewCommitResponse ToResponse(BulkReviewResult result) => new(
        result.Action,
        result.RequestedCount,
        result.AffectedCount,
        result.Person is null ? null : ToResponse(result.Person),
        result.CreatedAtUtc);

    private static ReviewPersonResponse ToResponse(ReviewPerson person) =>
        new(person.Id.ToString(), person.DisplayName);

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });

    private static bool TryFaceOccurrenceIds(
        IReadOnlyList<string>? values,
        out FaceOccurrenceId[] ids)
    {
        ids = [];
        if (values is null || values.Count == 0)
        {
            return false;
        }

        List<FaceOccurrenceId> parsed = new(values.Count);
        foreach (string value in values)
        {
            if (!Guid.TryParse(value, out Guid guid) || guid == Guid.Empty)
            {
                return false;
            }

            parsed.Add(FaceOccurrenceId.From(guid));
        }

        ids = parsed.ToArray();
        return true;
    }

    private static bool TryOptionalPersonId(string? value, out PersonId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Guid.TryParse(value, out Guid guid) || guid == Guid.Empty)
        {
            return false;
        }

        id = PersonId.From(guid);
        return true;
    }
}
