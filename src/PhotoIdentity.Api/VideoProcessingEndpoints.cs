using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Web;

namespace PhotoIdentity.Api;

public sealed record VideoProcessingSettingsUpdate(
    bool Enabled, bool IncludeOnlineOnly);

public sealed record VideoProcessingSettingsResponse(
    bool Enabled, bool IncludeOnlineOnly,
    bool Processing, bool ToolsConfigured,
    long Queued, long Running, long Succeeded, long Failed,
    long Cancelled, long ExpiredLeases, string LastResult,
    IReadOnlyList<VideoProcessingJobOperatorRow> Recent);

/// <summary>
/// Operator settings only. No arbitrary source paths, executable paths,
/// archive writes or per-video process endpoint are accepted over HTTP.
/// The existing Archive start/pause commands remain the sole run switch.
/// </summary>
public static class VideoProcessingEndpoints
{
    public static IEndpointRouteBuilder MapVideoProcessingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/video-processing/settings", GetAsync);
        endpoints.MapPut("/api/video-processing/settings", UpdateAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        VideoProcessingPreferencesStore preferences,
        VideoArchiveProcessingService processor,
        PostgresCatalogueDatabase database,
        IConfiguration configuration,
        CancellationToken token)
    {
        var inspector = new PostgresVideoProcessingQueueInspector(database);
        var overview = await inspector.GetOverviewAsync(token);
        var recent = await inspector.ListRecentAsync(12, token);
        return Results.Ok(ToResponse(preferences.Current, overview,
            processor.IsProcessing, ToolsConfigured(configuration),
            processor.LastResult, recent));
    }

    private static async Task<IResult> UpdateAsync(
        VideoProcessingSettingsUpdate value,
        VideoProcessingPreferencesStore preferences,
        VideoArchiveProcessingService processor,
        PostgresCatalogueDatabase database,
        IConfiguration configuration,
        CancellationToken token)
    {
        // Do not enable video work without explicit approved local tool
        // configuration. This also prevents the UI from advertising success
        // when no executable path/hash was ever configured.
        if (value.Enabled && !ToolsConfigured(configuration))
            return Results.BadRequest(new ArchiveErrorResponse(
                "Configure approved local FFmpeg/FFprobe paths and SHA-256 hashes before enabling video processing."));

        var controls = new PostgresVideoProcessingControlRepository(database);
        if (!value.Enabled)
        {
            processor.RequestPause();
            await controls.DisableKeepQueuedAsync(token);
        }
        else
        {
            await controls.EnableAsync(token);
        }
        VideoProcessingPreferences saved = await preferences.SaveAsync(
            new(value.Enabled, value.IncludeOnlineOnly), token);
        var inspector = new PostgresVideoProcessingQueueInspector(database);
        var overview = await inspector.GetOverviewAsync(token);
        var recent = await inspector.ListRecentAsync(12, token);
        return Results.Ok(ToResponse(saved, overview,
            processor.IsProcessing, ToolsConfigured(configuration),
            processor.LastResult, recent));
    }

    private static bool ToolsConfigured(IConfiguration configuration) =>
        new[] { "FfmpegPath", "FfmpegSha256", "FfprobePath", "FfprobeSha256" }
            .All(key => !string.IsNullOrWhiteSpace(
                configuration["PhotoIdentity:VideoProcessing:" + key]));

    private static VideoProcessingSettingsResponse ToResponse(
        VideoProcessingPreferences settings,
        VideoProcessingQueueOverview status,
        bool processing, bool configured, string lastResult,
        IReadOnlyList<VideoProcessingJobOperatorRow> recent) =>
        new(settings.Enabled, settings.IncludeOnlineOnly,
            processing, configured, status.Queued, status.Running,
            status.Succeeded, status.Failed, status.Cancelled,
            status.ExpiredLeases, lastResult, recent);
}
