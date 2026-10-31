using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Explicit operator-only, at-most-two-frame job. The selected identity is
/// an already published local PlayableProxy UUID, not a source path or video
/// filename. --apply plus BOTH acknowledgements is required to enqueue and
/// process it; the default dry run has no database writes or media access.
/// </summary>
public sealed record VideoFrameSelectedOneShotOptions(
    string ConnectionEnv, string DatabaseName, Guid ProxyJobId,
    VideoRegisteredProxyFrameProducerOptions Producer, int Frames,
    bool Apply, bool AcknowledgeV38, bool AcknowledgeDisclosure,
    IReadOnlyList<long>? SceneCutsMs = null)
{
    public static VideoFrameSelectedOneShotOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool apply = false, v38 = false, disclosure = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--apply" when !apply:
                    apply = true; continue;
                case "--acknowledge-isolated-v38-rehearsal" when !v38:
                    v38 = true; continue;
                case "--acknowledge-frame-disclosure" when !disclosure:
                    disclosure = true; continue;
            }
            string key = args[i];
            if (key is not (
                "--postgres-connection-env" or "--database" or
                "--proxy-job-id" or "--cache-root" or "--scratch-root" or
                "--ffmpeg" or "--ffmpeg-sha256" or
                "--clip-model" or "--clip-model-sha256" or "--clip-vocab" or "--clip-merges" or
                "--ollama-sha256" or
                "--ollama-model" or "--max-frames" or
                "--scene-boundaries-ms") ||
                i+1 >= args.Length || args[i+1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(key, args[++i]))
                throw new ArgumentException(
                    "Unknown, repeated or incomplete selected frame job option.");
        }
        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException("Every selected frame job input must be explicit.");
        string env = Required("--postgres-connection-env");
        string database = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(database, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant) ||
            !Guid.TryParse(Required("--proxy-job-id"), out Guid id) || id == Guid.Empty ||
            !int.TryParse(Required("--max-frames"), out int frames) || frames is < 1 or > 2)
            throw new ArgumentException(
                "Selected frames require a safe local database, proxy UUID and 1-2 frames.");
        if (apply != (v38 && disclosure) || (!apply && (v38 || disclosure)))
            throw new ArgumentException(
                "Execution requires --apply and both v38-rehearsal and frame-disclosure acknowledgements.");

        string Absolute(string name)
        {
            string path = Required(name);
            if (!Path.IsPathFullyQualified(path) ||
                (OperatingSystem.IsWindows() && path.StartsWith(@"\\", StringComparison.Ordinal)))
                throw new ArgumentException(
                    "Selected frame job paths must be absolute and local.");
            return Path.GetFullPath(path);
        }
        IReadOnlyList<long>? sceneCuts = null;
        if (values.TryGetValue("--scene-boundaries-ms", out string? annotations))
        {
            string[] parts = annotations.Split(',');
            if (parts.Length is < 1 or > VideoFrameSceneSamplingIdentity.MaximumCuts)
                throw new ArgumentException(
                    "Scene sampling requires one to six bounded cuts.");
            long[] cuts = new long[parts.Length];
            long previous = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length is < 1 or > 7 ||
                    parts[i][0] == '0' ||
                    !long.TryParse(parts[i], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out long cut) ||
                    cut <= previous || cut >= 3_600_000 ||
                    parts[i] != cut.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))
                    throw new ArgumentException(
                        "Scene cuts must be canonical increasing positive milliseconds.");
                cuts[i] = cut;
                previous = cut;
            }
            sceneCuts = cuts;
        }
        var producer = new VideoRegisteredProxyFrameProducerOptions(
            Absolute("--cache-root"), Absolute("--scratch-root"),
            Absolute("--ffmpeg"), Required("--ffmpeg-sha256"),
            Absolute("--clip-model"), Absolute("--clip-vocab"),
            Absolute("--clip-merges"),
            values.GetValueOrDefault("--ollama-model") ??
                OllamaVisionCaptionClient.DefaultModel,
            Required("--clip-model-sha256"), Required("--ollama-sha256"),
            SelectedProxyJobId: id);
        producer.Validate();
        return new(env, database, id, producer, frames, apply, v38, disclosure,
            sceneCuts);
    }
}

/// <summary>
/// No hosted worker, enqueue scan, original source read or implicit cloud
/// fallback. Dry-run only reads current catalogue metadata and preflight
/// configuration. Apply creates/claims ONE idempotent FrameEvidence job,
/// scopes the v37 control generation to its UUID, and always disarms.
/// </summary>
public static class VideoFrameSelectedOneShotCommandRunner
{
    private const long MaxSource = 1024L * 1024 * 1024;
    private const long MaxScratch = 320L * 1024 * 1024;
    private const int MaxSeconds = 13 * 60;

