using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Recognition.Onnx.Semantic;

namespace PhotoIdentity.Cli;

/// <summary>
/// Explicit, read-only proof against one registered, current, already-local
/// playable proxy. No source/original path argument, no implicit hydration,
/// no schema migration, no video queue claim, no evidence publication.
/// </summary>
public sealed record VideoRegisteredFramePilotOptions(
    string ConnectionEnv, string Database, Guid JobId, string CacheRoot,
    string ScratchRoot, string Ffmpeg, string FfmpegSha256,
    string ClipModel, string ClipVocabulary, string ClipMerges,
    string OllamaModel, int MaximumFrames, string? Query, bool Apply,
    Uri? OllamaBaseUri = null)
{
    public static VideoRegisteredFramePilotOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply)
            {
                apply = true;
                continue;
            }
            if (args[i] is not ("--postgres-connection-env" or "--database" or
                "--job-id" or "--cache-root" or "--scratch-root" or "--ffmpeg" or
                "--ffmpeg-sha256" or "--clip-model" or "--clip-vocab" or
                "--clip-merges" or "--ollama-model" or "--ollama-base-url" or "--max-frames" or "--query") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Invalid or repeated registered-frame pilot option.");
        }
        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException("All registered-frame pilot inputs must be explicitly configured.");
        string Absolute(string key)
        {
            string path = Required(key);
            if (!Path.IsPathFullyQualified(path) ||
                (OperatingSystem.IsWindows() && path.StartsWith(@"\\", StringComparison.Ordinal)))
                throw new ArgumentException("Registered-frame pilot paths must be local and absolute.");
            return Path.GetFullPath(path);
        }
        string env = Required("--postgres-connection-env");
        string db = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant) ||
            !Guid.TryParse(Required("--job-id"), out Guid jobId) || jobId == Guid.Empty)
            throw new ArgumentException("A valid local database and registered video job UUID are required.");
        string digest = Required("--ffmpeg-sha256");
        if (!VideoDerivativeArtifactPath.IsHash(digest))
            throw new ArgumentException("FFmpeg requires an approved lowercase SHA-256.");
        int frames = 4;
        if (values.TryGetValue("--max-frames", out string? frameText) &&
            (!int.TryParse(frameText, out frames) || frames is < 1 or > 4))
            throw new ArgumentException("Pilot frame count must be 1..4.");
        string ollama = values.TryGetValue("--ollama-model", out string? chosen)
            ? chosen : OllamaVisionCaptionClient.DefaultModel;
        if (ollama.Length is < 1 or > 100 || ollama.Any(char.IsControl))
            throw new ArgumentException("A bounded local Ollama model name is required.");
        string? query = values.TryGetValue("--query", out string? text) ? text.Trim() : null;
        if (query is not null && (query.Length is < 1 or > 200 ||
                                  query.Any(char.IsControl)))
            throw new ArgumentException("CLIP comparison query must be 1..200 plain-text characters.");
        Uri? baseUri = null;
        if (values.TryGetValue("--ollama-base-url", out string? baseText) &&
            (!Uri.TryCreate(baseText, UriKind.Absolute, out baseUri) ||
             !baseUri.IsLoopback || baseUri.Scheme is not ("http" or "https") ||
             baseUri.AbsolutePath != "/" || baseUri.UserInfo.Length > 0 ||
             baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0))
            throw new ArgumentException("Pilot Ollama URL must be a credential-free loopback HTTP(S) origin.");
        return new(env, db, jobId, Absolute("--cache-root"), Absolute("--scratch-root"),
            Absolute("--ffmpeg"), digest, Absolute("--clip-model"),
            Absolute("--clip-vocab"), Absolute("--clip-merges"), ollama, frames, query, apply,
            baseUri);
    }
}

