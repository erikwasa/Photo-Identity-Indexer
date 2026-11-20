using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

/// <summary>
/// One explicitly selected existing video revision, never a supplied path.
/// Default preview performs metadata-only reads and makes no queue/control
/// changes. --apply requires separate rights and private biometric-processing
/// acknowledgements, and never automatically queues another video.
/// </summary>
public sealed record VideoFaceSelectedOneShotOptions(
    string ConnectionEnv, string DatabaseName, Guid RevisionId,
    VideoFaceNativeProducerOptions Producer, int Frames,
    bool Apply, bool AcknowledgePrivateProcessing, bool AcknowledgeModelRights)
{
    public static VideoFaceSelectedOneShotOptions Parse(string[] args)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        bool apply = false, biometric = false, rights = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--apply" when !apply:
                    apply = true; continue;
                case "--acknowledge-private-face-processing" when !biometric:
                    biometric = true; continue;
                case "--acknowledge-model-rights" when !rights:
                    rights = true; continue;
            }

            string key = args[i];
            if (key is not (
                "--postgres-connection-env" or "--database" or "--revision-id" or
                "--scratch-root" or "--ffmpeg" or "--ffmpeg-sha256" or
                "--manifest-directory" or "--model-directory" or "--max-frames") ||
                i + 1 >= args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !fields.TryAdd(key, args[++i]))
                throw new ArgumentException(
                    "Unknown, duplicate or incomplete native video-face option.");
        }

        string Required(string key) =>
            fields.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException(
                    "Every selected native face-processing field must be explicit.");

        string env = Required("--postgres-connection-env");
        string db = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant) ||
            !Guid.TryParse(Required("--revision-id"), out Guid revision) ||
            revision == Guid.Empty ||
            !int.TryParse(Required("--max-frames"),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int frames) ||
            frames is < 1 or > VideoFaceEvidenceLeaseIdentity.MaximumFrames)
            throw new ArgumentException(
                "A local database, exact video revision UUID and 1–4 frames are required.");

        if (apply != (biometric && rights) || (!apply && (biometric || rights)))
            throw new ArgumentException(
                "Execution requires --apply plus BOTH private-processing and model-rights acknowledgements.");

        string Absolute(string key)
        {
            string value = Required(key);
            if (!Path.IsPathFullyQualified(value) ||
                (OperatingSystem.IsWindows() &&
                 value.StartsWith(@"\\", StringComparison.Ordinal)))
                throw new ArgumentException(
                    "Native face tools, models and scratch must have local absolute paths.");
            return Path.GetFullPath(value);
        }

        var producer = new VideoFaceNativeProducerOptions(
            Absolute("--scratch-root"), Absolute("--ffmpeg"),
            Required("--ffmpeg-sha256"),
            Absolute("--manifest-directory"), Absolute("--model-directory"));
        producer.Validate();
        return new(env, db, revision, producer, frames, apply, biometric, rights);
    }
}

/// <summary>
/// Manual-only real-source entry point. Before --apply it opens NO original
/// media, checks NO online-only availability and creates NO job. Apply can
/// enqueue and claim only the exact selected revision, with one temporary
/// source-attested lease, and disarms it in a cancellation-independent finally.
/// No hosted worker, automatic assignments, image persistence or Azure bridge.
/// </summary>
public static class VideoFaceSelectedOneShotCommandRunner
{
    public const long MaximumSourceBytes = 256L * 1024 * 1024;
    public const long MaximumScratchBytes = 320L * 1024 * 1024;
    private const long ScratchOverheadBytes = 64L * 1024 * 1024;
    private const int MaximumJobSeconds = 13 * 60;
    private static readonly TimeSpan AuthorizationWindow = TimeSpan.FromMinutes(15);
    public const string Protocol = "wi0155-selected-native-face-one-shot-v1";

