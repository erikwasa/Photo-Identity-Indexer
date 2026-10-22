using System.Security.Cryptography;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

/// <summary>
/// One video at a time, only from the existing archive advancement loop after
/// photo work has drained. Never deletes originals or touches photo tables.
/// No periodic/background video worker is registered separately.
/// </summary>
public sealed class VideoArchiveProcessingService(
    PostgresCatalogueDatabase database,
    VideoProcessingPreferencesStore preferences,
    IConfiguration configuration,
    IOneDriveFilesOnDemandPlatform oneDrive,
    ILogger<VideoArchiveProcessingService> logger)
{
    private const long MaxSourceBytes = 256L * 1024 * 1024;
    private const long ReservationBytes = 512L * 1024 * 1024;
    private readonly object _activeLock = new();
    private CancellationTokenSource? _active;
    public bool IsProcessing
    {
        get { lock (_activeLock) return _active is not null; }
    }

    /// <summary>Shared archive Pause and Settings disable both stop active video work.</summary>
    public void RequestPause()
    {
        lock (_activeLock) _active?.Cancel();
    }

    public static VideoProcessingBudget JobBudget => new(
        Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
        MaximumSourceBytes: MaxSourceBytes,
        MaximumTemporaryBytes: ReservationBytes,
        MaximumExtractedFrames: 1, MaximumJobSeconds: 660);

    public static VideoToolExecutionBudget ToolBudget => new(
        MaximumWallTime: TimeSpan.FromMinutes(9),
        MaximumTemporaryBytes: VideoProxyCandidateProfile.MaximumOutputBytes,
        MaximumTemporaryEntries: 16,
        InspectionInterval: TimeSpan.FromMilliseconds(200),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 768, MaximumProcesses: 1,
            MaximumCpuPercent: 15,
            MaximumObservedReadBytes: 512L * 1024 * 1024,
            MaximumObservedWriteBytes: 384L * 1024 * 1024));

    /// <returns>True while a video job was attempted or a video hydration
    /// was requested, so the archive keeps its single Run session alive.</returns>
    public async Task<bool> AdvanceOnceAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows() || !preferences.Current.Enabled)
            return false;

        CancellationTokenSource active;
        lock (_activeLock)
        {
            if (_active is not null) return false;
            _active = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            active = _active;
        }

        try
        {
            CancellationToken token = active.Token;
            // Required external tools are configured explicitly by the operator
            // and checked by digest; Settings does not silently install tools.
            string ffmpeg = Required("FfmpegPath");
            string ffprobe = Required("FfprobePath");
            string ffmpegHash = Required("FfmpegSha256");
            string ffprobeHash = Required("FfprobeSha256");
            if (!VideoDerivativeArtifactPath.IsHash(ffmpegHash) ||
                !VideoDerivativeArtifactPath.IsHash(ffprobeHash))
                throw new InvalidOperationException("Video executable approval is invalid.");
            await VerifyToolAsync(ffmpeg, ffmpegHash, token);
            await VerifyToolAsync(ffprobe, ffprobeHash, token);

            string cache = configuration["PhotoIdentity:VideoProcessing:CacheRoot"]
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoIdentity", "video-worker-cache");
            // Do not create or mark a pre-existing directory that might hold
            // unrelated data. A new owned cache stays outside all archive roots.
            if (!Directory.Exists(cache))
            {
                Directory.CreateDirectory(cache);
                await File.WriteAllTextAsync(
                    Path.Combine(cache, VideoDerivativeFilePublisher.CacheMarker),
                    VideoDerivativeFilePublisher.CacheMarkerContents, token);
            }
            await VideoDerivativeCacheGuard.ValidateAsync(database, cache, token);
            var control = new PostgresVideoProcessingControlRepository(database);
            VideoProcessingControlState state = await control.GetAsync(token);
            if (!state.Enabled || state.Paused)
                await control.EnableAsync(token);

            if ((await new PostgresVideoPhotoPriorityProbe(database)
                    .GetAsync(token)).PhotoWorkActive)
                return false;

            // Recover expired claims after a crashed process before making
            // another attempt; never reset completed derivative receipts.
            await new PostgresVideoProcessingJobRepository(database)
                .RecoverExpiredAsync(token);

            VideoCandidate? candidate = await FindPendingAsync(
                ffmpegHash, token);
            bool ownedHydration = false;
            string? ownedPath = null;
            try
            {
                if (candidate is not null)
                {
                    var source = new OneDriveVideoAssetSource(
                        SourceId.From(candidate.SourceId), candidate.Root);
                    var reference = new SourceAssetReference(
                        source.SourceId, candidate.Key);
                    AssetAvailability availability =
                        await source.GetAvailabilityAsync(reference, token);
                    if (availability != AssetAvailability.Local)
                    {
                        if (!preferences.Current.IncludeOnlineOnly ||
                            availability != AssetAvailability.OnlineOnly)
                            return false;
                        // The source is a DB-vetted archive entry, never a
                        // caller path. No duplicate archive downloads.
                        ownedPath = ResolveOwnedSourcePath(
                            candidate.Root, candidate.Key);
                        if (oneDrive.GetSyncClientAvailability() ==
                            OneDriveSyncClientAvailability.Unavailable)
                            throw new InvalidOperationException(
                                "OneDrive is unavailable for video hydration.");
                        await oneDrive.RequestHydrationAsync(ownedPath, token);
                        ownedHydration = true;
                        using CancellationTokenSource hydrationTimeout =
                            CancellationTokenSource.CreateLinkedTokenSource(token);
                        hydrationTimeout.CancelAfter(TimeSpan.FromMinutes(5));
                        while (await source.GetAvailabilityAsync(
                            reference, hydrationTimeout.Token) != AssetAvailability.Local)
                        {
                            if ((await new PostgresVideoPhotoPriorityProbe(database)
                                    .GetAsync(hydrationTimeout.Token)).PhotoWorkActive)
                                return false;
                            await Task.Delay(TimeSpan.FromSeconds(2),
                                hydrationTimeout.Token);
                        }
                    }

                    string local = source.GetLocalPath(reference);
                    if (new FileInfo(local).Length != candidate.Size)
                        return false;

                    var identity = new VideoDerivativeJobIdentity(
                        AssetRevisionId.From(candidate.RevisionId),
                        VideoDerivativeKind.PlayableProxy, "wi0188-v1",
                        VideoProxyCandidateProfile.Version, ffmpegHash);
                    var admission = new VideoProcessingCandidate(
                        IsCurrentRevision: true, IsExcluded: false,
                        IsLocallyAvailable: true, HasSupportedInspection: true,
                        SourceBytes: candidate.Size,
                        ExpectedTemporaryBytes: candidate.Size + 256L * 1024 * 1024,
                        RequestedFrames: 0);
                    await new PostgresVideoProcessingJobRepository(database)
                        .EnqueueAsync(identity, JobBudget, admission, token);
                }

                var adapter = new VideoExperimentalProxyAdapter(
                    database, ffmpeg, ffprobe, ffprobeHash, cache, ToolBudget);
                VideoWorkerCycleResult result =
                    await new VideoWorkerAttemptCoordinator(database)
                        .RunOneAsync(JobBudget, adapter, token);
                return result.Claimed || candidate is not null;
            }
            finally
            {
                // Never use File.Delete on a OneDrive original. Do not touch
                // a file that was already locally present before our request.
                if (ownedHydration && ownedPath is not null)
                {
                    try
                    {
                        using CancellationTokenSource cleanup =
                            new(TimeSpan.FromSeconds(15));
                        await oneDrive.RequestOnlineOnlyAsync(ownedPath, cleanup.Token);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException
                            or OperationCanceledException)
                    {
                        logger.LogWarning(
                            "An app-initiated video hydration could not be released; original left intact.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (active.IsCancellationRequested)
        {
            return true; // Archive pause or shutdown; lease fencing handles retry.
        }
        catch (Exception exception)
        {
            // No private source keys, paths or media data in operator logs.
            logger.LogWarning(
                "Video processing skipped or failed ({FailureType}); photos remain unaffected.",
                exception.GetType().Name);
            return false;
        }
        finally
        {
            lock (_activeLock)
            {
                _active = null;
            }
            active.Dispose();
        }
    }

    private string Required(string key)
    {
        string? configured = configuration["PhotoIdentity:VideoProcessing:" + key];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Approved video tools are not configured.");
        return configured;
    }

    private static async Task VerifyToolAsync(
        string path, string hash, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) ||
            (OperatingSystem.IsWindows() && path.StartsWith(@"\\", StringComparison.Ordinal)) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Approved video tool is unavailable.");
        VideoDerivativeCacheGuard.AssertNoLinks(path);
        await using FileStream input = new(path, FileMode.Open,
            FileAccess.Read, FileShare.Read);
        string observed = Convert.ToHexString(
            await SHA256.HashDataAsync(input, token)).ToLowerInvariant();
        if (!string.Equals(observed, hash, StringComparison.Ordinal))
            throw new InvalidOperationException("Approved video tool digest changed.");
    }

    private static string ResolveOwnedSourcePath(string root, string key)
    {
        if (!Path.IsPathFullyQualified(root) ||
            Path.IsPathRooted(key) || key.Contains(':'))
            throw new InvalidOperationException("Video source root is unsafe.");
        string normalized = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(normalized,
            key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(
            normalized.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Video source escaped its archive root.");
        VideoDerivativeCacheGuard.AssertNoLinks(Path.GetDirectoryName(path)!);
        return path;
    }

    private sealed record VideoCandidate(
        Guid RevisionId, Guid SourceId, string Root, string Key, long Size);

    private async Task<VideoCandidate?> FindPendingAsync(
        string toolSha256, CancellationToken token)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand query = connection.CreateCommand();
        query.CommandText = """
            SELECT revision.id, source.id, source.root_locator,
                   asset.source_key, revision.size_bytes
            FROM asset_revisions revision
            JOIN assets asset ON asset.id=revision.asset_id
            JOIN sources source ON source.id=asset.source_id
            JOIN video_revision_metadata metadata ON metadata.asset_revision_id=revision.id
            WHERE revision.media_kind='video' AND asset.media_kind='video'
              AND asset.deleted_at_utc IS NULL AND source.kind='local-folder'
              AND metadata.inspection_status='supported'
              AND metadata.color_transfer='bt709'
              AND metadata.video_codec IN ('h264','hevc')
              AND metadata.pixel_format IN ('yuv420p','yuv420p10le')
              AND (metadata.audio_codec IS NULL OR
                   metadata.audio_codec IN ('aac','pcm_s16le','pcm_s24le'))
              AND metadata.coded_width BETWEEN 16 AND 4096
              AND metadata.coded_height BETWEEN 16 AND 4096
              AND metadata.duration_seconds BETWEEN 0.2 AND 600
              AND metadata.display_rotation_degrees IN (0,90,-90,180,-180)
              AND revision.size_bytes BETWEEN 1 AND @max
              AND revision.id=(
                  SELECT latest.id FROM asset_revisions latest
                  WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                  ORDER BY latest.observed_at_utc DESC,latest.id DESC LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id=asset.source_id
                    AND excluded.source_key=asset.source_key)
              AND NOT EXISTS (
                  SELECT 1 FROM video_processing_jobs job
                  WHERE job.asset_revision_id=revision.id
                    AND job.derivative_kind='PlayableProxy'
                    AND job.preset_version=@preset AND job.tool_sha256=@tool
                    AND job.policy_version='wi0188-v1')
            ORDER BY metadata.duration_seconds, revision.id
            LIMIT 1;
            """;
        query.Parameters.AddWithValue("max", MaxSourceBytes);
        query.Parameters.AddWithValue("preset", VideoProxyCandidateProfile.Version);
        query.Parameters.AddWithValue("tool", toolSha256);
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        return new VideoCandidate(reader.GetGuid(0), reader.GetGuid(1),
            reader.GetString(2), reader.GetString(3), reader.GetInt64(4));
    }
}
