using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

/// <summary>Explicit opt-in read-only video pages; never used by photo routes.</summary>
public static class VideoLibraryEndpoints
{
    public static IEndpointRouteBuilder MapVideoLibraryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/videos", ListAsync);
        endpoints.MapGet("/api/videos/coverage", GetCoverageAsync);
        endpoints.MapGet("/api/videos/{revisionId}/poster", GetPosterAsync);
        endpoints.MapMethods("/api/videos/{revisionId}/play",
            ["GET", "HEAD"], GetPlaybackAsync);
        return endpoints;
    }

    private static bool Enabled(IConfiguration settings) =>
        string.Equals(settings["PhotoIdentity:VideoLibraryEnabled"], "true",
            StringComparison.OrdinalIgnoreCase);

    private static string DerivativeRoot(IConfiguration settings) =>
        settings["PhotoIdentity:VideoPosterRoot"] ??
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoIdentity", "video-derivatives");

    private static async Task<IResult> ListAsync(
        int? offset, int? limit, bool? playableOnly,
        PostgresVideoLibraryRepository videos, IConfiguration settings,
        HttpContext context, CancellationToken token)
    {
        DisableCaching(context);
        if (!Enabled(settings)) return Results.NotFound();
        int start = offset ?? 0;
        int count = limit ?? 24;
        if (start < 0 || count is < 1 or > 60)
            return Results.BadRequest(new { error = "Invalid bounded video page." });

        VideoLibraryPage page = await videos.ListAsync(start, count, playableOnly ?? false, token);
        return Results.Ok(new VideoLibraryResponse(
            page.Items.Select(item => new VideoLibraryItemResponse(
                item.RevisionId.ToString(), item.InspectionStatus,
                item.DurationSeconds, item.Width, item.Height,
                item.RotationDegrees, item.CaptureAtUtc, item.HasPoster,
                item.HasPlayableProxy)).ToArray(),
            page.Offset, page.Limit, page.HasMore));
    }

    private static async Task<IResult> GetCoverageAsync(
        PostgresVideoLibraryRepository videos, IConfiguration settings,
        HttpContext context, CancellationToken token)
    {
        DisableCaching(context);
        if (!Enabled(settings)) return Results.NotFound();
        VideoLibraryCoverage coverage = await videos.GetCoverageAsync(token);
        return Results.Ok(new VideoLibraryCoverageResponse(
            coverage.TotalVideos, coverage.PlayableVideos,
            coverage.VideosWithPosters, coverage.PlayableWithPosters,
            coverage.PlayableMissingPosters,
            coverage.UninspectedOrUnsupported));
    }

    private static string ProxyRoot(IConfiguration settings) =>
        settings["PhotoIdentity:VideoProcessing:CacheRoot"] ??
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoIdentity", "video-worker-cache");

    private static void DisableCaching(HttpContext context)
    {
        // Video metadata and private revision-bound posters must not persist in
        // client or intermediary HTTP caches, even when a route returns 404.
        context.Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    }


    private static async Task<IResult> GetPlaybackAsync(
        string revisionId,
        PostgresVideoPlaybackRepository proxies, IConfiguration settings,
        HttpContext context, CancellationToken token)
    {
        DisableCaching(context);
        if (!Enabled(settings) ||
            !Guid.TryParseExact(revisionId, "D", out Guid guid))
            return Results.NotFound();
        try
        {
            VideoPlaybackLease? lease = await proxies.OpenCurrentAsync(
                AssetRevisionId.From(guid), ProxyRoot(settings), token);
            return lease is null ? Results.NotFound() : new PlaybackResult(lease);
        }
        catch (Exception error) when (
            error is IOException or InvalidDataException or
                UnauthorizedAccessException or InvalidOperationException or
                ArgumentException)
        {
            // Missing/evicted/corrupt/linked cache entries are never served or
            // resolved through originals. Return no private path diagnostics.
            return Results.NotFound();
        }
    }

    private sealed class PlaybackResult(VideoPlaybackLease lease) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            // Results.File implements standard GET/HEAD + single-byte-range
            // 206/416 semantics on a seekable stream. Hold the source-row
            // lock until ASP.NET has finished streaming or aborted.
            await using (lease)
            {
                await Results.File(lease.Stream, "video/mp4",
                    enableRangeProcessing: true).ExecuteAsync(context);
            }
        }
    }

    private static async Task<IResult> GetPosterAsync(
        string revisionId,
        PostgresVideoLibraryRepository videos, IConfiguration settings,
        HttpContext context, CancellationToken token)
    {
        DisableCaching(context);
        if (!Enabled(settings)) return Results.NotFound();
        if (!Guid.TryParseExact(revisionId, "D", out Guid guid))
            return Results.NotFound();
        try
        {
            byte[]? bytes = await videos.GetPosterBytesAsync(
                AssetRevisionId.From(guid), DerivativeRoot(settings), token);
            return bytes is null
                ? Results.NotFound()
                : Results.File(bytes, "image/jpeg", enableRangeProcessing: false);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
                                    UnauthorizedAccessException or InvalidOperationException)
        {
            // Never expose local paths, GPS or raw filesystem diagnostics.
            return Results.NotFound();
        }
    }
}
