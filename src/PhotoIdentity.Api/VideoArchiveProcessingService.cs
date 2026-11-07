using System.Security.Cryptography;
using Npgsql;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Recognition;
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
    VideoManagedHydrationCoordinator managedHydration,
    VideoManagedHydrationRecovery hydrationRecovery,
    ILogger<VideoArchiveProcessingService> logger)
{
    private const long MaxSourceBytes = 256L * 1024 * 1024;
    private const long ReservationBytes = 512L * 1024 * 1024;
    private readonly object _activeLock = new();
    private CancellationTokenSource? _active;
    private string _lastResult = "not-run";
    private string _lastActivityResult = "not-run";
    private string _lastPosterResult = "not-run";
    private VideoAttemptFailureDiagnostic? _lastAttemptFailure;
    public string LastResult => Volatile.Read(ref _lastResult);
    // Last substantive outcome is not cleared by a later empty Archive scan.
    // This is in-memory operator feedback; durable job history is PostgreSQL.
    public string LastActivityResult => Volatile.Read(ref _lastActivityResult);
    public string LastPosterResult => Volatile.Read(ref _lastPosterResult);

    public static bool ShouldCreateAutomaticPoster(
        VideoProcessingPreferences options, VideoWorkerCycleResult result) =>
        options.AutomaticPostersForNewProxies && result.Published &&
        string.Equals(result.ReasonCode, "published", StringComparison.Ordinal);
    // Deliberately not cleared by a later no-admissible-job cycle.
    // In-memory only: restarting the API resets this diagnostic.
    public VideoAttemptFailureDiagnostic? LastAttemptFailure =>
        Volatile.Read(ref _lastAttemptFailure);
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
    public async Task<bool> AdvanceOnceAsync(
        ArchiveCoverageState coverage, CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows() || !preferences.Current.Enabled)
        {
            Volatile.Write(ref _lastResult, "disabled");
            return false;
        }

        CancellationTokenSource active;
        lock (_activeLock)
        {
            if (_active is not null) return false;
            _active = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            active = _active;
        }

        string preflightStage = "tool-configuration";
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
            preflightStage = "ffmpeg-approval";
            await VerifyToolAsync(ffmpeg, ffmpegHash, token);
            preflightStage = "ffprobe-approval";
            await VerifyToolAsync(ffprobe, ffprobeHash, token);

            preflightStage = "cache-validation";
            string? configuredCache = configuration["PhotoIdentity:VideoProcessing:CacheRoot"];
            string cache = configuredCache
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhotoIdentity", "video-worker-cache");
            // Never create/claim an arbitrary operator-selected directory:
            // it might be a OneDrive/archive root. Custom caches must
            // already be privately marked under the published runbook.
            // Only our fixed application-local default may be created.
            if (!Directory.Exists(cache) && configuredCache is null)
            {
                string parent = Path.GetDirectoryName(cache)!;
                Directory.CreateDirectory(parent);
                VideoDerivativeCacheGuard.AssertNoLinks(parent);
                Directory.CreateDirectory(cache);
                await File.WriteAllTextAsync(
                    Path.Combine(cache, VideoDerivativeFilePublisher.CacheMarker),
                    VideoDerivativeFilePublisher.CacheMarkerContents, token);
            }
            await VideoDerivativeCacheGuard.ValidateAsync(database, cache, token);
            preflightStage = "quota-configuration";
            VideoProcessingPreferences cyclePreferences = preferences.Current;
            VideoProxyRetentionLimits? publicationQuota =
                cyclePreferences.ResolvePublicationQuota(configuration);
            VideoProxyEncodingProfile quality =
                VideoProxyEncodingProfiles.Resolve(
                    preferences.Current.EncodingProfile);
            VideoProxyEncodingProfile fullRangeConversion =
                VideoProxyEncodingProfiles.ResolveForInput(
                    quality.Key, fullRange: true);
            preflightStage = "runtime";
            var control = new PostgresVideoProcessingControlRepository(database);
            VideoProcessingControlState state = await control.GetAsync(token);
            if (!state.Enabled || state.Paused)
                await control.EnableAsync(token);

            if ((await new PostgresVideoPhotoPriorityProbe(database)
                    .GetAsync(token)).PhotoWorkActive)
            {
                Volatile.Write(ref _lastResult, "photos-first");
                return false;
            }

            // Reconcile at most 16 pending managed-video release receipts.
            // Confirm online-only state, or retry the +u-only operation
            // for a still-local, unpinned, authorized prior request.
            // Never perform a new hydration or clear an existing pin.
            VideoManagedHydrationRecoverySummary recovery =
                await hydrationRecovery.ReconcileAsync(token);
            if (recovery.Reconciled > 0 || recovery.Failed > 0)
                logger.LogInformation(
                    "Managed video release receipt reconciliation: confirmed={Confirmed}; retained={Retained}; failed={Failed}.",
                    recovery.Reconciled, recovery.Retained, recovery.Failed);

            // Recover expired claims after a crashed process before making
            // another attempt; never reset completed derivative receipts.
            await new PostgresVideoProcessingJobRepository(database)
                .RecoverExpiredAsync(token);

            // A bounded operator-started recovery pass handles source-level
            // provisional video leases left by cancellation or a crash.
            await managedHydration.ReconcileUninspectedAsync(token);

            // A retryable queued job takes priority. Its original may have
            // become online-only since enqueue/restart; never claim it blindly
            // or hydrate it unless the separate opt-in is active.
            // Prefer all known local jobs (queued or pending) before any
            // online-only job, even when hydration has been explicitly enabled.
            VideoArchiveCandidate? candidate =
                await VideoArchiveCandidateSelector.SelectLocalFirstAsync(
                    preferences.Current.IncludeOnlineOnly,
                    include => FindOldestQueuedAsync(coverage, include,
                        quality.Version, fullRangeConversion.Version,
                        ffmpegHash, token),
                    include => FindPendingAsync(coverage, ffmpegHash, include,
                        quality.Version, fullRangeConversion.Version, token));
            // Register one new video per archive cycle only after the existing
            // photo work has drained. The metadata probe is the approved
            // bounded WI-0151 one; unsupported media is recorded, not encoded.
            VideoDiscoveryOutcome discovery = VideoDiscoveryOutcome.None;
            if (candidate is null)
            {
                discovery = await RegisterNextVideoAsync(
                    coverage, ffprobe, token);
                if (discovery != VideoDiscoveryOutcome.None)
                    candidate = await FindPendingAsync(
                        coverage, ffmpegHash,
                        preferences.Current.IncludeOnlineOnly,
                        quality.Version, fullRangeConversion.Version, token);
            }
            if (candidate is null)
            {
                // Release previously managed, no-longer-needed video
                // originals even when there is no new job this cycle.
                // A bounded, operator-initiated sweep closes the
                // completed-job and interrupted-attempt cleanup gap.
                int releaseRequests = await managedHydration
                    .ReleaseIdleBatchAsync(4, token);
                if (discovery != VideoDiscoveryOutcome.None)
                    Volatile.Write(ref _lastActivityResult,
                        discovery == VideoDiscoveryOutcome.Unsupported
                            ? "video-inspected-unsupported" : "video-inspected");
                Volatile.Write(ref _lastResult,
                    releaseRequests > 0
                        ? "video-managed-release-requested"
                        : discovery == VideoDiscoveryOutcome.Unsupported
                            ? "video-inspected-unsupported"
                        : discovery == VideoDiscoveryOutcome.Supported
                            ? "video-inspected"
                        : "no-eligible-video");
                return discovery != VideoDiscoveryOutcome.None || releaseRequests > 0;
            }
            // Both pending and queued selections bind the same admitted
            // metadata class; the worker independently proves staged bytes.
            VideoProxyEncodingProfile encodingProfile =
                VideoProxyEncodingProfiles.ResolveForInput(
                    quality.Key, candidate.IsFullRange);
            bool ownedHydration = false;
            string? ownedPath = null;
            try
            {
                Guid selectedJobId = candidate.JobId ?? Guid.Empty;
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
                        {
                            Volatile.Write(ref _lastResult,
                                "online-only-skipped");
                            return false;
                        }
                        // The source is a DB-vetted archive entry, never a
                        // caller path. No duplicate archive downloads.
                        ownedPath = ResolveOwnedSourcePath(
                            candidate.Root, candidate.Key);
                        // Reserve headroom on the SOURCE volume, which may
                        // differ from the private derivative cache volume.
                        VideoScratchCapacityGuard.EnsureCapacity(
                            candidate.Root, candidate.Size);
                        if (oneDrive.GetSyncClientAvailability() ==
                            OneDriveSyncClientAvailability.Unavailable)
                            throw new InvalidOperationException(
                                "OneDrive is unavailable for video hydration.");
                        // Use the same durable revision receipt and capacity/
                        // concurrent-operation gate as photo hydration. No
                        // video request may bypass configured storage limits.
                        using CancellationTokenSource hydrationTimeout =
                            CancellationTokenSource.CreateLinkedTokenSource(token);
                        hydrationTimeout.CancelAfter(TimeSpan.FromMinutes(5));
                        await managedHydration.RequestForSelectedAsync(
                            candidate, ownedPath, hydrationTimeout.Token);
                        ownedHydration = true;
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
                    else
                    {
                        // If a prior authorized Archive attempt hydrated
                        // this source, recover its durable ownership after a
                        // pause, worker crash or queued derivative retry.
                        // Ordinary pre-existing local videos have no receipt
                        // and are NEVER made app-owned by this lookup.
                        ownedPath = ResolveOwnedSourcePath(
                            candidate.Root, candidate.Key);
                        ownedHydration = await managedHydration
                            .ResumeIfManagedLocalAsync(
                                candidate, ownedPath, token);
                    }

                    string local = source.GetLocalPath(reference);
                    if (new FileInfo(local).Length != candidate.Size)
                        return false;

                    var identity = new VideoDerivativeJobIdentity(
                        AssetRevisionId.From(candidate.RevisionId),
                        VideoDerivativeKind.PlayableProxy, "wi0188-v1",
                        encodingProfile.Version, ffmpegHash);
                    var admission = new VideoProcessingCandidate(
                        IsCurrentRevision: true, IsExcluded: false,
                        IsLocallyAvailable: true, HasSupportedInspection: true,
                        SourceBytes: candidate.Size,
                        ExpectedTemporaryBytes: candidate.Size + 256L * 1024 * 1024,
                        RequestedFrames: 0);
                    if (candidate.JobId is null)
                        selectedJobId = await new PostgresVideoProcessingJobRepository(database)
                            .EnqueueAsync(identity, JobBudget, admission, token);
                }

                var adapter = new VideoExperimentalProxyAdapter(
                    database, ffmpeg, ffprobe, ffprobeHash, cache, ToolBudget,
                    publicationQuota, encodingProfile);
                VideoWorkerCycleResult result =
                    await new VideoWorkerAttemptCoordinator(database)
                        .RunOneAsync(JobBudget, adapter, token,
                            selectedJobId: selectedJobId,
                            requireArchiveCoverage: true);
                Volatile.Write(ref _lastResult, result.ReasonCode);
                Volatile.Write(ref _lastActivityResult, result.ReasonCode);
                if (candidate is { } publishedCandidate &&
                    ShouldCreateAutomaticPoster(cyclePreferences, result))
                {
                    try
                    {
                        string posterRoot = configuration["PhotoIdentity:VideoPosterRoot"] ??
                            Path.Combine(Environment.GetFolderPath(
                                Environment.SpecialFolder.LocalApplicationData),
                                "PhotoIdentity", "video-derivatives");
                        // Newly arrived photo work always takes priority
                        // over this optional post-publication enhancement.
                        if ((await new PostgresVideoPhotoPriorityProbe(database)
                                .GetAsync(token)).PhotoWorkActive)
                        {
                            Volatile.Write(ref _lastPosterResult,
                                "skipped-photo-priority");
                        }
                        else
                        {
                            bool created = await VideoProxyPosterCommandRunner
                                .TryCreateForPublishedProxyAsync(
                                    database, AssetRevisionId.From(publishedCandidate.RevisionId),
                                    cache, posterRoot, ffmpeg, ffmpegHash,
                                    ffprobe, ffprobeHash, token);
                            Volatile.Write(ref _lastPosterResult,
                                created ? "created" : "skipped");
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        // The playable MP4 is already durably published.
                        // Poster failure cannot downgrade, revoke or retry that
                        // video job. Never log source paths or tool stderr.
                        Volatile.Write(ref _lastPosterResult, "failed");
                        logger.LogWarning(
                            "Optional poster creation failed after video proxy publication.");
                    }
                }
                if (result.Failure is not null)
                    Volatile.Write(ref _lastAttemptFailure,
                        VideoAttemptFailureDiagnostics.Sanitize(result.Failure));
                return result.Claimed || candidate is not null ||
                    discovery != VideoDiscoveryOutcome.None;
            }
            finally
            {
                // A video worker may not clear an app/user pin simply because
                // its attempt completed. Check current source eligibility and
                // dependent jobs through a fresh database read, as well as
                // current Files On-Demand state and photo priority.
                //
                // New video hydration does not +p. If the user pins the
                // original meanwhile (or a legacy app +p receipt exists),
                // its pin is never cleared by video cleanup.
                // Pre-existing user-local originals remain unowned.
                if (ownedHydration && ownedPath is not null)
                {
                    try
                    {
                        using CancellationTokenSource cleanup =
                            new(TimeSpan.FromSeconds(15));
                        // Retain persistent ownership across Archive cycles
                        // and process restarts, even if the video job fails.
                        await managedHydration.TouchAsync(
                            candidate.RevisionId, cleanup.Token);
                        var availability = oneDrive.GetState(ownedPath);
                        (bool current, bool dependents) =
                            await ReadVideoReleaseEvidenceAsync(
                                database, candidate.RevisionId, cleanup.Token);
                        bool photoWork = (await new PostgresVideoPhotoPriorityProbe(
                                database).GetAsync(cleanup.Token)).PhotoWorkActive;
                        string decision = VideoHydrationReleaseGuard.Evaluate(
                            ownedHydration, availability, current,
                            dependents, photoWork);
                        if (decision == VideoHydrationReleaseGuard.ReleaseAllowed)
                        {
                            // Never clear +p. Transient, unpinned app
                            // hydrations are offered back with +u only.
                            // The committed release admission is serialized
                            // with new queued/restarted derivative jobs.
                            await managedHydration.ReleaseIfIdleAsync(
                                candidate, ownedPath, cleanup.Token);
                        }
                        else
                        {
                            logger.LogInformation(
                                "Managed video source retained under WI-0207 safety guard: {ReasonCode}.",
                                decision);
                        }
                    }
                    catch (Exception)
                    {
                        // Do not undo successful playable MP4 publication and
                        // never log private source paths or platform errors.
                        logger.LogWarning(
                            "Managed video source release check failed; original retained.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (active.IsCancellationRequested)
        {
            Volatile.Write(ref _lastResult, "paused");
            Volatile.Write(ref _lastActivityResult, "paused");
            return true; // Archive pause or shutdown; lease fencing handles retry.
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref _lastResult, "source-timeout");
            Volatile.Write(ref _lastActivityResult, "source-timeout");
            return false;
        }
        catch (Exception exception)
        {
            // Fixed, path-free diagnostic codes: no source names, keys or
            // native exception messages are returned to clients or logs.
            string code = ClassifyFailure(exception, preflightStage);
            Volatile.Write(ref _lastResult, code);
            Volatile.Write(ref _lastActivityResult, code);
            logger.LogWarning("Video processing ended with {Code}.", code);
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

    // Only an allowlisted stage and exception type affect public/logged codes.
    // Never return exception.Message, executable paths, hashes, private source keys
    // or tool output. The same fail-closed runtime checks remain authoritative.
    internal static string ClassifyFailure(Exception exception, string preflightStage)
    {
        if ((preflightStage is "tool-configuration" or "ffmpeg-approval"
                or "ffprobe-approval" or "cache-validation"
                or "quota-configuration") &&
            (exception is InvalidOperationException or ArgumentException
                or IOException or UnauthorizedAccessException))
        {
            return preflightStage switch
            {
                "tool-configuration" => "video-tool-configuration-rejected",
                "ffmpeg-approval" => "video-ffmpeg-approval-rejected",
                "ffprobe-approval" => "video-ffprobe-approval-rejected",
                "cache-validation" => "video-cache-validation-rejected",
                "quota-configuration" => "video-proxy-quota-config-rejected",
                _ => "video-safety-or-configuration-rejected"
            };
        }

        return exception switch
        {
            UnauthorizedAccessException => "filesystem-access-refused",
            IOException => "video-io-failure",
            InvalidDataException => "invalid-video-evidence",
            InvalidOperationException => "video-safety-or-configuration-rejected",
            _ => "video-attempt-failed"
        };
    }

    /// <summary>
    /// One fresh read of the current revision/exclusion fence and all
    /// queued/running derivative jobs for this revision. No original
    /// bytes or queue state are changed. Any query failure defers release.
    /// </summary>
    private static async Task<(bool Current, bool Dependents)>
        ReadVideoReleaseEvidenceAsync(
            PostgresCatalogueDatabase database,
            Guid revisionId, CancellationToken token)
    {
        await using NpgsqlConnection conn =
            await database.OpenConnectionAsync(token);
        await using NpgsqlCommand query = conn.CreateCommand();
        query.CommandText = """
            SELECT
              EXISTS (
                SELECT 1 FROM asset_revisions revision
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN sources source ON source.id=asset.source_id
                WHERE revision.id=@revision
                  AND revision.media_kind='video'
                  AND asset.media_kind='video'
                  AND asset.deleted_at_utc IS NULL
                  AND source.kind='local-folder'
                  AND revision.id=(
                    SELECT latest.id FROM asset_revisions latest
                    WHERE latest.asset_id=asset.id
                      AND latest.media_kind='video'
                    ORDER BY latest.observed_at_utc DESC,latest.id DESC
                    LIMIT 1)
                  AND NOT EXISTS (
                    SELECT 1 FROM source_copy_exclusions excluded
                    WHERE excluded.source_id=asset.source_id
                      AND excluded.source_key=asset.source_key)
              ),
              EXISTS (
                SELECT 1 FROM video_processing_jobs job
                WHERE job.asset_revision_id=@revision
                  AND job.status IN ('queued','running')
                  AND job.cancellation_requested_at_utc IS NULL
              );
            """;
        query.Parameters.AddWithValue("revision", revisionId);
        await using NpgsqlDataReader reader =
            await query.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException(
                "Video release evidence was not returned.");
        return (reader.GetBoolean(0), reader.GetBoolean(1));
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

    /// <summary>
    /// Incrementally discover one not-yet-inspected MOV/MP4 from the operator's
    /// existing included archive folders. Local files first, online-only only
    /// with a separate explicit opt-in. Never touch photo assets or source
    /// copies outside configured coverage.
    /// </summary>
    private enum VideoDiscoveryOutcome { None, Supported, Unsupported }

    /// <summary>
    /// One bounded local-first video inspection. A never-inspected online-only
    /// video enters the same Archive cycle only with the separate opt-in,
    /// durable provisional SOURCE ownership, and shared hydration admission.
    /// Do not create a guessed revision or content hash from metadata alone.
    /// </summary>
    private async Task<VideoDiscoveryOutcome> RegisterNextVideoAsync(
        ArchiveCoverageState coverage, string ffprobe, CancellationToken token)
    {
        ArchiveCatalogueSource sourceState =
            await new PostgresCatalogueSourceRepository(database)
                .GetOrCreateLocalFolderSourceAsync(
                    coverage.Source.RootLocator, DateTimeOffset.UtcNow, token);
        if (sourceState.SourceId != coverage.Source.SourceId)
            throw new InvalidOperationException("Video archive identity differs.");
        var videos = new OneDriveVideoAssetSource(
            sourceState.SourceId, sourceState.RootLocator);

        HashSet<string> inspected = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase);
        await using (NpgsqlConnection db = await database.OpenConnectionAsync(token))
        await using (NpgsqlCommand known = db.CreateCommand())
        {
            known.CommandText = """
                SELECT asset.source_key FROM assets asset
                JOIN asset_revisions revision ON revision.asset_id=asset.id
                JOIN video_revision_metadata metadata
                  ON metadata.asset_revision_id=revision.id
                WHERE asset.source_id=@source AND asset.media_kind='video'
                  AND asset.deleted_at_utc IS NULL
                  AND revision.media_kind='video';
                """;
            known.Parameters.AddWithValue("source", sourceState.SourceId.Value);
            await using NpgsqlDataReader reader = await known.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                inspected.Add(reader.GetString(0));
        }
        await using (NpgsqlConnection db = await database.OpenConnectionAsync(token))
        await using (NpgsqlCommand exclusions = db.CreateCommand())
        {
            exclusions.CommandText = """
                SELECT source_key FROM source_copy_exclusions WHERE source_id=@source;
                """;
            exclusions.Parameters.AddWithValue("source", sourceState.SourceId.Value);
            await using NpgsqlDataReader reader =
                await exclusions.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                excluded.Add(reader.GetString(0));
        }

        SourceAsset? localCandidate = null;
        SourceAsset? onlineCandidate = null;
        foreach (string folder in coverage.IncludedFolders)
        {
            try
            {
                await foreach (SourceAsset video in videos.EnumerateAsync(
                    new SourceScanOptions(folder), token))
                {
                    if (!VideoFirstInspectionAdmission.CanDiscover(
                            video,
                            inspected.Contains(video.Reference.ItemKey),
                            excluded.Contains(video.Reference.ItemKey),
                            preferences.Current.IncludeOnlineOnly))
                        continue;
                    if (video.Availability == AssetAvailability.Local)
                    {
                        localCandidate ??= video;
                        break;
                    }
                    if (preferences.Current.IncludeOnlineOnly &&
                        video.Availability == AssetAvailability.OnlineOnly)
                        onlineCandidate ??= video;
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Missing included folders never authorize unrelated sources.
            }
            if (localCandidate is not null) break;
        }

        SourceAsset? selected = localCandidate ?? onlineCandidate;
        if (selected is null) return VideoDiscoveryOutcome.None;

        string local = ResolveOwnedSourcePath(
            sourceState.RootLocator, selected.Reference.ItemKey);
        var provisionalRepository = new PostgresVideoProvisionalRepository(database);
        ArchiveSourceObservationSnapshot? provisional = null;
        bool promoted = false;
        try
        {
            AssetAvailability availability =
                await videos.GetAvailabilityAsync(selected.Reference, token);
            if (availability == AssetAvailability.OnlineOnly)
            {
                if (!preferences.Current.IncludeOnlineOnly)
                    return VideoDiscoveryOutcome.None;
                // An active source lease is durable before Cloud Files starts.
                provisional = await provisionalRepository.ObserveAsync(
                    sourceState, selected, DateTimeOffset.UtcNow, token);
                if (oneDrive.GetSyncClientAvailability() ==
                    OneDriveSyncClientAvailability.Unavailable)
                    throw new InvalidOperationException(
                        "OneDrive is unavailable for video inspection.");
                VideoScratchCapacityGuard.EnsureCapacity(
                    sourceState.RootLocator, selected.SizeBytes);
                using CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                Volatile.Write(ref _lastResult, "video-inspection-hydrating");
                await managedHydration.RequestForUninspectedAsync(
                    provisional, local, timeout.Token);
                while (await videos.GetAvailabilityAsync(
                        selected.Reference, timeout.Token) != AssetAvailability.Local)
                {
                    if (!preferences.Current.IncludeOnlineOnly)
                        throw new OperationCanceledException(timeout.Token);
                    if ((await new PostgresVideoPhotoPriorityProbe(database)
                            .GetAsync(timeout.Token)).PhotoWorkActive)
                        throw new InvalidOperationException(
                            "Photo work preempted online-only video inspection.");
                    await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
                }
            }
            else if (availability == AssetAvailability.Local)
            {
                // A previous authorized hydrate may have completed before a
                // process restart. Reuse ONLY an exact active owned receipt,
                // never claim a pre-existing local/user original.
                ProvisionalVideoHydration? owned =
                    await provisionalRepository.GetActiveSourceAsync(
                        sourceState.SourceId, selected.Reference.ItemKey, token);
                if (owned is { ReleaseRequested: true })
                    return VideoDiscoveryOutcome.None;
                provisional = owned?.Observation;
            }
            else
            {
                return VideoDiscoveryOutcome.None;
            }

            if (await videos.GetAvailabilityAsync(
                    selected.Reference, token) != AssetAvailability.Local ||
                !VideoIngestCommandRunner.MatchesObservation(
                    selected, new FileInfo(local)))
                return VideoDiscoveryOutcome.None;

            // Hash the local bytes before registering a real revision;
            // a failed FFprobe can still be durably classified unsupported
            // without causing the same original to download forever.
            byte[] digest;
            await using (Stream original = OneDriveVideoNoRecallReader.OpenRead(local))
                digest = await SHA256.HashDataAsync(original, token);

            if (!VideoIngestCommandRunner.MatchesObservation(
                    selected, new FileInfo(local)) ||
                await videos.GetAvailabilityAsync(
                    selected.Reference, token) != AssetAvailability.Local)
                return VideoDiscoveryOutcome.None;
            var verified = new VerifiedLocalVideo(sourceState,
                new SourceAsset(selected.Reference, selected.RelativePath,
                    selected.MediaType, selected.SizeBytes,
                    selected.LastWriteTimeUtc, AssetAvailability.Local),
                new Sha256Digest(Convert.ToHexString(digest).ToLowerInvariant()),
                DateTimeOffset.UtcNow);
            RegisteredVideoRevision revision =
                await new PostgresVideoAssetRepository(database)
                    .RegisterVerifiedAsync(verified, token);

            VideoRevisionInspection inspection;
            try
            {
                var probe = new VideoPilotMediaTools(
                    ffprobe, TimeSpan.FromSeconds(20));
                VideoPilotMetadata media = await probe.ProbeAsync(local, token);
                inspection = VideoIngestCommandRunner.BuildInspection(
                    revision.RevisionId, media, Required("FfprobeSha256"),
                    DateTimeOffset.UtcNow);
            }
            catch (Exception e) when (e is IOException or InvalidDataException
                or TimeoutException or System.Text.Json.JsonException)
            {
                inspection = new VideoRevisionInspection(
                    RevisionId: revision.RevisionId,
                    InspectionStatus: "unsupported",
                    UnsupportedReasonCode: "probe_failed",
                    ContainerName: null, VideoCodec: null, VideoProfile: null,
                    AudioCodec: null, PixelFormat: null, ColorTransfer: null,
                    CodedWidth: null, CodedHeight: null,
                    DisplayRotationDegrees: null, DurationSeconds: null,
                    AverageFrameRate: null, CaptureAtUtc: null,
                    CaptureTimeProvenance: null, CameraMake: null,
                    CameraModel: null, GpsLatitude: null, GpsLongitude: null,
                    InspectedAtUtc: DateTimeOffset.UtcNow,
                    ToolSha256: Required("FfprobeSha256"),
                    InspectionProfileId: VideoIngestCommandRunner.InspectionProfile);
            }
            await new PostgresVideoEvidenceRepository(database)
                .SaveInspectionAsync(inspection, token);

            if (provisional is not null)
            {
                promoted = await managedHydration.PromoteInspectedSourceAsync(
                    provisional.AssetId, revision.RevisionId, token);
                if (!promoted)
                    throw new InvalidOperationException(
                        "Provisional video ownership promotion was refused.");
            }

            bool playable = CanEncodeInspectedVideo(
                inspection, selected.SizeBytes);
            if (!playable && promoted)
            {
                // Unsupported/HDR media have no derivative worker to trigger
                // normal cleanup. Release the newly revision-owned bytes now.
                await managedHydration.ReleaseIfIdleAsync(
                    new VideoArchiveCandidate(
                        revision.RevisionId.Value, sourceState.SourceId.Value,
                        sourceState.RootLocator, selected.Reference.ItemKey,
                        selected.SizeBytes), local, token);
            }
            return playable
                ? VideoDiscoveryOutcome.Supported
                : VideoDiscoveryOutcome.Unsupported;
        }
        finally
        {
            if (provisional is not null && !promoted)
            {
                // Cancellation/FFprobe/source-change must retain a durable
                // receipt; release only if the original is still an exact,
                // unpinned, pre-revision Photo-Identity-owned source.
                try
                {
                    using CancellationTokenSource cleanup =
                        new(TimeSpan.FromSeconds(15));
                    await managedHydration.ReleaseUninspectedAsync(
                        provisional, local, cleanup.Token);
                }
                catch (Exception)
                {
                    logger.LogWarning(
                        "Provisional video source retained for safe Archive recovery.");
                }
            }
        }
    }

    private static bool CanEncodeInspectedVideo(
        VideoRevisionInspection inspection, long sourceBytes) =>
        inspection.InspectionStatus == "supported" &&
        inspection.ColorTransfer == "bt709" &&
        inspection.VideoCodec is ("h264" or "hevc") &&
        inspection.PixelFormat is ("yuv420p" or "yuv420p10le" or "yuvj420p") &&
        inspection.AudioCodec is (null or "aac" or "pcm_s16le" or "pcm_s24le") &&
        inspection.CodedWidth is >= 16 and <= 4096 &&
        inspection.CodedHeight is >= 16 and <= 4096 &&
        inspection.DisplayRotationDegrees is (0 or 90 or -90 or 180 or -180) &&
        inspection.DurationSeconds is >= 0.2m and <= 600m &&
        sourceBytes is >= 1 and <= MaxSourceBytes;

    /// <summary>
    /// Select an eligible queued job only within the current archive coverage.
    /// Inspect all queued candidates in order: an older OneDrive placeholder
    /// cannot block an already-local queued video.
    /// </summary>
    private async Task<VideoArchiveCandidate?> FindOldestQueuedAsync(
        ArchiveCoverageState coverage, bool includeOnlineOnly,
        string sdrPresetVersion, string fullRangePresetVersion,
        string toolSha256, CancellationToken token)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand query = connection.CreateCommand();
        query.CommandText = """
            SELECT job.id, revision.id, source.id, source.root_locator,
                   asset.source_key, revision.size_bytes,
                   (metadata.pixel_format='yuvj420p')
            FROM video_processing_jobs job
            JOIN asset_revisions revision ON revision.id=job.asset_revision_id
            JOIN assets asset ON asset.id=revision.asset_id
            JOIN sources source ON source.id=asset.source_id
            JOIN video_revision_metadata metadata ON metadata.asset_revision_id=revision.id
            WHERE job.status='queued'
              AND job.derivative_kind='PlayableProxy'
              AND job.policy_version='wi0188-v1'
              AND job.preset_version=(CASE
                  WHEN metadata.pixel_format='yuvj420p' THEN @fullrange
                  ELSE @preset END) AND job.tool_sha256=@tool
              AND job.cancellation_requested_at_utc IS NULL
              AND job.attempt_count<3 AND job.available_at_utc<=now()
              AND revision.media_kind='video' AND asset.media_kind='video'
              AND asset.deleted_at_utc IS NULL
              AND source.kind='local-folder'
              AND revision.size_bytes=job.source_bytes
              AND revision.size_bytes BETWEEN 1 AND @max
              AND metadata.inspection_status='supported'
              AND metadata.color_transfer='bt709'
              AND metadata.video_codec IN ('h264','hevc')
              AND metadata.pixel_format IN ('yuv420p','yuv420p10le','yuvj420p')
              AND (metadata.audio_codec IS NULL OR
                   metadata.audio_codec IN ('aac','pcm_s16le','pcm_s24le'))
              AND metadata.coded_width BETWEEN 16 AND 4096
              AND metadata.coded_height BETWEEN 16 AND 4096
              AND metadata.duration_seconds BETWEEN 0.2 AND 600
              AND metadata.display_rotation_degrees IN (0,90,-90,180,-180)
              AND revision.id=(
                  SELECT latest.id FROM asset_revisions latest
                  WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                  ORDER BY latest.observed_at_utc DESC,latest.id DESC LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id=asset.source_id
                    AND excluded.source_key=asset.source_key)
            ORDER BY job.requested_at_utc, job.id;
            """;
        query.Parameters.AddWithValue("max", MaxSourceBytes);
        query.Parameters.AddWithValue("preset", sdrPresetVersion);
        query.Parameters.AddWithValue("fullrange", fullRangePresetVersion);
        query.Parameters.AddWithValue("tool", toolSha256);
        List<VideoArchiveCandidate> candidates = [];
        await using (NpgsqlDataReader reader = await query.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
                candidates.Add(new VideoArchiveCandidate(
                    reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                    reader.GetString(4), reader.GetInt64(5), reader.GetGuid(0),
                    reader.GetBoolean(6)));
        }
        return await VideoArchiveCandidateSelector.SelectAsync(
            coverage, candidates, includeOnlineOnly, ReadAvailabilityAsync, token);
    }

    private async Task<VideoArchiveCandidate?> FindPendingAsync(
        ArchiveCoverageState coverage, string toolSha256,
        bool includeOnlineOnly, string sdrPresetVersion,
        string fullRangePresetVersion, CancellationToken token)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand query = connection.CreateCommand();
        query.CommandText = """
            SELECT revision.id, source.id, source.root_locator,
                   asset.source_key, revision.size_bytes,
                   (metadata.pixel_format='yuvj420p')
            FROM asset_revisions revision
            JOIN assets asset ON asset.id=revision.asset_id
            JOIN sources source ON source.id=asset.source_id
            JOIN video_revision_metadata metadata ON metadata.asset_revision_id=revision.id
            WHERE revision.media_kind='video' AND asset.media_kind='video'
              AND asset.deleted_at_utc IS NULL AND source.kind='local-folder'
              AND metadata.inspection_status='supported'
              AND metadata.color_transfer='bt709'
              AND metadata.video_codec IN ('h264','hevc')
              AND metadata.pixel_format IN ('yuv420p','yuv420p10le','yuvj420p')
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
                    AND job.preset_version=(CASE
                  WHEN metadata.pixel_format='yuvj420p' THEN @fullrange
                  ELSE @preset END) AND job.tool_sha256=@tool
                    AND job.policy_version='wi0188-v1')
            ORDER BY metadata.duration_seconds, revision.id;
            """;
        query.Parameters.AddWithValue("max", MaxSourceBytes);
        query.Parameters.AddWithValue("preset", sdrPresetVersion);
        query.Parameters.AddWithValue("fullrange", fullRangePresetVersion);
        query.Parameters.AddWithValue("tool", toolSha256);
        List<VideoArchiveCandidate> candidates = [];
        await using (NpgsqlDataReader reader = await query.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
                candidates.Add(new VideoArchiveCandidate(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    reader.GetString(3), reader.GetInt64(4), null,
                    reader.GetBoolean(5)));
        }
        return await VideoArchiveCandidateSelector.SelectAsync(
            coverage, candidates, includeOnlineOnly, ReadAvailabilityAsync, token);
    }

    private static Task<AssetAvailability> ReadAvailabilityAsync(
        VideoArchiveCandidate candidate, CancellationToken token)
    {
        var source = new OneDriveVideoAssetSource(
            SourceId.From(candidate.SourceId), candidate.Root);
        return source.GetAvailabilityAsync(
            new SourceAssetReference(source.SourceId, candidate.Key), token);
    }
}

/// <summary>
/// Source-metadata-only first-inspection admission. No placeholder is opened,
/// unpinned, hydrated or registered by this eligibility check. Existing
/// video job/revision selectors apply their independent durable fences.
/// </summary>
public static class VideoFirstInspectionAdmission
{
    public static bool CanDiscover(
        SourceAsset source, bool alreadyInspected, bool excluded,
        bool includeOnlineOnly)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (alreadyInspected || excluded ||
            source.SizeBytes is < 1 or > 256L * 1024 * 1024 ||
            source.MediaType is not ("video/mp4" or "video/quicktime") ||
            Path.GetExtension(source.Reference.ItemKey).ToLowerInvariant()
                is not (".mov" or ".mp4"))
            return false;
        return source.Availability == AssetAvailability.Local ||
            (includeOnlineOnly &&
             source.Availability == AssetAvailability.OnlineOnly);
    }
}

/// <summary>A catalogue video revision and optional existing queued job.</summary>
public sealed record VideoArchiveCandidate(
    Guid RevisionId, Guid SourceId, string Root, string Key, long Size,
    Guid? JobId = null, bool IsFullRange = false);

/// <summary>
/// Shared local-first selection for the queued and newly inspected lanes.
/// A source must be in the currently selected included folders before even
/// querying local/OneDrive availability, and local always wins over online-only.
/// </summary>
public static class VideoArchiveCandidateSelector
{
    public static bool IsIncluded(
        ArchiveCoverageState coverage, VideoArchiveCandidate candidate)
    {
        if (coverage.Source.SourceId.Value != candidate.SourceId ||
            !string.Equals(coverage.Source.RootLocator, candidate.Root,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return false;

        try
        {
            return coverage.IncludedFolders.Any(folder =>
                ArchiveCoverage.Covers(folder, candidate.Key));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Queued online-only work must not jump ahead of a different local
    /// catalogue candidate simply because it has an older queue timestamp.
    /// </summary>
    public static async Task<VideoArchiveCandidate?> SelectLocalFirstAsync(
        bool includeOnlineOnly,
        Func<bool, Task<VideoArchiveCandidate?>> queued,
        Func<bool, Task<VideoArchiveCandidate?>> pending)
    {
        ArgumentNullException.ThrowIfNull(queued);
        ArgumentNullException.ThrowIfNull(pending);
        VideoArchiveCandidate? local = await queued(false) ?? await pending(false);
        if (local is not null || !includeOnlineOnly) return local;
        return await queued(true) ?? await pending(true);
    }

    public static async Task<VideoArchiveCandidate?> SelectAsync(
        ArchiveCoverageState coverage,
        IEnumerable<VideoArchiveCandidate> candidates,
        bool includeOnlineOnly,
        Func<VideoArchiveCandidate, CancellationToken, Task<AssetAvailability>> availability,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(availability);
        VideoArchiveCandidate? online = null;
        foreach (VideoArchiveCandidate candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            if (!IsIncluded(coverage, candidate)) continue;
            AssetAvailability state;
            try { state = await availability(candidate, token); }
            catch (Exception e) when (
                e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }
            if (state == AssetAvailability.Local) return candidate;
            if (includeOnlineOnly && state == AssetAvailability.OnlineOnly)
                online ??= candidate;
        }
        return online;
    }
}
