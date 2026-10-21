using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Read-only operator preflight for a FUTURE explicitly approved video trial.
/// Unlike the normal one-shot run preview, this command does not need a job
/// to exist. It never queues work, accesses original media, enables the lane,
/// launches tools or publishes derivatives. It is not execution permission.
/// </summary>
public sealed record VideoOneShotEnvironmentOptions(
    string ConnectionEnv, string DatabaseName, string CacheRoot,
    string Ffmpeg, string FfmpegSha256, string Ffprobe, string FfprobeSha256)
{
    public static VideoOneShotEnvironmentOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is not ("--postgres-connection-env" or "--database" or
                               "--cache-root" or "--ffmpeg" or
                               "--ffmpeg-sha256" or "--ffprobe" or
                               "--ffprobe-sha256") ||
                i + 1 >= args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException(
                    "Read-only video environment preflight rejects unknown or repeated options.");
        }
        string Required(string key) =>
            values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
            throw new ArgumentException("Read-only video environment preflight needs all explicit fields.");
        string environment = Required("--postgres-connection-env");
        string database = Required("--database");
        if (!Regex.IsMatch(environment, @"^[A-Za-z_][A-Za-z0-9_]*$",
                RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(database, @"^[a-z][a-z0-9_]{0,62}$",
                RegexOptions.CultureInvariant) ||
            !VideoDerivativeArtifactPath.IsHash(Required("--ffmpeg-sha256")) ||
            !VideoDerivativeArtifactPath.IsHash(Required("--ffprobe-sha256")))
            throw new ArgumentException("Read-only video environment preflight requires safe names and approved hash shapes.");

        string Absolute(string key)
        {
            string candidate = Required(key);
            if (!Path.IsPathFullyQualified(candidate) ||
                (OperatingSystem.IsWindows() &&
                 candidate.StartsWith(@"\\", StringComparison.Ordinal)))
                throw new ArgumentException("Video environment preflight accepts local absolute paths only.");
            return Path.GetFullPath(candidate);
        }
        return new(environment, database, Absolute("--cache-root"),
            Absolute("--ffmpeg"), Required("--ffmpeg-sha256"),
            Absolute("--ffprobe"), Required("--ffprobe-sha256"));
    }
}

public static class VideoOneShotEnvironmentCommandRunner
{
    // The selected one-shot limits a single job to 512 MiB of reserved
    // staged source and output bytes. The capacity guard separately requires
    // another 512 MiB of independent volume headroom.
    private const long MaximumReservedBytes = 512L * 1024 * 1024;

    public static async Task<int> RunAsync(
        VideoOneShotEnvironmentOptions options, TextWriter output,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Video environment preflight requires Windows.");

        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnv);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Video environment preflight requires configured local PostgreSQL.");

        NpgsqlConnectionStringBuilder builder = new(raw);
        if (builder.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new ArgumentException("Video environment preflight requires localhost PostgreSQL.");
        builder.Database = options.DatabaseName;
        await using PostgresCatalogueDatabase database = new(builder.ConnectionString);

        // Version observation precedes all repository access. Nothing calls
        // InitializeAsync, so old schemas are never upgraded by preflight.
        await using (NpgsqlConnection connection = await database.OpenConnectionAsync(token))
        await using (NpgsqlCommand schema = connection.CreateCommand())
        {
            schema.CommandText = """
                SELECT COALESCE(MAX(version),0)
                FROM photo_identity_schema_migrations;
                """;
            if (Convert.ToInt32(await schema.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Video environment preflight requires schema v37 and never migrates it.");
        }

        VideoProcessingControlState control =
            await new PostgresVideoProcessingControlRepository(database).GetAsync(token);
        VideoPhotoPrioritySnapshot photo =
            await new PostgresVideoPhotoPriorityProbe(database).GetAsync(token);

        bool runningVideo;
        await using (NpgsqlConnection connection = await database.OpenConnectionAsync(token))
        await using (NpgsqlCommand running = connection.CreateCommand())
        {
            running.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM video_processing_jobs WHERE status='running'
                );
                """;
            runningVideo = await running.ExecuteScalarAsync(token) is true;
        }

        string? gate = EvaluateControl(control, photo.PhotoWorkActive, runningVideo);
        if (gate is not null)
        {
            Write(output, passed: false, reasonCode: gate);
            return 1;
        }

        // Verify the exact same ownership, root-separation and digest
        // boundaries as the actual one-shot command. Read only marker and
        // installed binary bytes; never open originals or start tools.
        string root = await VideoDerivativeCacheGuard.ValidateAsync(
            database, options.CacheRoot, token);
        VideoScratchCapacityGuard.EnsureCapacity(root, MaximumReservedBytes);
        await VideoScopedOneShotCommandRunner.VerifyExecutableAsync(
            options.Ffmpeg, options.FfmpegSha256, token);
        await VideoScopedOneShotCommandRunner.VerifyExecutableAsync(
            options.Ffprobe, options.FfprobeSha256, token);

        Write(output, passed: true, reasonCode: "preflight-only");
        return 0;
    }

    public static string? EvaluateControl(
        VideoProcessingControlState state, bool photoWorkActive, bool runningVideo)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Enabled || !state.Paused ||
            state.SelectedJobId is not null ||
            state.AuthorizationExpiresAtUtc is not null)
            return "video-control-not-stopped";
        if (runningVideo) return "running-video-lease";
        if (photoWorkActive) return "photo-work-active";
        return null;
    }

    public static void Write(TextWriter output, bool passed, string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (reasonCode is not (
            "preflight-only" or "video-control-not-stopped" or
            "running-video-lease" or "photo-work-active"))
            throw new ArgumentException("Unrecognized video environment preflight reason.");
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0188-one-shot-environment-preflight-v1",
            dryRun = true, passed, reasonCode,
            schemaVersion = PostgresCatalogueDatabase.CurrentSchemaVersion,
            originalsOpened = false, oneDriveHydrationRequested = false,
            videoWorkersActivated = false, jobsEnqueued = false,
            derivativesPublished = false,
            // Hard disk/IO and network containment is still a separate
            // acceptance gate, not silently implied by free-space checks.
            hardDiskIoQuotaVerified = false,
            networkSandboxVerified = false,
            realVideoExecutionAuthorized = false
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