    public static async Task<int> RunAsync(
        VideoFaceSelectedOneShotOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Native face processing requires Windows Job Object containment.");
        if (options.RevisionId == Guid.Empty ||
            options.Frames is < 1 or > VideoFaceEvidenceLeaseIdentity.MaximumFrames ||
            (options.Apply !=
                (options.AcknowledgePrivateProcessing && options.AcknowledgeModelRights)))
            throw new ArgumentException("Exact bounded single-video authorization is required.");

        options.Producer.Validate();
        // The existing ownership guard requires a dedicated, empty, marked
        // child of OS temp; no arbitrary directories can be deleted or reused.
        string scratch = VideoFrameEvidenceScratchGuard.Validate(
            options.Producer.ScratchRoot);
        VideoScratchCapacityGuard.EnsureCapacity(scratch, MaximumScratchBytes);
        await VideoScopedOneShotCommandRunner.VerifyExecutableAsync(
            options.Producer.FfmpegPath, options.Producer.FfmpegSha256, token);

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("A local PostgreSQL connection environment variable is required.");
        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Single-video face processing requires localhost PostgreSQL.");
        builder.Database = options.DatabaseName;
        await using PostgresCatalogueDatabase db = new(builder.ConnectionString);

        // Must not initialize/migrate production as a side effect of a pilot.
        // The operator upgrades the maintained catalogue separately.
        await using (NpgsqlConnection connection = await db.OpenConnectionAsync(token))
        await using (NpgsqlCommand version = connection.CreateCommand())
        {
            version.CommandText =
                "SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;";
            if (Convert.ToInt32(await version.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Selected native face processing requires the current independently migrated schema.");
        }

        // Metadata and canonical source authority only; do not inspect, open,
        // recall or stage original bytes during this preview.
        long sourceBytes;
        decimal duration;
        Guid sourceId;
        string sourceRoot, sourceKey;
        await using (NpgsqlConnection connection = await db.OpenConnectionAsync(token))
        await using (NpgsqlCommand selected = connection.CreateCommand())
        {
            selected.CommandText = """
                SELECT revision.size_bytes, metadata.duration_seconds,
                       source.id, source.root_locator, asset.source_key
                FROM asset_revisions revision
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN sources source ON source.id=asset.source_id
                JOIN video_revision_metadata metadata
                  ON metadata.asset_revision_id=revision.id
                WHERE revision.id=@revision AND revision.media_kind='video'
                  AND asset.media_kind='video' AND asset.deleted_at_utc IS NULL
                  AND source.kind='local-folder'
                  AND metadata.inspection_status='supported'
                  AND metadata.color_transfer='bt709'
                  AND metadata.coded_width BETWEEN 1 AND 3840
                  AND metadata.coded_height BETWEEN 1 AND 3840
                  AND metadata.duration_seconds BETWEEN 0.2 AND 3600
                  AND revision.size_bytes BETWEEN 1 AND @maximum
                  AND revision.id=(
                      SELECT latest.id FROM asset_revisions latest
                      WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                      ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                  AND NOT EXISTS (
                      SELECT 1 FROM source_copy_exclusions excluded
                      WHERE excluded.source_id=asset.source_id
                        AND excluded.source_key=asset.source_key)
                  AND NOT EXISTS (
                      SELECT 1 FROM video_face_evidence_batches existing
                      WHERE existing.asset_revision_id=revision.id);
                """;
            selected.Parameters.AddWithValue("revision", options.RevisionId);
            selected.Parameters.AddWithValue("maximum", MaximumSourceBytes);
            await using NpgsqlDataReader reader = await selected.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
            {
                Write(output, !options.Apply, false, false, "revision-not-eligible");
                return 1;
            }
            sourceBytes = reader.GetInt64(0);
            duration = reader.GetDecimal(1);
            sourceId = reader.GetGuid(2);
            sourceRoot = reader.GetString(3);
            sourceKey = reader.GetString(4);
        }

        VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
            new VideoFrameSamplingRequest(duration,
                MaximumFrames: options.Frames,
                MaximumReservedDecodeMilliseconds: 12000,
                ReservedDecodeMillisecondsPerFrame: 3000));
        if (plan.Samples.Count != options.Frames)
        {
            Write(output, !options.Apply, false, false, "sampling-incomplete");
            return 1;
        }

        VideoProcessingControlState control =
            await new PostgresVideoProcessingControlRepository(db).GetAsync(token);
        VideoPhotoPrioritySnapshot photo =
            await new PostgresVideoPhotoPriorityProbe(db).GetAsync(token);
        bool anyRunning;
        await using (NpgsqlConnection connection = await db.OpenConnectionAsync(token))
        await using (NpgsqlCommand running = connection.CreateCommand())
        {
            running.CommandText =
                "SELECT EXISTS(SELECT 1 FROM video_processing_jobs WHERE status='running');";
            anyRunning = await running.ExecuteScalarAsync(token) is true;
        }
        if (control.Enabled || !control.Paused ||
            control.SelectedJobId is not null || anyRunning || photo.PhotoWorkActive)
        {
            Write(output, !options.Apply, false, false, "operator-or-photo-busy");
            return 1;
        }

        if (!options.Apply)
        {
            Write(output, true, true, false, "preflight-only");
            return 0;
        }

        // Explicit private-media authorization, not a general worker switch.
        // Check local availability without requesting OneDrive hydration.
        bool local;
        try
        {
            if (!Path.IsPathFullyQualified(sourceRoot) ||
                sourceRoot.StartsWith(@"\\", StringComparison.Ordinal) ||
                VideoDerivativeCacheGuard.Overlaps(scratch, sourceRoot))
                throw new IOException("Untrusted source root or scratch overlap.");
            var source = new OneDriveVideoAssetSource(SourceId.From(sourceId), sourceRoot);
            var reference = new SourceAssetReference(source.SourceId, sourceKey);
            local = await source.GetAvailabilityAsync(reference, token) ==
                AssetAvailability.Local;
            if (local)
            {
                string localPath = source.GetLocalPath(reference);
                local = File.Exists(localPath) &&
                    (File.GetAttributes(localPath) & FileAttributes.ReparsePoint) == 0 &&
                    new FileInfo(localPath).Length == sourceBytes;
                if (local)
                    VideoDerivativeCacheGuard.AssertNoLinks(localPath);
            }
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            local = false;
        }
        if (!local)
        {
            Write(output, false, false, false, "source-not-local");
            return 1;
        }

        // Models are loaded and their digests verified inside the leased
        // producer. Do not initialize any model before a selected lease exists.
        foreach (string path in new[] {
            options.Producer.ManifestDirectory, options.Producer.ModelDirectory
        })
        {
            if (!Directory.Exists(path))
                throw new InvalidOperationException("The approved local face model directories are unavailable.");
            VideoDerivativeCacheGuard.AssertNoLinks(path);
        }

        var budget = new VideoProcessingBudget(
            Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
            MaximumSourceBytes: MaximumSourceBytes,
            MaximumTemporaryBytes: MaximumScratchBytes,
            MaximumExtractedFrames: VideoFaceEvidenceLeaseIdentity.MaximumFrames,
            MaximumJobSeconds: MaximumJobSeconds);
        var candidate = new VideoProcessingCandidate(
            IsCurrentRevision: true, IsExcluded: false, IsLocallyAvailable: true,
            HasSupportedInspection: true, SourceBytes: sourceBytes,
            ExpectedTemporaryBytes: sourceBytes + ScratchOverheadBytes,
            RequestedFrames: options.Frames);
        var identity = new VideoDerivativeJobIdentity(
            AssetRevisionId.From(options.RevisionId), VideoDerivativeKind.FaceEvidence,
            VideoFaceEvidenceLeaseIdentity.PolicyVersion,
            VideoFaceEvidenceLeaseIdentity.PresetVersion, options.Producer.FfmpegSha256);

        Guid job = await new PostgresVideoProcessingJobRepository(db)
            .EnqueueAsync(identity, budget, candidate, token);
        var scoped = new PostgresVideoScopedAttemptControl(db);
        VideoScopedAttemptGrant? grant = await scoped.ArmAsync(
            job, options.Producer.FfmpegSha256,
            VideoFaceEvidenceLeaseIdentity.PresetVersion, AuthorizationWindow,
            token, derivativeKind: VideoFaceEvidenceLeaseIdentity.Kind);
        if (grant is null)
        {
            Write(output, false, false, false, "selected-job-not-queued");
            return 1;
        }

        long started = Stopwatch.GetTimestamp();
        VideoWorkerCycleResult? cycle = null;
        try
        {
            var producer = new VideoFaceNativeEvidenceProducer(db, options.Producer);
            var adapter = new VideoFaceEvidenceWorkerAdapter(db, producer);
            cycle = await new VideoWorkerAttemptCoordinator(db).RunOneAsync(
                budget, adapter, token, selectedJobId: job);
        }
        finally
        {
            // This cleanup has its OWN deadline, independent of cancellation
            // of the source processing attempt.
            using CancellationTokenSource cleanup =
                new(TimeSpan.FromSeconds(15));
            bool disarmed = await scoped.DisarmAsync(grant, cleanup.Token);
            if (!disarmed)
            {
                VideoProcessingControlState current =
                    await new PostgresVideoProcessingControlRepository(db)
                        .GetAsync(cleanup.Token);
                if (current.Enabled || current.SelectedJobId is not null)
                    throw new InvalidOperationException(
                        "Selected native face grant could not be safely disarmed.");
            }
        }

        bool published = cycle?.Published == true;
        int? detectedFaces = null;
        if (published)
        {
            // Read the durable, current batch after disarming. A zero count
            // means only that no faces were detected in the sampled frames.
            var receipt = await new PostgresVideoFaceReviewRepository(db, TimeProvider.System)
                .GetCurrentAsync(AssetRevisionId.From(options.RevisionId), token);
            detectedFaces = receipt?.Observations.Count;
        }
        string reason = published
            ? detectedFaces == 0 ? "no-faces-in-sampled-frames" : "published"
            : cycle?.Failure?.FailureClass == "no-faces-found" ? "no-faces-found"
            : cycle?.ReasonCode ?? "execution-unavailable";
        Write(output, false, cycle?.Claimed == true, published,
            reason, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            detectedFaces);
        return published ? 0 : 1;
    }

    private static void Write(
        TextWriter output, bool dryRun, bool admitted,
        bool published, string reasonCode, long? wallMilliseconds = null,
        int? sampledFaceObservations = null) =>
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = Protocol,
            dryRun, admitted, published, reasonCode,
            sampledFaceObservations,
            maximumFrames = VideoFaceEvidenceLeaseIdentity.MaximumFrames,
            maximumConcurrentJobs = 1,
            originalsMayHaveBeenOpened = !dryRun && admitted,
            oneDriveHydrationRequested = false,
            cloudInferenceRequested = false,
            automaticVideoWorkersEnabled = false,
            identityAssignmentsMade = 0,
            executionWallMilliseconds = wallMilliseconds
        }));
}
