using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Strict, manually selected, single-attempt Windows pilot. The only archive
/// identity accepted is an already-queued UUID; no arbitrary source path,
/// directory scan, enqueue, hydration override or unsupervised worker exists.
/// </summary>
public sealed record VideoScopedOneShotOptions(
    string ConnectionEnv, string DatabaseName, Guid JobId, string CacheRoot,
    string Ffmpeg, string FfmpegSha256,
    string Ffprobe, string FfprobeSha256, bool Apply, bool Acknowledged)
{
    public static VideoScopedOneShotOptions Parse(string[] args)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        bool apply = false, acknowledged = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply) { apply = true; continue; }
            if (args[i] == "--acknowledge-isolated-v37-rehearsal" && !acknowledged)
            { acknowledged = true; continue; }
            string key = args[i];
            if (key is not ("--postgres-connection-env" or "--database" or
                           "--job-id" or "--cache-root" or "--ffmpeg" or
                           "--ffmpeg-sha256" or "--ffprobe" or "--ffprobe-sha256") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !options.TryAdd(key, args[++i]))
                throw new ArgumentException("Unknown, repeated or incomplete video trial parameter.");
        }
        string Required(string key) => options.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException("All video trial options must be explicit.");
        string env = Required("--postgres-connection-env");
        string db = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(db, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant) ||
            !Guid.TryParse(Required("--job-id"), out Guid id) || id == Guid.Empty ||
            !VideoDerivativeArtifactPath.IsHash(Required("--ffmpeg-sha256")) ||
            !VideoDerivativeArtifactPath.IsHash(Required("--ffprobe-sha256")))
            throw new ArgumentException("Video trial requires safe database, UUID and two lowercase tool hashes.");
        string Absolute(string key)
        {
            string path = Required(key);
            if (!Path.IsPathFullyQualified(path) ||
                (OperatingSystem.IsWindows() && path.StartsWith(@"\\", StringComparison.Ordinal)))
                throw new ArgumentException("Video trial accepts local absolute paths only.");
            return Path.GetFullPath(path);
        }
        if (apply && !acknowledged)
            throw new ArgumentException(
                "Apply requires explicit acknowledgement of the isolated v34-to-v37 rehearsal.");
        if (!apply && acknowledged)
            throw new ArgumentException(
                "Rehearsal acknowledgement is only valid with explicit --apply.");
        return new(env, db, id, Absolute("--cache-root"),
            Absolute("--ffmpeg"), Required("--ffmpeg-sha256"),
            Absolute("--ffprobe"), Required("--ffprobe-sha256"), apply, acknowledged);
    }
}

public static class VideoScopedOneShotCommandRunner
{
    private const long MaxSourceBytes = 256L * 1024 * 1024;
    private const long MaxTotalReservation = 512L * 1024 * 1024;
    private static readonly TimeSpan ArmWindow = TimeSpan.FromMinutes(13);

