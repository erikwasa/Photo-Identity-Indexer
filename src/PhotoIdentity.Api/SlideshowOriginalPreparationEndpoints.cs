using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class SlideshowOriginalPreparationEndpoints
{
    public static IEndpointRouteBuilder MapSlideshowOriginalPreparationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/slideshows/original-preparation");
        group.MapPost("", StartAsync);
        group.MapGet("/{sessionId:guid}", GetStatus);
        group.MapPost("/{sessionId:guid}/retry", Retry);
        group.MapDelete("/{sessionId:guid}", End);
        group.MapGet("/{sessionId:guid}/photos/{revisionId}/original", GetPreparedOriginalAsync);
        return endpoints;
    }

    private static async Task<IResult> StartAsync(
        SlideshowOriginalPreparationRequest request,
        SlideshowOriginalPreparationService service,
        CancellationToken cancellationToken)
    {
        if (!TryRevisionIds(request.RevisionIds, out AssetRevisionId[] revisionIds))
        {
            return Results.BadRequest(new
            {
                error = "The slideshow preparation request contains an invalid revision identifier.",
            });
        }

        try
        {
            SlideshowOriginalPreparationSnapshot snapshot = await service.StartAsync(
                revisionIds,
                cancellationToken);
            return Results.Accepted(
                $"/api/slideshows/original-preparation/{snapshot.SessionId:D}",
                ToResponse(snapshot));
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }

    private static IResult GetStatus(
        Guid sessionId,
        SlideshowOriginalPreparationService service)
    {
        SlideshowOriginalPreparationSnapshot? snapshot = service.GetStatus(sessionId);
        return snapshot is null
            ? Results.NotFound(new { error = "The slideshow preparation session is no longer available." })
            : Results.Ok(ToResponse(snapshot));
    }

    private static IResult Retry(
        Guid sessionId,
        SlideshowOriginalPreparationService service)
    {
        SlideshowOriginalPreparationSnapshot? snapshot = service.Retry(sessionId);
        return snapshot is null
            ? Results.NotFound(new { error = "The slideshow preparation session is no longer available." })
            : Results.Ok(ToResponse(snapshot));
    }

    private static IResult End(
        Guid sessionId,
        SlideshowOriginalPreparationService service)
    {
        _ = service.End(sessionId);
        return Results.NoContent();
    }

    private static async Task<IResult> GetPreparedOriginalAsync(
        Guid sessionId,
        string revisionId,
        SlideshowOriginalPreparationService service,
        CancellationToken cancellationToken)
    {
        if (!TryRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        VerifiedCollectionOriginal? original = await service.OpenPreparedOriginalAsync(
            sessionId,
            parsedRevisionId,
            cancellationToken);
        return original is null
            ? Results.NotFound(new
            {
                error = "The prepared original is unavailable, no longer verified, or is not part of this active slideshow session.",
            })
            : Results.File(original.Stream, original.ContentType, enableRangeProcessing: true);
    }

    private static SlideshowOriginalPreparationResponse ToResponse(
        SlideshowOriginalPreparationSnapshot snapshot) =>
        new(
            snapshot.SessionId.ToString("D"),
            snapshot.State,
            snapshot.Ready,
            snapshot.Total,
            snapshot.Downloading,
            snapshot.Queued,
            snapshot.WaitingForRelease,
            snapshot.HydrationRequests,
            snapshot.Phase,
            snapshot.LastProgressAtUtc,
            snapshot.NoProgressSeconds,
            snapshot.NoProgressWarning,
            snapshot.CanRetry,
            snapshot.RequiredAdditionalBytes,
            snapshot.AvailableManagedCapacity,
            snapshot.Message,
            snapshot.CanContinueWithAvailable);

    private static bool TryRevisionIds(
        string[]? values,
        out AssetRevisionId[] revisionIds)
    {
        revisionIds = [];
        if (values is null)
        {
            return false;
        }

        List<AssetRevisionId> parsed = new(values.Length);
        foreach (string value in values)
        {
            if (!TryRevisionId(value, out AssetRevisionId revisionId))
            {
                return false;
            }

            parsed.Add(revisionId);
        }

        revisionIds = parsed.Distinct().ToArray();
        return true;
    }

    private static bool TryRevisionId(
        string value,
        out AssetRevisionId revisionId)
    {
        revisionId = default;
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        revisionId = AssetRevisionId.From(parsed);
        return true;
    }
}
