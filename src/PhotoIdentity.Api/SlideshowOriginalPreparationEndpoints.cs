using System.Globalization;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Web.Contracts;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public sealed record SlideshowBrowserPlaybackTimingRequest(
    int Sequence,
    double PresentationMilliseconds,
    double? ResourceMilliseconds,
    bool Prefetched);

public sealed record SlideshowBrowserPlaybackTimingBatchRequest(
    IReadOnlyList<SlideshowBrowserPlaybackTimingRequest>? Samples);

public static class SlideshowOriginalPreparationEndpoints
{
    public static IEndpointRouteBuilder MapSlideshowOriginalPreparationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/slideshows/original-preparation");
        group.MapPost("", StartAsync);
        group.MapPost("/revalidate", RevalidateAsync);
        group.MapGet("/{sessionId:guid}", GetStatus);
        group.MapPost("/{sessionId:guid}/retry", Retry);
        group.MapDelete("/{sessionId:guid}", EndAsync);
        group.MapGet("/{sessionId:guid}/photos/{revisionId}/original", GetPreparedOriginalAsync);
        endpoints.MapPost("/api/slideshows/diagnostics/playback", RecordBrowserPlaybackTiming);
        return endpoints;
    }

    private static IResult RecordBrowserPlaybackTiming(
        SlideshowBrowserPlaybackTimingBatchRequest request,
        ArchiveThroughputMetrics metrics)
    {
        const double maximumMilliseconds = 120_000d;
        if (request.Samples is null || request.Samples.Count is < 1 or > 50)
        {
            return BadBrowserTimingRequest();
        }

        foreach (SlideshowBrowserPlaybackTimingRequest sample in request.Samples)
        {
            if (sample.Sequence is < 1 or > 50 ||
                !double.IsFinite(sample.PresentationMilliseconds) ||
                sample.PresentationMilliseconds < 0d ||
                sample.PresentationMilliseconds > maximumMilliseconds ||
                (sample.ResourceMilliseconds is double resourceMilliseconds &&
                    (!double.IsFinite(resourceMilliseconds) ||
                     resourceMilliseconds < 0d ||
                     resourceMilliseconds > maximumMilliseconds)))
            {
                return BadBrowserTimingRequest();
            }
        }

        foreach (SlideshowBrowserPlaybackTimingRequest sample in request.Samples)
        {
            TimeSpan presentation = TimeSpan.FromMilliseconds(sample.PresentationMilliseconds);
            metrics.RecordStage(ArchiveThroughputMetricNames.SlideshowBrowserImagePresentation, presentation);
            metrics.RecordStage(
                ArchiveThroughputMetricNames.SlideshowBrowserImagePresentationPositionPrefix +
                sample.Sequence.ToString("D2", CultureInfo.InvariantCulture),
                presentation);

            if (sample.ResourceMilliseconds is double measuredResourceMilliseconds)
            {
                metrics.RecordStage(
                    ArchiveThroughputMetricNames.SlideshowBrowserImageResource,
                    TimeSpan.FromMilliseconds(measuredResourceMilliseconds));
            }

            metrics.RecordCounter(
                sample.Prefetched
                    ? ArchiveThroughputMetricNames.SlideshowBrowserPrefetchHits
                    : ArchiveThroughputMetricNames.SlideshowBrowserPrefetchMisses);
        }

        return Results.NoContent();
    }

    private static IResult BadBrowserTimingRequest() =>
        Results.BadRequest(new
        {
            error = "The slideshow browser timing samples are outside the supported diagnostic bounds.",
        });

    private static async Task<IResult> StartAsync(
        SlideshowOriginalPreparationRequest request,
        SlideshowOriginalPreparationService service,
        ArchiveThroughputMetrics metrics,
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
            SlideshowOriginalPreparationSnapshot snapshot;
            using (metrics.Measure(ArchiveThroughputMetricNames.SlideshowPreparationStart))
            {
                snapshot = await service.StartAsync(
                    revisionIds,
                    cancellationToken);
            }

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

    private static async Task<IResult> RevalidateAsync(
        SlideshowOriginalPreparationRequest request,
        CollectionOriginalAccessService originals,
        CancellationToken cancellationToken)
    {
        if (!TryRevisionIds(request.RevisionIds, out AssetRevisionId[] revisionIds))
        {
            return Results.BadRequest(new
            {
                error = "The slideshow preparation revalidation request contains an invalid revision identifier.",
            });
        }

        int ready = 0;
        foreach (AssetRevisionId revisionId in revisionIds)
        {
            CollectionOriginalAccessSnapshot? status = await originals.GetStatusAsync(
                revisionId,
                cancellationToken);
            if (status?.State != CollectionOriginalAccessService.ReadyState)
            {
                return Results.Ok(new SlideshowOriginalRevalidationResponse(
                    false,
                    ready,
                    revisionIds.Length));
            }

            ready++;
        }

        return Results.Ok(new SlideshowOriginalRevalidationResponse(
            true,
            ready,
            revisionIds.Length));
    }

    private static IResult GetStatus(
        Guid sessionId,
        SlideshowOriginalPreparationService service,
        ArchiveThroughputMetrics metrics)
    {
        SlideshowOriginalPreparationSnapshot? snapshot;
        using (metrics.Measure(ArchiveThroughputMetricNames.SlideshowPreparationStatus))
        {
            snapshot = service.GetStatus(sessionId);
        }

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

    private static async Task<IResult> EndAsync(
        Guid sessionId,
        SlideshowOriginalPreparationService service)
    {
        _ = await service.EndAsync(sessionId);
        return Results.NoContent();
    }

    private static async Task<IResult> GetPreparedOriginalAsync(
        Guid sessionId,
        string revisionId,
        SlideshowOriginalPreparationService service,
        ArchiveThroughputMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (!TryRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        VerifiedCollectionOriginal? original;
        using (metrics.Measure(ArchiveThroughputMetricNames.SlideshowPreparedOriginalOpen))
        {
            original = await service.OpenPreparedOriginalAsync(
                sessionId,
                parsedRevisionId,
                cancellationToken);
        }

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
