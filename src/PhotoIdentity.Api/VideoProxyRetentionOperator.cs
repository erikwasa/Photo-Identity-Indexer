using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Api;

/// <summary>
/// Explicit, bounded and user-confirmed proxy reclamation for Settings.
/// No caller-supplied paths, job IDs or quota values cross this boundary.
/// It reuses the existing DB-first stopped-lane maintenance implementation;
/// no worker, scheduler, background cleanup or archive read is registered.
/// </summary>
public sealed class VideoProxyRetentionOperator(
    PostgresCatalogueDatabase database,
    VideoProcessingPreferencesStore preferences,
    VideoArchiveProcessingService processor,
    IConfiguration configuration)
{
    // One short foreground request. The older operator-only CLI retains
    // independent configurable limits for maintenance specialists.
    public const int MaximumRecords = 8;
    public static readonly TimeSpan MinimumAge = TimeSpan.FromHours(48);

    public async Task<VideoRegisteredMaintenanceReport> PreviewAsync(
        CancellationToken token = default) =>
        await ScanAsync(apply: false, expectedFingerprint: null, token: token);

    public async Task<VideoRegisteredMaintenanceReport> ApplyAsync(
        string expectedFingerprint, CancellationToken token = default)
    {
        if (expectedFingerprint is not { Length: 64 } ||
            !expectedFingerprint.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException(
                "A valid current maintenance preview is required.");
        return await ScanAsync(apply: true, expectedFingerprint: expectedFingerprint, token: token);
    }

    private async Task<VideoRegisteredMaintenanceReport> ScanAsync(
        bool apply, string? expectedFingerprint, CancellationToken token)
    {
        if (processor.IsProcessing || preferences.Current.Enabled)
            throw new InvalidOperationException(
                "Disable video processing and wait for the active attempt to stop.");
        // The PostgreSQL transaction enforces BOTH enabled=false/paused=true
        // and zero running leases independently of these UI-level checks.
        VideoProxyRetentionLimits limits = preferences.Current
            .ResolvePublicationQuota(configuration)
            ?? throw new InvalidOperationException(
                "Configure a video proxy quota before previewing retention.");

        string root = configuration["PhotoIdentity:VideoProcessing:CacheRoot"]
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoIdentity", "video-worker-cache");

        // Fail closed if the private cache does not already exist and match
        // its ownership marker/source-root/reparse protection; never create
        // a user-provided path during maintenance.
        return await new VideoRegisteredDerivativeMaintenance(database)
            .SweepAsync(root,
                maximumRecords: MaximumRecords,
                quotaMiB: limits.MaximumCacheBytes / (1024L * 1024),
                minimumQuotaAge: MinimumAge,
                verifyHashes: true,
                apply: apply,
                token: token,
                expectedPlanFingerprint: expectedFingerprint);
    }
}
