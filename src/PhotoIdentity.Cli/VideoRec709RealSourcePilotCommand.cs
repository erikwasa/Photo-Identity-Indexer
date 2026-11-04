using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

/// <summary>
/// WI-0210 single private real-video evaluation. This is NOT a production
/// worker, derivative publisher or source hydration mechanism. Dry-run never
/// connects to PostgreSQL or reads source bytes. Apply requires a separately
/// approved one-revision original read and an owned review-only output.
/// </summary>
public sealed record VideoRec709RealSourcePilotOptions(
    string ConnectionEnv, string Database, Guid RevisionId, string ScratchRoot,
    string Ffmpeg, string FfmpegSha256, string Ffprobe, string FfprobeSha256,
    bool Apply, bool ReadLocalBytes, bool RetainReviewProxy)
{
    public static VideoRec709RealSourcePilotOptions Parse(string[] args)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        bool apply = false, read = false, retain = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply) { apply = true; continue; }
            if (args[i] == "--read-local-bytes" && !read) { read = true; continue; }
            if (args[i] == "--retain-review-proxy" && !retain)
            { retain = true; continue; }
            if (args[i] is not ("--postgres-connection-env" or "--database" or
                "--revision-id" or "--scratch-root" or "--ffmpeg" or
                "--ffmpeg-sha256" or "--ffprobe" or "--ffprobe-sha256") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !options.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Invalid, repeated or incomplete real-video evaluation option.");
        }
        string Required(string key) =>
            options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException("Real-video evaluation requires explicit inputs.");
        string env = Required("--postgres-connection-env");
        string database = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(database, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant) ||
            !Guid.TryParse(Required("--revision-id"), out Guid revision) ||
            revision == Guid.Empty ||
            !VideoOneShotSourceProofCommandRunner.IsApprovedDigest(
                Required("--ffmpeg-sha256")) ||
            !VideoOneShotSourceProofCommandRunner.IsApprovedDigest(
                Required("--ffprobe-sha256")))
            throw new ArgumentException("Invalid database, UUID or approved tool hash.");
        if (apply != read || apply != retain)
            throw new ArgumentException(
                "Real-video execution requires --apply, --read-local-bytes AND --retain-review-proxy together.");
        string Absolute(string key)
        {
            string path = Required(key);
            if (!Path.IsPathFullyQualified(path) ||
                path.StartsWith(@"\\", StringComparison.Ordinal))
                throw new ArgumentException("Real-video evaluation needs local absolute paths.");
            return Path.GetFullPath(path);
        }
        return new(env, database, revision, Absolute("--scratch-root"),
            Absolute("--ffmpeg"), Required("--ffmpeg-sha256"),
            Absolute("--ffprobe"), Required("--ffprobe-sha256"),
            apply, read, retain);
    }
}

internal sealed record VideoRec709SelectedSource(
    Guid SourceId, string Root, string Key, string Sha256,
    long Bytes, VideoRevisionInspection Inspection);

public static class VideoRec709RealSourcePilotCommandRunner
{
    public const string RootMarker = ".photoidentity-real-video-evaluation";
    public const string MarkerText = "wi0210-private-real-video-evaluation-v1";
    public const string ReviewFileName = "review-proxy.mp4";
    private const long MaximumBytes = 256L * 1024 * 1024;