    public static async Task<int> RunAsync(
        VideoFrameSelectedOneShotOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Selected frame processing requires Windows Job Object containment.");
        options.Producer.Validate();
        string root = VideoFrameEvidenceScratchGuard.Validate(options.Producer.ScratchRoot);

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("A local PostgreSQL environment variable is required.");
        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Selected frames require localhost PostgreSQL.");
        builder.Database = options.DatabaseName;
        await using PostgresCatalogueDatabase db = new(builder.ConnectionString);

        // Check migration history BEFORE any repository call that could
        // trigger lazy initialization. Do not self-migrate the maintained DB.
        await using (NpgsqlConnection connection = await db.OpenConnectionAsync(token))
        await using (NpgsqlCommand schema = connection.CreateCommand())
        {
            schema.CommandText =
                "SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;";
            if (Convert.ToInt32(await schema.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "An independently rehearsed current PostgreSQL schema is required.");
        }

        string cache = await VideoDerivativeCacheGuard.ValidateAsync(
            db, options.Producer.CacheRoot, token);
        if (VideoDerivativeCacheGuard.Overlaps(root, cache))
            throw new InvalidOperationException(
                "Frame scratch and registered proxy cache must be separate.");
        VideoScratchCapacityGuard.EnsureCapacity(root, MaxScratch);
        await VideoScopedOneShotCommandRunner.VerifyExecutableAsync(
            options.Producer.FfmpegPath, options.Producer.FfmpegSha256, token);

        // No original path, original bytes or OneDrive request is consulted.
        VideoPublishedDerivativeReceipt? receipt =
            await new PostgresVideoPublishedDerivativeRepository(db)
                .GetCurrentAsync(options.ProxyJobId, token);
        if (receipt is null)
        {
            Write(output, !options.Apply, false, false, "proxy-not-current",
                sceneAnnotated: options.SceneCutsMs is not null);
            return 1;
        }
        long originalBytes;
        decimal duration;
        await using (NpgsqlConnection connection = await db.OpenConnectionAsync(token))
        await using (NpgsqlCommand metadata = connection.CreateCommand())
        {
            metadata.CommandText = """
                SELECT revision.size_bytes, inspection.duration_seconds
                FROM asset_revisions revision
                JOIN video_revision_metadata inspection
                  ON inspection.asset_revision_id=revision.id
                WHERE revision.id=@revision
                  AND revision.media_kind='video'
                  AND inspection.inspection_status='supported'
                  AND inspection.color_transfer='bt709'
                  AND inspection.duration_seconds BETWEEN 0.2 AND 3600;
                """;
            metadata.Parameters.AddWithValue("revision", receipt.RevisionId);
            await using NpgsqlDataReader reader = await metadata.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
            {
                Write(output, !options.Apply, false, false, "unsupported-video",
                sceneAnnotated: options.SceneCutsMs is not null);
                return 1;
            }
            originalBytes = reader.GetInt64(0);
            duration = reader.GetDecimal(1);
        }
        string samplingPolicy;
        try
        {
            samplingPolicy = options.SceneCutsMs is null
                ? VideoFrameSamplingPlanner.PolicyVersion
                : VideoFrameSceneSamplingIdentity.Create(
                    options.Frames, options.SceneCutsMs,
                    (long)decimal.Round(duration * 1000m, 0,
                        MidpointRounding.AwayFromZero));
            if (VideoFrameSceneSamplingIdentity.BuildPlan(
                duration, options.Frames, samplingPolicy).Samples.Count != options.Frames)
                throw new ArgumentException("Sampling plan is incomplete.");
        }
        catch (ArgumentException)
        {
            Write(output, !options.Apply, false, false,
                "invalid-sampling-plan", sceneAnnotated: options.SceneCutsMs is not null);
            return 1;
        }
        if (originalBytes is < 1 or > MaxSource ||
            receipt.EncodedBytes is < 100 or
                > PostgresVideoFrameProxyPilotSource.MaximumPilotProxyBytes)
        {
            Write(output, !options.Apply, false, false,
                "bounded-candidate-rejected", sceneAnnotated: options.SceneCutsMs is not null);
            return 1;
        }

        VideoProcessingControlState control =
            await new PostgresVideoProcessingControlRepository(db).GetAsync(token);
        VideoPhotoPrioritySnapshot priority =
            await new PostgresVideoPhotoPriorityProbe(db).GetAsync(token);
        bool otherJobRunning;
        await using (NpgsqlConnection connection = await db.OpenConnectionAsync(token))
        await using (NpgsqlCommand running = connection.CreateCommand())
        {
            running.CommandText =
                "SELECT EXISTS(SELECT 1 FROM video_processing_jobs WHERE status='running');";
            otherJobRunning = await running.ExecuteScalarAsync(token) is true;
        }
        if (control.Enabled || !control.Paused ||
            control.SelectedJobId is not null || otherJobRunning ||
            priority.PhotoWorkActive)
        {
            Write(output, !options.Apply, false, false, "operator-or-photo-busy",
                sceneAnnotated: options.SceneCutsMs is not null);
            return 1;
        }

        // Preview stops HERE. Do not check model inventory in dry run:
        // that might wake or bill the operator-controlled Azure application.
        if (!options.Apply)
        {
            Write(output, true, true, false, "preflight-only",
                sceneAnnotated: options.SceneCutsMs is not null);
            return 0;
        }

        // Fresh v38 approval and a separate explicit acknowledgment that
        // selected JPEG pixels are disclosed to the configured loopback
        // Ollama server (potentially an operator-managed Azure bridge).
        if (!options.AcknowledgeV38 || !options.AcknowledgeDisclosure)
            throw new InvalidOperationException(
                "Operator acknowledgements are required for any frame disclosure.");
        foreach (string file in new[] {
            options.Producer.ClipModelPath, options.Producer.ClipVocabularyPath,
            options.Producer.ClipMergesPath
        })
        {
            if (!File.Exists(file))
                throw new InvalidOperationException("Required local CLIP file missing.");
            VideoDerivativeCacheGuard.AssertNoLinks(file);
        }

        VideoProcessingBudget budget = new(
            Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
            MaximumSourceBytes: MaxSource, MaximumTemporaryBytes: MaxScratch,
            MaximumExtractedFrames: 2, MaximumJobSeconds: MaxSeconds);
        var candidate = new VideoProcessingCandidate(
            true, false, true, true, originalBytes, MaxScratch, options.Frames);
        var identity = new VideoDerivativeJobIdentity(
            AssetRevisionId.From(receipt.RevisionId), VideoDerivativeKind.FrameEvidence,
            samplingPolicy, VideoFrameExtractionProfile.Version,
            options.Producer.FfmpegSha256);
        Guid job = await new PostgresVideoProcessingJobRepository(db)
            .EnqueueAsync(identity, budget, candidate, token);
        var scoped = new PostgresVideoScopedAttemptControl(db);
        VideoScopedAttemptGrant? grant = await scoped.ArmAsync(job,
            options.Producer.FfmpegSha256, VideoFrameExtractionProfile.Version,
            TimeSpan.FromMinutes(15), token, derivativeKind: "FrameEvidence");
        if (grant is null)
        {
            Write(output, false, false, false, "selected-job-not-queued",
                sceneAnnotated: options.SceneCutsMs is not null);
            return 1;
        }

        long executionStarted = Stopwatch.GetTimestamp();
        VideoWorkerCycleResult? cycle = null;
        try
        {
            var producer = new VideoRegisteredProxyFrameEvidenceProducer(
                db, options.Producer);
            var adapter = new VideoFrameEvidenceWorkerAdapter(db, producer);
            cycle = await new VideoWorkerAttemptCoordinator(db)
                .RunOneAsync(budget, adapter, token, selectedJobId: job);
        }
        finally
        {
            // External cancellation must not suppress disarm. A crashed
            // process is still fenced by the absolute database grant expiry.
            using CancellationTokenSource cleanup =
                new(TimeSpan.FromSeconds(15));
            bool disarmed = await scoped.DisarmAsync(grant, cleanup.Token);
            if (!disarmed)
            {
                VideoProcessingControlState latest =
                    await new PostgresVideoProcessingControlRepository(db)
                        .GetAsync(cleanup.Token);
                if (latest.Enabled)
                    throw new InvalidOperationException(
                        "Selected frame scope could not be safely disarmed.");
            }
        }
        bool success = cycle?.Published == true;
        Write(output, false, cycle?.Claimed == true, success,
            success ? "published" : cycle?.ReasonCode ?? "execution-unavailable",
            (long)Stopwatch.GetElapsedTime(executionStarted).TotalMilliseconds,
            sceneAnnotated: options.SceneCutsMs is not null);
        return success ? 0 : 1;
    }

    private static void Write(TextWriter output, bool dryRun, bool admitted,
        bool published, string reasonCode, long? executionWallMs = null,
        bool sceneAnnotated = false) =>
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0190-selected-frame-evidence-v1",
            dryRun, admitted, published, reasonCode,
            samplingMode = sceneAnnotated ? "annotated-scene" : "interval",
            originalsOpened = false, oneDriveHydrationRequested = false,
            maximumConcurrentJobs = 1, maximumFrames = 2,
            executionWallMs,
            automaticallyEnabled = false, captionsReturned = false
        }));
}