    public static async Task<int> RunAsync(
        VideoScopedOneShotOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Video trial requires Windows native Job Object containment.");
        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Video trial requires an existing PostgreSQL variable.");
        NpgsqlConnectionStringBuilder builder = new(raw);
        if (builder.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new ArgumentException("Video trial requires local PostgreSQL only.");
        builder.Database = options.DatabaseName;
        await using PostgresCatalogueDatabase database = new(builder.ConnectionString);

        // Must precede every call to the normal job repository, which may
        // initialize schema. A v34 maintained catalogue must NEVER be
        // upgraded as a side effect of the operator pilot command.
        await using (NpgsqlConnection connection = await database.OpenConnectionAsync(token))
        await using (NpgsqlCommand schema = connection.CreateCommand())
        {
            schema.CommandText = """
                SELECT COALESCE(MAX(version),0) FROM photo_identity_schema_migrations;
                """;
            if (Convert.ToInt32(await schema.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Selected video trial requires a previously rehearsed current schema; no migration attempted.");
        }

        _ = await VideoDerivativeCacheGuard.ValidateAsync(database, options.CacheRoot, token);
        await VerifyExecutableAsync(options.Ffmpeg, options.FfmpegSha256, token);
        await VerifyExecutableAsync(options.Ffprobe, options.FfprobeSha256, token);

        // All preflight information comes from the authoritative database.
        // Never include source names, private paths, hashes or footage details
        // in CLI output or diagnostics.
        VideoProcessingJobState? selected =
            await new PostgresVideoProcessingJobRepository(database)
                .GetAsync(options.JobId, token);
        VideoProcessingControlState control =
            await new PostgresVideoProcessingControlRepository(database)
                .GetAsync(token);
        bool previewAllowed = selected?.Status == "queued" &&
            !selected.CancellationRequested &&
            !control.Enabled && control.Paused &&
            !(await new PostgresVideoPhotoPriorityProbe(database)
                .GetAsync(token)).PhotoWorkActive;

        if (!options.Apply)
        {
            Write(output, true, previewAllowed, "preflight-only", false);
            return previewAllowed ? 0 : 1;
        }
        if (!previewAllowed)
        {
            Write(output, false, false, "preflight-denied", false);
            return 1;
        }

        var scoped = new PostgresVideoScopedAttemptControl(database);
        VideoScopedAttemptGrant? grant = await scoped.ArmAsync(
            options.JobId, options.FfmpegSha256,
            VideoProxyCandidateProfile.Version, ArmWindow, token);
        if (grant is null)
        {
            Write(output, false, false, "admission-denied", false);
            return 1;
        }

        VideoWorkerCycleResult? cycle = null;
        try
        {
            // One worker invocation, not a polling loop. Queue admission is
            // globally serialized and SQL-scoped to only the selected UUID.
            VideoProcessingBudget jobBudget = new(
                Enabled: true, Paused: false, MaximumConcurrentJobs: 1,
                MaximumSourceBytes: MaxSourceBytes,
                MaximumTemporaryBytes: MaxTotalReservation,
                MaximumExtractedFrames: 1, MaximumJobSeconds: 660);
            VideoToolExecutionBudget toolBudget = new(
                MaximumWallTime: TimeSpan.FromMinutes(9),
                MaximumTemporaryBytes: VideoProxyCandidateProfile.MaximumOutputBytes,
                MaximumTemporaryEntries: 16,
                InspectionInterval: TimeSpan.FromMilliseconds(200),
                WindowsJobLimits: new WindowsVideoJobLimits(
                    MaximumJobMemoryMiB: 768,
                    MaximumProcesses: 1, MaximumCpuPercent: 15));
            var adapter = new VideoExperimentalProxyAdapter(
                database, options.Ffmpeg, options.Ffprobe,
                options.FfprobeSha256, options.CacheRoot, toolBudget);
            cycle = await new VideoWorkerAttemptCoordinator(database)
                .RunOneAsync(jobBudget, adapter, token);
        }
        finally
        {
            // Do not let caller cancellation suppress disarm. If the
            // connection fails, the absolute PostgreSQL deadline continues
            // to veto source reads, claims, heartbeats and publication.
            using var cleanupDeadline = new CancellationTokenSource(
                TimeSpan.FromSeconds(15));
            await scoped.DisarmAsync(grant, cleanupDeadline.Token);
        }
        bool published = cycle?.Published == true;
        Write(output, false, cycle?.Claimed == true,
            published ? "published" : cycle?.ReasonCode ?? "attempt-unavailable",
            published);
        return published ? 0 : 1;
    }

    private static async Task VerifyExecutableAsync(
        string path, string expectedSha256, CancellationToken token)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Approved video tool is missing or linked.");
        VideoDerivativeCacheGuard.AssertNoLinks(path);
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.SequentialScan);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(input, token))
            .ToLowerInvariant();
        if (actual != expectedSha256)
            throw new InvalidOperationException("Video trial tool digest was not approved.");
    }

    private static void Write(
        TextWriter output, bool dryRun, bool admitted,
        string reasonCode, bool published)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-selected-one-shot-v1",
            dryRun, admitted, published, reasonCode,
            maximumConcurrentVideoJobs = 1, permanentlyEnabled = false
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