    private static VideoToolExecutionBudget InputProbeBudget => new(
        MaximumWallTime: TimeSpan.FromSeconds(20),
        MaximumTemporaryBytes: 128 * 1024,
        MaximumTemporaryEntries: 8,
        InspectionInterval: TimeSpan.FromMilliseconds(100),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 512, MaximumProcesses: 1,
            MaximumCpuPercent: 25,
            MaximumObservedReadBytes: 512L * 1024 * 1024,
            MaximumObservedWriteBytes: 32L * 1024 * 1024));

    private static VideoToolExecutionBudget EncodeBudget => new(
        MaximumWallTime: TimeSpan.FromMinutes(5),
        MaximumTemporaryBytes: VideoProxyCandidateProfile.MaximumOutputBytes,
        MaximumTemporaryEntries: 16,
        InspectionInterval: TimeSpan.FromMilliseconds(200),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 768, MaximumProcesses: 1,
            MaximumCpuPercent: 15,
            MaximumObservedReadBytes: 1024L * 1024 * 1024,
            MaximumObservedWriteBytes: 384L * 1024 * 1024));

    public static string ValidateRoot(string scratch)
    {
        if (!Path.IsPathFullyQualified(scratch) ||
            !Directory.Exists(scratch) ||
            scratch.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Real-video evaluation needs an existing local scratch root.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratch));
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!root.StartsWith(temp + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Evaluation scratch must be a dedicated system-temp child.");
        for (string? dir = root; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked evaluation scratch directory.");
            if (string.Equals(dir, temp, StringComparison.OrdinalIgnoreCase)) break;
        }
        string marker = Path.Combine(root, RootMarker);
        if (!File.Exists(marker) ||
            (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(marker).Length != MarkerText.Length ||
            File.ReadAllText(marker) != MarkerText ||
            Directory.EnumerateFileSystemEntries(root).Any(x =>
                !string.Equals(Path.GetFileName(x), RootMarker, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Real-video scratch must contain only the exact ownership marker.");
        return root;
    }

    public static async Task<int> RunAsync(
        VideoRec709RealSourcePilotOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        string root = ValidateRoot(options.ScratchRoot);
        if (!options.Apply)
        {
            Write(output, "not-run", dryRun: true, originalOpened: false);
            return 0;
        }
        if (!options.ReadLocalBytes || !options.RetainReviewProxy ||
            !OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Real-video evaluation requires explicit Windows source-read and review permission.");

        string stage = "database-preflight";
        bool opened = false, inputProven = false, outputValidated = false,
            afterIntegrity = false, retained = false;
        VideoProxyInspection? playable = null;
        string reviewFile = Path.Combine(root, ReviewFileName);
        string partialReview = Path.Combine(root, "review-proxy.partial");
        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(12));
        try
        {
            string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException("PostgreSQL environment is not configured.");
            NpgsqlConnectionStringBuilder builder = new(raw);
            if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
                throw new InvalidOperationException("Real-video evaluation requires loopback PostgreSQL.");
            builder.Database = options.Database;
            await using PostgresCatalogueDatabase database = new(builder.ConnectionString);

            VideoRec709SelectedSource? selected = await ReadSelectedAsync(
                database, options.RevisionId, deadline.Token);
            if (selected is null)
                throw new InvalidOperationException("Selected revision is not eligible.");

            // Headroom for staged input + bounded encode + a private
            // review copy, in addition to the mandatory 512 MiB reserve.
            stage = "scratch-capacity";
            VideoScratchCapacityGuard.EnsureCapacity(root,
                selected.Bytes + 2L * VideoProxyCandidateProfile.MaximumOutputBytes);
            stage = "safety-preflight";
            async Task<bool> Allowed(CancellationToken ct) =>
                await CheckAllowedAsync(database, options.RevisionId, selected, ct);
            if (!await Allowed(deadline.Token))
                throw new InvalidOperationException("Source authorization is not available.");

            // Verify exact executable identities before any original access.
            stage = "tool-identity";
            await VideoScopedOneShotCommandRunner.VerifyExecutableAsync(
                options.Ffmpeg, options.FfmpegSha256, deadline.Token);
            await VideoScopedOneShotCommandRunner.VerifyExecutableAsync(
                options.Ffprobe, options.FfprobeSha256, deadline.Token);

            // The stage copies through FILE_FLAG_OPEN_NO_RECALL, hashes each
            // byte against the current registered SHA, and rechecks the local
            // OneDrive status and source metadata after copying.
            stage = "verified-local-stage";
            var source = new OneDriveVideoAssetSource(
                SourceId.From(selected.SourceId), selected.Root);
            var reference = new SourceAssetReference(source.SourceId, selected.Key);
            opened = true; // Conservative: staging may have opened before failing.
            await using VideoStagedLocalCopy staged =
                await new OneDriveVideoLocalStager(source).StageAsync(
                    reference, selected.Bytes, selected.Sha256, root,
                    Allowed, deadline.Token);

            // An independent, digest-pinned FFprobe reads ONLY the privately
            // staged exact-byte copy, never the original/archive locator.
            stage = "input-probe";
            bool verifiedInput = false;
            var supervisor = new VideoToolProcessSupervisor();
            VideoGuardedToolExecutionResult inputProbe =
                await new VideoGuardedToolExecutor(supervisor).RunAsync(
                options.Ffprobe, options.FfprobeSha256, root,
                workspace => BuildInputProbeArgs(staged.FilePath, workspace),
                InputProbeBudget, ct => InspectAsync(Allowed, ct),
                TimeSpan.FromMilliseconds(200), deadline.Token,
                onValidatedSuccessfulExit: async (workspace, ct) =>
                {
                    string json = Path.Combine(workspace, "probe.json");
                    if (!File.Exists(json) ||
                        (File.GetAttributes(json) & FileAttributes.ReparsePoint) != 0 ||
                        new FileInfo(json).Length is < 20 or > 65536)
                        throw new InvalidDataException("Input probe evidence absent or unbounded.");
                    byte[] evidence = await File.ReadAllBytesAsync(json, ct);
                    VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                        evidence, selected.Inspection, selected.Bytes);
                    verifiedInput = true;
                });
            if (!inputProbe.Completed || inputProbe.ExitCode != 0 || !verifiedInput)
                throw new InvalidDataException("Selected source range was not independently proven.");
            inputProven = true;

            stage = "range-conversion";
            var validator = new VideoProxyOutputValidator(
                options.Ffprobe, options.FfprobeSha256, root,
                maximumDimension: 720, requireLimitedRange: true);
            VideoGuardedToolExecutionResult encode =
                await new VideoGuardedToolExecutor(supervisor).RunAsync(
                options.Ffmpeg, options.FfmpegSha256, root,
                workspace => VideoRec709FullRangeEvaluationProfile.BuildArguments(
                    staged.FilePath, workspace),
                EncodeBudget, ct => InspectAsync(Allowed, ct),
                TimeSpan.FromMilliseconds(200), deadline.Token,
                onValidatedSuccessfulExit: async (workspace, ct) =>
                {
                    string candidate = Path.Combine(workspace, "output.mp4");
                    stage = "independent-output-validation";
                    VideoProxyInspection validated = await validator.ValidateAsync(
                        candidate, token: ct,
                        inspect: async t => await InspectAsync(Allowed, t));
                    if (!MatchesExpectedOutput(selected.Inspection, validated))
                        throw new InvalidDataException(
                            "Output duration, audio or display orientation is unexpected.");
                    playable = validated;
                    outputValidated = true;
                    // Review copy is not a registered or published derivative.
                    // Only an exact, known filename within the marked root.
                    stage = "retain-private-review";
                    if (!await Allowed(ct))
                        throw new InvalidOperationException("Source authorization was revoked.");
                    File.Copy(candidate, partialReview, overwrite: false);
                    if (new FileInfo(partialReview).Length !=
                        new FileInfo(candidate).Length)
                        throw new InvalidDataException(
                            "Private review copy differs from validated output.");
                    // Review is only exposed as a playable filename after
                    // a complete, validated copy; never expose half-written
                    // private output from an interrupted tool callback.
                    File.Move(partialReview, reviewFile);
                    retained = true;
                });
            if (!encode.Completed || encode.ExitCode != 0 || !outputValidated)
                throw new InvalidDataException("Range conversion was not validated.");

            // A separate no-recall SHA pass proves the original remains the
            // registered bytes at the END, after all tool processes exited.
            stage = "post-trial-source-integrity";
            if (!await Allowed(deadline.Token))
                throw new InvalidOperationException("Source authorization was revoked.");
            VideoOneShotSourceProofResult after =
                await VideoOneShotSourceProofCommandRunner.VerifyLocalFileAsync(
                    selected.SourceId, selected.Root, selected.Key,
                    selected.Sha256, selected.Bytes, deadline.Token);
            if (!after.Verified || !after.SourceMetadataStable)
                throw new InvalidDataException("Original source integrity changed.");
            // The revision, exclusion, stopped lane and photo-priority
            // decision must still hold AFTER the last no-recall read.
            if (!await Allowed(deadline.Token))
                throw new InvalidOperationException(
                    "Final source or video-lane authorization was revoked.");
            afterIntegrity = true;

            Write(output, "validated", false, opened, inputProven, outputValidated,
                afterIntegrity, retained, playable, null);
            return 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            // Never print exception messages, private paths, tokens,
            // hashes, raw probe JSON or tool stdout/stderr. If private
            // review-file cleanup fails, report that failure explicitly,
            // rather than pretending the evaluation left no media behind.
            bool cleanupFailed = false;
            try
            {
                if (File.Exists(partialReview)) File.Delete(partialReview);
                if (File.Exists(reviewFile)) File.Delete(reviewFile);
            }
            catch (Exception deleteError) when (deleteError is IOException or
                UnauthorizedAccessException)
            {
                cleanupFailed = true;
            }
            Write(output, "trial-failed", false, opened, inputProven,
                outputValidated, afterIntegrity, cleanupFailed, null,
                new
                {
                    stage = cleanupFailed ? "review-cleanup" : stage,
                    reason = cleanupFailed
                        ? "private-review-cleanup-refused"
                        : VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(error)
                });
            return 1;
        }
    }

    private static async Task<VideoExecutionPermit> InspectAsync(
        Func<CancellationToken, Task<bool>> allowed, CancellationToken ct) =>
        await allowed(ct) ? VideoExecutionPermit.Ready
            : VideoExecutionPermit.Deny("revision-or-lane-changed");

    internal static IReadOnlyList<string> BuildInputProbeArgs(string staged, string workspace) =>
    [
        "-v", "error", "-hide_banner", "-protocol_whitelist", "file",
        "-probesize", "2097152", "-analyzeduration", "2000000",
        "-show_streams", "-show_format", "-show_entries",
        "format=format_name,duration,size:stream=codec_type,codec_name,pix_fmt,width,height,color_range,color_transfer,color_primaries,color_space,channels:stream_tags=rotate:stream_side_data=rotation",
        "-of", "json", "-o", Path.Combine(workspace, "probe.json"),
        "-i", staged
    ];

    public static bool MatchesExpectedOutput(
        VideoRevisionInspection input, VideoProxyInspection output)
    {
        if (input.CodedWidth is null || input.CodedHeight is null ||
            input.DisplayRotationDegrees is null || input.DurationSeconds is null)
            return false;
        bool switched = Math.Abs(input.DisplayRotationDegrees.Value) == 90;
        int width = switched ? input.CodedHeight.Value : input.CodedWidth.Value;
        int height = switched ? input.CodedWidth.Value : input.CodedHeight.Value;
        if ((width > height && output.Width <= output.Height) ||
            (height > width && output.Height <= output.Width))
            return false;
        return output.HasAudio == (input.AudioCodec is not null) &&
            Math.Abs(output.DurationSeconds - (double)input.DurationSeconds.Value) <= 1;
    }

    internal static async Task<VideoRec709SelectedSource?> ReadSelectedAsync(
        PostgresCatalogueDatabase database, Guid revision, CancellationToken token)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, token);
        await using (NpgsqlCommand guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = "SET TRANSACTION READ ONLY;";
            await guard.ExecuteNonQueryAsync(token);
        }
        await using (NpgsqlCommand version = connection.CreateCommand())
        {
            version.Transaction = transaction;
            version.CommandText =
                "SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;";
            if (Convert.ToInt32(await version.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException("Current PostgreSQL schema required; no migration.");
        }
        await using NpgsqlCommand query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT source.id, source.root_locator, asset.source_key,
                   revision.content_sha256, revision.size_bytes,
                   metadata.video_codec, metadata.pixel_format,
                   metadata.color_transfer, metadata.audio_codec,
                   metadata.coded_width, metadata.coded_height,
                   metadata.display_rotation_degrees,
                   metadata.duration_seconds, metadata.inspection_status
            FROM asset_revisions revision
            JOIN assets asset ON asset.id=revision.asset_id
            JOIN sources source ON source.id=asset.source_id
            JOIN video_revision_metadata metadata ON metadata.asset_revision_id=revision.id
            WHERE revision.id=@revision
              AND asset.media_kind='video' AND revision.media_kind='video'
              AND asset.deleted_at_utc IS NULL AND source.kind='local-folder'
              AND revision.size_bytes BETWEEN 1 AND @maximum
              AND revision.id=(
                  SELECT latest.id FROM asset_revisions latest
                  WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                  ORDER BY latest.observed_at_utc DESC,latest.id DESC LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id=asset.source_id
                    AND excluded.source_key=asset.source_key);
            """;
        query.Parameters.AddWithValue("revision", revision);
        query.Parameters.AddWithValue("maximum", MaximumBytes);
        VideoRec709SelectedSource? selected = null;
        await using (NpgsqlDataReader reader = await query.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
            {
                Guid sourceId = reader.GetGuid(0);
                string root = reader.GetString(1);
                string key = reader.GetString(2);
                string digest = reader.GetString(3);
                long bytes = reader.GetInt64(4);
                string? T(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                int? I(int i) => reader.IsDBNull(i) ? null : reader.GetInt32(i);
                string? codec = T(5), pixel = T(6), transfer = T(7), audio = T(8);
                int? width = I(9), height = I(10), rotation = I(11);
                decimal? duration = reader.IsDBNull(12) ? null : reader.GetDecimal(12);
                string status = reader.GetString(13);
                VideoCompatibilityObservation gateRow = new(
                    false, "local-folder", null, status, null,
                    codec, pixel, transfer, audio, width, height, rotation,
                    duration, bytes, false, false, false);
                if (VideoCompatibilityLocalCheckCommandRunner.IsPixelOnlyCandidate(gateRow) &&
                    VideoOneShotSourceProofCommandRunner.IsApprovedDigest(digest))
                {
                    VideoRevisionInspection inspection = new(
                        AssetRevisionId.From(revision), status, null,
                        null, codec, null, audio, pixel, transfer, width,
                        height, rotation, duration, null, null, null,
                        null, null, null, null, DateTimeOffset.UtcNow,
                        new string('0', 64),
                        VideoRec709FullRangeEvaluationProfile.Version);
                    selected = new(sourceId, root, key, digest, bytes, inspection);
                }
            }
        }
        await transaction.CommitAsync(token);
        return selected;
    }

    private static async Task<bool> CheckAllowedAsync(
        PostgresCatalogueDatabase database, Guid revision,
        VideoRec709SelectedSource expected, CancellationToken token)
    {
        VideoRec709SelectedSource? current =
            await ReadSelectedAsync(database, revision, token);
        if (current is null || current.SourceId != expected.SourceId ||
            current.Root != expected.Root || current.Key != expected.Key ||
            current.Sha256 != expected.Sha256 || current.Bytes != expected.Bytes ||
            current.Inspection.VideoCodec != expected.Inspection.VideoCodec ||
            current.Inspection.PixelFormat != expected.Inspection.PixelFormat ||
            current.Inspection.ColorTransfer != expected.Inspection.ColorTransfer ||
            current.Inspection.CodedWidth != expected.Inspection.CodedWidth ||
            current.Inspection.CodedHeight != expected.Inspection.CodedHeight ||
            current.Inspection.DisplayRotationDegrees != expected.Inspection.DisplayRotationDegrees ||
            current.Inspection.DurationSeconds != expected.Inspection.DurationSeconds)
            return false;

        VideoProcessingControlState control =
            await new PostgresVideoProcessingControlRepository(database).GetAsync(token);
        VideoPhotoPrioritySnapshot photo =
            await new PostgresVideoPhotoPriorityProbe(database).GetAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand query = connection.CreateCommand();
        query.CommandText = """
            SELECT EXISTS (SELECT 1 FROM video_processing_jobs WHERE status='running');
            """;
        bool running = await query.ExecuteScalarAsync(token) is true;
        return VideoOneShotEnvironmentCommandRunner.EvaluateControl(
            control, photo.PhotoWorkActive, running) is null;
    }

    private static void Write(
        TextWriter writer, string status, bool dryRun, bool originalOpened,
        bool inputProven = false, bool outputValidated = false,
        bool afterIntegrity = false, bool retained = false,
        VideoProxyInspection? output = null, object? failure = null)
    {
        writer.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0210-guarded-real-fullrange-evaluation-v1",
            profile = VideoRec709FullRangeEvaluationProfile.Version,
            status, dryRun, originalBytesOpened = originalOpened,
            inputFullRangeProven = inputProven,
            outputLimitedRangeValidated = outputValidated,
            sourceIntegrityRechecked = afterIntegrity,
            reviewProxyRetained = retained,
            reviewFileName = retained ? ReviewFileName : null,
            width = output?.Width, height = output?.Height,
            durationSeconds = output?.DurationSeconds, hasAudio = output?.HasAudio,
            oneDriveHydrationRequested = false, databaseWriteRequested = false,
            jobsEnqueued = false, videoWorkersActivated = false,
            derivativesPublished = false, productionProfileChanged = false,
            visualColourApproved = false, realVideoProductionAuthorized = false,
            failure
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
