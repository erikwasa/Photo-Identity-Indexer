using System.Net;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Web;

namespace PhotoIdentity.Api;

/// <summary>
/// A confirmation token is a short-lived snapshot fingerprint, not a
/// credential or a filesystem path. The server rechecks the complete plan.
/// </summary>
public sealed record VideoProxyRetentionApplyRequest(string ConfirmationFingerprint);

/// <summary>
/// A nullable override flag preserves compatibility with callers that update
/// only the original Enabled/IncludeOnlineOnly settings. Clearing quotas must
/// be explicit; missing fields never silently erase a stored override.
/// </summary>
public sealed record VideoProcessingSettingsUpdate(
    bool Enabled, bool IncludeOnlineOnly,
    int? MaximumCacheMiB = null, int? MaximumProxyMiB = null,
    bool? OverrideProxyLimits = null, string? EncodingProfile = null,
    int? RetentionAgeHours = null)
{
    public VideoProcessingPreferences MergeWith(VideoProcessingPreferences existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        string selectedProfile = EncodingProfile ?? existing.EncodingProfile;
        int retentionAge = RetentionAgeHours ?? existing.RetentionAgeHours;
        if (OverrideProxyLimits is null)
        {
            if (MaximumCacheMiB.HasValue || MaximumProxyMiB.HasValue)
                throw new ArgumentException(
                    "An explicit quota override choice is required.");
            return existing with
            {
                Enabled = Enabled,
                IncludeOnlineOnly = IncludeOnlineOnly,
                EncodingProfile = selectedProfile,
                RetentionAgeHours = retentionAge
            };
        }
        if (OverrideProxyLimits is false)
        {
            if (MaximumCacheMiB.HasValue || MaximumProxyMiB.HasValue)
                throw new ArgumentException(
                    "Clearing quota overrides cannot supply quota limits.");
            return new(Enabled, IncludeOnlineOnly,
                EncodingProfile: selectedProfile,
                RetentionAgeHours: retentionAge);
        }

        if (!MaximumCacheMiB.HasValue || !MaximumProxyMiB.HasValue)
            throw new ArgumentException(
                "Both quota limits are required when overriding launcher settings.");
        return new(Enabled, IncludeOnlineOnly,
            MaximumCacheMiB, MaximumProxyMiB, selectedProfile, retentionAge);
    }
}

public sealed record VideoProcessingSettingsResponse(
    bool Enabled, bool IncludeOnlineOnly,
    int? MaximumCacheMiB, int? MaximumProxyMiB,
    string EncodingProfile, int RetentionAgeHours,
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
        endpoints.MapPost("/api/video-processing/retention/preview",
            PreviewRetentionAsync);
        endpoints.MapPost("/api/video-processing/retention/apply",
            ApplyRetentionAsync);
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
                "Choose an approved video profile and retention age (48 hours, 7 days or 30 days); quota override requires valid paired limits."));
        }
        bool quotaOrProfileChanged =
            existing.MaximumCacheMiB != requested.MaximumCacheMiB ||
            existing.MaximumProxyMiB != requested.MaximumProxyMiB ||
            !string.Equals(existing.EncodingProfile,
                requested.EncodingProfile, StringComparison.Ordinal) ||
            existing.RetentionAgeHours != requested.RetentionAgeHours;
        if (quotaOrProfileChanged && processor.IsProcessing)
            return Results.Conflict(new ArchiveErrorResponse(
                "Pause archive processing before changing video proxy limits, encoding profile or retention policy."));

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

    private static async Task<IResult> PreviewRetentionAsync(
        HttpContext context, PostgresCatalogueDatabase database,
        VideoProcessingPreferencesStore preferences,
        VideoArchiveProcessingService processor,
        IConfiguration configuration,
        CancellationToken token)
    {
        if (!IsDirectLoopback(context.Connection.RemoteIpAddress,
                context.Connection.LocalIpAddress))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            VideoRegisteredMaintenanceReport report =
                await new VideoProxyRetentionOperator(
                    database, preferences, processor, configuration)
                    .PreviewAsync(token);
            return Results.Ok(report);
        }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new ArchiveErrorResponse(
                "Retention preview requires configured limits, the video lane disabled and paused, and no running leases."));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ArchiveErrorResponse(
                "Invalid video retention settings or private cache configuration."));
        }
        catch (InvalidDataException)
        {
            return Results.Conflict(new ArchiveErrorResponse(
                "The registered proxy evidence is inconsistent; no unverified cache cleanup is permitted."));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Conflict(new ArchiveErrorResponse(
                "The owned private proxy cache could not be accessed."));
        }
        catch (IOException)
        {
            return Results.Conflict(new ArchiveErrorResponse(
                "The owned private proxy cache could not be verified."));
        }
    }

    private static async Task<IResult> ApplyRetentionAsync(
        HttpContext context, VideoProxyRetentionApplyRequest value,
        PostgresCatalogueDatabase database,
        VideoProcessingPreferencesStore preferences,
        VideoArchiveProcessingService processor,
        IConfiguration configuration,
        CancellationToken token)
    {
        if (!IsDirectLoopback(context.Connection.RemoteIpAddress,
                context.Connection.LocalIpAddress))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (value is null || string.IsNullOrWhiteSpace(value.ConfirmationFingerprint))
            return Results.BadRequest(new ArchiveErrorResponse(
                "Review a fresh proxy retention preview before confirming."));
        try
        {
            VideoRegisteredMaintenanceReport report =
                await new VideoProxyRetentionOperator(
                    database, preferences, processor, configuration)
                    .ApplyAsync(value.ConfirmationFingerprint, token);
            return report.DeleteFailures == 0
                ? Results.Ok(report)
                : Results.Conflict(new ArchiveErrorResponse(
                    "Some already revoked private files could not be deleted. Run a new preview and review recovery."));
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new ArchiveErrorResponse(
                "Invalid proxy retention confirmation."));
        }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new ArchiveErrorResponse(
                "Retention changed or expired, or processing has not fully stopped. Run a new preview."));
        }
        catch (InvalidDataException)
        {
            return Results.Conflict(new ArchiveErrorResponse(
                "The registered proxy evidence is inconsistent; no unverified cache cleanup is permitted."));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Conflict(new ArchiveErrorResponse(
                "The owned private proxy cache could not be accessed."));
        }
        catch (IOException)
        {
            return Results.Conflict(new ArchiveErrorResponse(
                "The owned private proxy cache could not be verified."));
        }
    }

    /// <summary>
    /// Restrict operator-triggered proxy deletion to direct loopback,
    /// independent of an optional trusted-LAN listener or any HTTP headers.
    /// Remote and local socket addresses must BOTH be loopback.
    /// </summary>
    public static bool IsDirectLoopback(IPAddress? remote, IPAddress? local)
    {
        static bool Local(IPAddress? address)
        {
            if (address is null) return false;
            IPAddress checkedAddress = address.IsIPv4MappedToIPv6
                ? address.MapToIPv4() : address;
            return IPAddress.IsLoopback(checkedAddress);
        }
        return Local(remote) && Local(local);
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
            settings.EncodingProfile, settings.RetentionAgeHours,
            processing, configured, status.Queued, status.Running,
            status.Succeeded, status.Failed, status.Cancelled,
            status.ExpiredLeases, lastResult, recent, lastAttemptFailure);
}