public static class VideoRegisteredFramePilotCommandRunner
{
    private static VideoToolExecutionBudget FrameBudget => new(
        MaximumWallTime: TimeSpan.FromSeconds(60),
        MaximumTemporaryBytes: 8L * 1024 * 1024,
        MaximumTemporaryEntries: 16,
        InspectionInterval: TimeSpan.FromMilliseconds(200),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 768, MaximumProcesses: 1,
            MaximumCpuPercent: 15));

    public static async Task<int> RunAsync(
        VideoRegisteredFramePilotOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        // Even dry-run requires the *marked and empty* isolated temp root.
        string root = VideoSyntheticProxyPilotCommandRunner.ValidateRoot(options.ScratchRoot);
        if (!options.Apply)
        {
            Write(output, true, "not-run", 0, 0, 0, 0, null, null);
            return 0;
        }
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Registered frame pilots require Windows Job Objects.");

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        string workspace = Path.Combine(root, "wi0190-proxy-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(workspace))
            throw new IOException("The pilot workspace already exists.");
        Directory.CreateDirectory(workspace);
        string stage = "preflight";
        int produced = 0;
        long totalJpegBytes = 0;
        long decodeMilliseconds = 0;
        long inferenceMilliseconds = 0;
        try
        {
            VideoScratchCapacityGuard.EnsureCapacity(root, 256L * 1024 * 1024);
            string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("The local PostgreSQL environment is not configured.");
            var builder = new NpgsqlConnectionStringBuilder(raw);
            if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
                throw new InvalidOperationException("Video pilot requires loopback PostgreSQL.");
            builder.Database = options.Database;

            await using PostgresCatalogueDatabase database = new(builder.ConnectionString);
            // Absolutely no InitializeAsync call or migration on operator data.
            await using (NpgsqlConnection db = await database.OpenConnectionAsync(deadline.Token))
            await using (NpgsqlCommand version = db.CreateCommand())
            {
                version.CommandText = """
                    SELECT COALESCE(MAX(version),0)
                    FROM photo_identity_schema_migrations;
                    """;
                if (Convert.ToInt32(await version.ExecuteScalarAsync(deadline.Token)) !=
                    PostgresCatalogueDatabase.CurrentSchemaVersion)
                    throw new InvalidOperationException("The registered frame pilot requires an already-current schema.");
            }

            var proxy = new PostgresVideoFrameProxyPilotSource(database);
            string staged = Path.Combine(workspace, "registered-proxy.mp4");
            stage = "registered-proxy-snapshot";
            VideoFrameProxySnapshot? snapshot = await proxy.CopyCurrentProxyAsync(
                options.JobId, options.CacheRoot, staged, deadline.Token);
            if (snapshot is null)
                throw new InvalidOperationException("No eligible current registered proxy is available.");
            VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
                new VideoFrameSamplingRequest(snapshot.DurationSeconds,
                    MaximumFrames: options.MaximumFrames));

            stage = "model-preflight";
            await using FileStream modelBytes = new(options.ClipModel, FileMode.Open,
                FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            string clipDigest = Convert.ToHexString(
                await SHA256.HashDataAsync(modelBytes, deadline.Token)).ToLowerInvariant();
            using ClipEmbeddingEncoder encoder = new(
                options.ClipModel, options.ClipVocabulary, options.ClipMerges);
            using HttpClient http = new()
            {
                Timeout = TimeSpan.FromSeconds(200)
            };
            var captionClient = new OllamaVisionCaptionClient(
                http, options.OllamaBaseUri ?? OllamaVisionCaptionClient.DefaultBaseUri,
                options.OllamaModel);
            LocalVisionModelDescriptor captionModel =
                await captionClient.GetInstalledModelAsync(deadline.Token);

            var provenance = new VideoFrameEvidenceProvenance(
                AssetRevisionId.From(snapshot.RevisionId),
                plan.PolicyVersion,
                VideoFrameExtractionProfile.Version,
                ClipEmbeddingEncoder.ModelId,
                clipDigest,
                ClipZeroShotTagger.ImagePreprocessingVersion)
            {
                CaptionModelId = captionModel.Name,
                CaptionModelSha256 = captionModel.Digest,
                CaptionPromptVersion = OllamaVisionCaptionClient.PromptVersion
            };

            var supervisor = new VideoToolProcessSupervisor();
            List<VideoFrameEvidenceItem> items = [];
            foreach (VideoFrameSample frame in plan.Samples)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (!await proxy.StillCurrentAsync(options.JobId,
                        snapshot.RevisionId, deadline.Token))
                    throw new InvalidOperationException("Registered video proxy was revoked.");

                stage = "bounded-frame-extraction";
                // Keep only bounded JPEG validation/decoding inside the
                // supervisor callback. Its 60-second timer is for FFmpeg,
                // NOT slow CLIP initialization or an Azure Ollama cold start.
                ImageFrame? image = null;
                byte[]? encoded = null;
                VideoToolExecutionResult result = await supervisor.RunAsync(
                    options.Ffmpeg, options.FfmpegSha256, root,
                    frameWorkspace => VideoFrameExtractionProfile.BuildArguments(
                        staged, frameWorkspace, frame, plan.DurationMilliseconds),
                    FrameBudget, deadline.Token,
                    afterSuccessfulExit: async (frameWorkspace, frameToken) =>
                    {
                        string jpeg = Path.Combine(frameWorkspace,
                            VideoFrameExtractionProfile.OutputName);
                        long started = Stopwatch.GetTimestamp();
                        image = await VideoFrameExtractionProfile.ReadValidatedFrameAsync(
                            jpeg, frameToken);
                        decodeMilliseconds += (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        encoded = await File.ReadAllBytesAsync(jpeg, frameToken);
                        totalJpegBytes += encoded.LongLength;
                    });
                if (result.ExitCode != 0)
                    throw new InvalidDataException("Bounded frame extraction failed.");
                if (image is null || encoded is null)
                    throw new InvalidDataException("Validated frame was not captured.");

                // Recheck before sending extracted frame pixels to Ollama.
                // The source can be revoked while FFmpeg is running.
                stage = "currentness-before-model";
                if (!await proxy.StillCurrentAsync(options.JobId,
                        snapshot.RevisionId, deadline.Token))
                    throw new InvalidOperationException("Registered video proxy was revoked.");

                stage = "local-caption-and-clip";
                long modelStarted = Stopwatch.GetTimestamp();
                try
                {
                    VideoFrameEvidenceItem item =
                        await VideoFrameModelInferenceBudget.RunAsync(async modelToken =>
                        {
                            var embedding = encoder.EncodeImage(image, modelToken);
                            LocalVisionCaptionResult caption =
                                await captionClient.CaptionAsync(encoded, modelToken);
                            return new VideoFrameEvidenceItem(
                                frame.TimestampMilliseconds, frame.SceneIndex,
                                caption.Content, embedding.Vector.ToArray());
                        }, deadline.Token);
                    items.Add(item);
                    produced++;
                }
                finally
                {
                    // Even a timeout must show time spent waiting on inference.
                    inferenceMilliseconds +=
                        (long)Stopwatch.GetElapsedTime(modelStarted).TotalMilliseconds;
                }
            }

            stage = "currentness-and-provenance";
            bool stillCurrent = await proxy.StillCurrentAsync(
                options.JobId, snapshot.RevisionId, deadline.Token);
            var batch = new VideoFrameEvidenceBatch(provenance, items);
            VideoFrameEvidenceValidation validation =
                VideoFrameEvidenceContract.Validate(plan, batch, provenance,
                    CurrentRevision: stillCurrent, Excluded: !stillCurrent,
                    FullyCompleted: produced == plan.Samples.Count);
            if (!validation.Accepted)
                throw new InvalidOperationException("Frame evidence validation refused: " +
                    validation.ReasonCode);

            VideoFrameRetrievalScores? scores = null;
            if (options.Query is not null)
            {
                stage = "retrieval-diagnostic";
                // Same CLIP joint encoder as still-photo search evaluation.
                // Numeric scores are diagnostic, never query content/results.
                var queryVector = encoder.EncodeText(options.Query, deadline.Token);
                scores = VideoFrameRetrievalScoring.Compare(
                    items.Select(item => item.Embedding!).ToArray(),
                    queryVector.Vector);
            }
            Write(output, false, "validated-not-published", produced,
                totalJpegBytes, decodeMilliseconds, inferenceMilliseconds, null, scores);
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            Write(output, false, "pilot-failed", produced, totalJpegBytes,
                decodeMilliseconds, inferenceMilliseconds, stage + ":timeout", null);
            return 1;
        }
        catch (Exception error)
        {
            // Aggregate-only. Never log private path, job ID, model caption,
            // embedding, executable stderr, or exception's raw message.
            Write(output, false, "pilot-failed", produced, totalJpegBytes,
                decodeMilliseconds, inferenceMilliseconds,
                stage + ":" + VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(error), null);
            return 1;
        }
        finally
        {
            // This directory was created under a marker-checked, initially
            // empty scratch root specifically for this call. Never delete the
            // marker or any pre-existing user directory.
            if (Directory.Exists(workspace))
                Directory.Delete(workspace, recursive: true);
        }
    }

    private static void Write(TextWriter output, bool dryRun, string status,
        int frames, long jpegBytes, long decodingMs, long modelMs,
        string? errorCode, VideoFrameRetrievalScores? scores)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0190-registered-proxy-frames-v1",
            dryRun, status, errorCode, frames,
            jpegBytes, decodingMs, modelMs, retrievalScores = scores,
            originalsOpened = false, oneDriveHydrationRequested = false,
            videoJobsEnqueued = false, schemaChanged = false,
            evidencePersisted = false, captionsReturned = false
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}


