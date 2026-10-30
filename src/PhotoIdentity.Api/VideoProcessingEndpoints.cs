using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Web;

namespace PhotoIdentity.Api;

/// <summary>
/// A nullable override flag preserves compatibility with callers that update
/// only the original Enabled/IncludeOnlineOnly settings. Clearing quotas must
/// be explicit; missing fields never silently erase a stored override.
/// </summary>
public sealed record VideoProcessingSettingsUpdate(
    bool Enabled, bool IncludeOnlineOnly,
    int? MaximumCacheMiB = null, int? MaximumProxyMiB = null,
    bool? OverrideProxyLimits = null)
{
    public VideoProcessingPreferences MergeWith(VideoProcessingPreferences existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (OverrideProxyLimits is null)
        {
            if (MaximumCacheMiB.HasValue || MaximumProxyMiB.HasValue)
                throw new ArgumentException(
                    "An explicit quota override choice is required.");
            return existing with
            {
                Enabled = Enabled,
                IncludeOnlineOnly = IncludeOnlineOnly
            };
        }
        if (OverrideProxyLimits is false)
        {
            if (MaximumCacheMiB.HasValue || MaximumProxyMiB.HasValue)
                throw new ArgumentException(
                    "Clearing quota overrides cannot supply quota limits.");
            return new(Enabled, IncludeOnlineOnly);
        }

        return new(Enabled, IncludeOnlineOnly, MaximumCacheMiB, MaximumProxyMiB);
    }
}

public sealed record VideoProcessingSettingsResponse(
    bool Enabled, bool IncludeOnlineOnly,
    int? MaximumCacheMiB, int? MaximumProxyMiB,
    bool Processing, bool ToolsConfigured,
    long Queued, long Running, long Succeeded, long Failed,
    long Cancelled, long ExpiredLeases, string LastResult,
    IReadOnlyList<VideoProcessingJobOperatorRow> Recent,
    VideoAttemptFailureDiagnostic? LastAttemptFailure);

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
        endpoints.MapPost("/api/video-processing/jobs/{jobId:guid}/retry",
            RetryFailedAsync);
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
            processor.LastResult, recent, processor.LastAttemptFailure));
    }

    private static async Task<IResult> UpdateAsync(
        VideoProcessingSettingsUpdate value,
        VideoProcessingPreferencesStore preferences,
        VideoArchiveProcessingService processor,
        PostgresCatalogueDatabase database,
        IConfiguration configuration,
        CancellationToken token)
    {
        VideoProcessingPreferences existing = preferences.Current;
        VideoProcessingPreferences requested;
        try
        {
            requested = value.MergeWith(existing);
            // Always allow operators to disable video processing, even
            // if inherited launcher settings are malformed. Only enabling
            // may consume the runtime quota configuration.
            requested.Validate();
            if (requested.Enabled)
                _ = requested.ResolvePublicationQuota(configuration);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ArchiveErrorResponse(
                "Specify both valid video proxy limits or neither; cache 64..16384 MiB, per-file 1..256 MiB and per-file <= cache."));
        }
        bool quotaChanged = existing.MaximumCacheMiB != requested.MaximumCacheMiB ||
                            existing.MaximumProxyMiB != requested.MaximumProxyMiB;
        if (quotaChanged && processor.IsProcessing)
            return Results.Conflict(new ArchiveErrorResponse(
                "Pause archive processing before changing video proxy storage limits."));

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
            requested, token);
        var inspector = new PostgresVideoProcessingQueueInspector(database);
        var overview = await inspector.GetOverviewAsync(token);
        var recent = await inspector.ListRecentAsync(12, token);
        return Results.Ok(ToResponse(saved, overview,
            processor.IsProcessing, ToolsConfigured(configuration),
            processor.LastResult, recent, processor.LastAttemptFailure));
    }

    private static async Task<IResult> RetryFailedAsync(
        Guid jobId, PostgresCatalogueDatabase database,
        CancellationToken token)
    {
        if (jobId == Guid.Empty)
            return Results.BadRequest(new ArchiveErrorResponse(
                "A valid video job ID is required."));
        bool restarted = await new PostgresVideoProcessingJobRepository(database)
            .RestartFailedAsync(jobId, token);
        return restarted
            ? Results.Ok(new { restarted = true })
            : Results.Conflict(new ArchiveErrorResponse(
                "This video job is not a retryable failed/cancelled current revision."));
    }

    private static bool ToolsConfigured(IConfiguration configuration) =>
        new[] { "FfmpegPath", "FfmpegSha256", "FfprobePath", "FfprobeSha256" }
            .All(key => !string.IsNullOrWhiteSpace(
                configuration["PhotoIdentity:VideoProcessing:" + key]));

    private static VideoProcessingSettingsResponse ToResponse(
        VideoProcessingPreferences settings,
        VideoProcessingQueueOverview status,
        bool processing, bool configured, string lastResult,
        IReadOnlyList<VideoProcessingJobOperatorRow> recent,
        VideoAttemptFailureDiagnostic? lastAttemptFailure) =>
        new(settings.Enabled, settings.IncludeOnlineOnly,
            settings.MaximumCacheMiB, settings.MaximumProxyMiB,
            processing, configured, status.Queued, status.Running,
            status.Succeeded, status.Failed, status.Cancelled,
            status.ExpiredLeases, lastResult, recent, lastAttemptFailure);
}