/// <summary>
/// Independent, bounded caption/embedding clock. Unlike the FFmpeg supervisor
/// this clock starts AFTER the validated JPEG has left the tool workspace.
/// Each frame is capped at 3 minutes, while the caller retains its separate
/// 10-minute limit for the complete registered-proxy pilot.
/// </summary>
public static class VideoFrameModelInferenceBudget
{
    public static readonly TimeSpan MaximumPerFrame = TimeSpan.FromMinutes(3);

    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> infer,
        CancellationToken overallCancellation,
        TimeSpan? maximumTime = null)
    {
        ArgumentNullException.ThrowIfNull(infer);
        TimeSpan limit = maximumTime ?? MaximumPerFrame;
        if (limit < TimeSpan.FromMilliseconds(10) || limit > MaximumPerFrame)
            throw new ArgumentOutOfRangeException(nameof(maximumTime),
                "Frame model inference must remain bounded to three minutes.");

        using CancellationTokenSource frameDeadline =
            CancellationTokenSource.CreateLinkedTokenSource(overallCancellation);
        frameDeadline.CancelAfter(limit);
        try
        {
            return await infer(frameDeadline.Token);
        }
        catch (OperationCanceledException) when (
            frameDeadline.IsCancellationRequested &&
            !overallCancellation.IsCancellationRequested)
        {
            throw new TimeoutException("Frame inference exceeded the per-frame time budget.");
        }
    }
}
