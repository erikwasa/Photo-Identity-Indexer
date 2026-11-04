using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Operator-only, bounded extraction from independently verified published MP4
/// proxies. Dry-run is the default. Neither mode opens any archive original.
/// </summary>
public sealed record VideoProxyPosterOptions(
    string CacheRoot, string DerivativeRoot, string FfmpegPath,
    string FfmpegSha256, string FfprobePath, string FfprobeSha256,
    string ConnectionEnvironmentVariable, string DatabaseName,
    int MaximumVideos, int TimeoutSeconds, bool Apply)
{
    public static VideoProxyPosterOptions Parse(string[] args)
    {
        Dictionary<string, string> flags = new(StringComparer.Ordinal);
        bool enabled = false, apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--enable-video" && !enabled) { enabled = true; continue; }
            if (args[i] == "--apply" && !apply) { apply = true; continue; }
            string key = args[i];
            if (key is not ("--cache-root" or "--derivatives" or "--ffmpeg" or
                            "--ffmpeg-sha256" or "--ffprobe" or "--ffprobe-sha256" or
                            "--postgres-connection-env" or "--database" or
                            "--max-videos" or "--timeout-seconds") ||
                i == args.Length - 1 || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !flags.TryAdd(key, args[++i]))
                throw new ArgumentException("Unknown, repeated or incomplete proxy poster option.");
        }
        if (!enabled)
            throw new ArgumentException("Proxy poster backfill requires --enable-video.");
        string Required(string key) => flags.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
                throw new ArgumentException($"Missing {key}.");
        int Bounded(string key, int fallback, int maximum) =>
            flags.TryGetValue(key, out string? raw)
                ? int.TryParse(raw, out int value) && value >= 1 && value <= maximum
                    ? value : throw new ArgumentException($"{key} must be 1..{maximum}.")
                : fallback;
        string env = Required("--postgres-connection-env");
        string database = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(database, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Use a valid environment-variable name and explicit database.");
        static string Digest(string value) =>
            Regex.IsMatch(value, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)
                ? value.ToLowerInvariant()
                : throw new ArgumentException("Provide approved SHA-256 for both tools.");
        return new(Required("--cache-root"), Required("--derivatives"),
            Required("--ffmpeg"), Digest(Required("--ffmpeg-sha256")),
            Required("--ffprobe"), Digest(Required("--ffprobe-sha256")),
            env, database, Bounded("--max-videos", 1, 3),
            Bounded("--timeout-seconds", 30, 90), apply);
    }
}

public static class VideoProxyPosterCommandRunner
{
    public const string Profile = "wi0211-proxy-still-v1";
    private const long MaximumJpegBytes = 8L * 1024 * 1024;

    public static string RelativePosterPath(AssetRevisionId revision, string ffmpegHash)
    {
        if (revision.IsEmpty || ffmpegHash.Length != 64 ||
            ffmpegHash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Invalid poster revision or tool digest.");
        return $"video-posters/{Profile}/{revision.Value:D}/{ffmpegHash}.jpg";
    }

    public static IReadOnlyList<string> BuildRenderArguments(
        string trustedProxyCopy, string outputJpeg, decimal seconds)
    {
        if (seconds is <= 0 or > 7200)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        // Seek past blank opening slates where possible, then select a
        // representative frame from the next short, bounded window.
        decimal seek = Math.Max(0m, Math.Min(seconds * 0.2m, seconds - 1m));
        return [
            "-nostdin", "-hide_banner", "-loglevel", "error",
            "-protocol_whitelist", "file",
            "-ss", seek.ToString("0.000", CultureInfo.InvariantCulture),
            "-i", trustedProxyCopy,
            "-map", "0:v:0", "-frames:v", "1",
            "-an", "-sn", "-dn", "-map_metadata", "-1", "-map_chapters", "-1",
            "-vf", "fps=2,thumbnail=n=8,scale=960:960:force_original_aspect_ratio=decrease:force_divisible_by=2,format=yuvj420p",
            "-q:v", "4", "-f", "image2", "-y", outputJpeg
        ];
    }

    public static IReadOnlyList<string> BuildBrightnessArguments(string jpeg) =>
    [
        "-nostdin", "-hide_banner", "-loglevel", "info",
        "-protocol_whitelist", "file", "-i", jpeg,
        "-map", "0:v:0", "-frames:v", "1", "-an", "-sn", "-dn",
        "-vf", "signalstats,metadata=print:key=lavfi.signalstats.YAVG",
        "-f", "null", "-"
    ];

    public static double ParseBrightness(string diagnostics)
    {
        Match value = Regex.Match(diagnostics,
            @"lavfi\.signalstats\.YAVG=([0-9]+(?:\.[0-9]+)?)",
            RegexOptions.CultureInvariant);
        if (!value.Success || !double.TryParse(value.Groups[1].Value,
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out double brightness) || !double.IsFinite(brightness) ||
            brightness is < 0 or > 255)
            throw new InvalidDataException("Poster brightness could not be independently checked.");
        return brightness;
    }

    public static async Task<int> RunAsync(
        VideoProxyPosterOptions options, TextWriter output, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("PostgreSQL connection environment variable is absent.");
        NpgsqlConnectionStringBuilder settings = new(raw);
        if (settings.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Proxy poster backfill requires loopback PostgreSQL.");
        settings.Database = options.DatabaseName;
        await using PostgresCatalogueDatabase database = new(settings.ConnectionString);
        string cache = await VideoDerivativeCacheGuard.ValidateAsync(
            database, options.CacheRoot, token);
        string derivative = await ValidatePosterRootAsync(database, options.DerivativeRoot, cache, token);
        PostgresVideoLibraryRepository library = new(database);
        IReadOnlyList<AssetRevisionId> candidates =
            await library.ListMissingProxyPostersAsync(options.MaximumVideos, token);

        if (!options.Apply)
        {
            await output.WriteLineAsync(
                $"WI-0211 dry run: candidates={candidates.Count}; applied=0; originalsRead=0; ffmpegRuns=0");
            return 0;
        }

        await RequireIdleVideoLaneAsync(database, token);
        string ffmpeg = await RequirePinnedToolAsync(
            options.FfmpegPath, options.FfmpegSha256, token);
        string ffprobe = await RequirePinnedToolAsync(
            options.FfprobePath, options.FfprobeSha256, token);
        VideoPilotMediaTools probe = new(ffprobe, TimeSpan.FromSeconds(options.TimeoutSeconds));
        var playback = new PostgresVideoPlaybackRepository(database);
        int created = 0, skipped = 0, failed = 0;
        foreach (AssetRevisionId revision in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await using VideoPlaybackLease? lease =
                    await playback.OpenCurrentAsync(revision, cache, token);
                if (lease is null) { skipped++; continue; }
                if (await ProcessOneAsync(
                    lease, revision, derivative, ffmpeg,
                    options.FfmpegSha256, probe, options.TimeoutSeconds, token))
                    created++;
                else skipped++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or InvalidDataException or
                TimeoutException or InvalidOperationException or UnauthorizedAccessException)
            {
                // Never print private paths, source identifiers, FFmpeg stderr, or frames.
                failed++;
            }
        }
        await output.WriteLineAsync(
            $"WI-0211 proxy poster backfill: created={created}; skipped={skipped}; failed={failed}; originalsRead=0; hydration=0");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<bool> ProcessOneAsync(
        VideoPlaybackLease lease, AssetRevisionId revision,
        string derivativeRoot, string ffmpeg, string ffmpegHash,
        VideoPilotMediaTools probe, int timeoutSeconds, CancellationToken token)
    {
        string relative = RelativePosterPath(revision, ffmpegHash);
        string target = Path.Combine(derivativeRoot,
            relative.Replace('/', Path.DirectorySeparatorChar));
        string directory = Path.GetDirectoryName(target)!;
        EnsureSafePosterDirectory(derivativeRoot, directory);

        string temporaryProxy = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp.mp4");
        string temporaryPoster = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp.jpg");
        bool published = false, moved = false;
        try
        {
            // This is the verified, revision-bound MP4 HANDLE from the playback
            // lease, not a new lookup of an arbitrary filename or an original.
            lease.Stream.Position = 0;
            await using (FileStream copy = new(temporaryProxy, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await lease.Stream.CopyToAsync(copy, token);
                await copy.FlushAsync(token);
                if (copy.Length is < 100 or > PostgresVideoPlaybackRepository.MaximumPlaybackBytes)
                    throw new InvalidDataException("Verified proxy copy exceeded its bound.");
            }
            VideoPilotMetadata input = await probe.ProbeAsync(temporaryProxy, token);
            if (input.VideoCodec != "h264" || input.PixelFormat != "yuv420p" ||
                input.ColorTransfer != "bt709" ||
                input.RotationDegrees is not null and not 0 ||
                input.Width is not > 0 or > 4096 || input.Height is not > 0 or > 4096 ||
                input.DurationSeconds is not > 0 or > 7200)
                throw new InvalidDataException("Published proxy failed independent input proof.");

            await RunFfmpegAsync(ffmpeg, BuildRenderArguments(
                temporaryProxy, temporaryPoster, (decimal)input.DurationSeconds.Value),
                timeoutSeconds, token);
            // Reject an all-black or near-black representative frame rather
            // than registering an indistinguishable thumbnail.
            string brightnessEvidence = await RunFfmpegAsync(
                ffmpeg, BuildBrightnessArguments(temporaryPoster), timeoutSeconds, token);
            if (ParseBrightness(brightnessEvidence) < 24)
                throw new InvalidDataException("Representative video frame is too dark.");
            (int width, int height) = VideoPosterCommandRunner.ReadJpegDimensions(temporaryPoster);
            if (width > 960 || height > 960)
                throw new InvalidDataException("Proxy poster dimensions exceeded bounds.");
            VideoPilotMetadata still = await probe.ProbeAsync(temporaryPoster, token);
            if (still.VideoCodec != "mjpeg" || still.Width != width || still.Height != height ||
                still.RotationDegrees is not null and not 0 || still.HasLocation)
                throw new InvalidDataException("Generated JPEG failed independent output proof.");
            // A source-derived orientation must be preserved (no crop or stretch).
            double inputAspect = (double)input.Width.Value / input.Height.Value;
            double outputAspect = (double)width / height;
            if (Math.Abs(inputAspect - outputAspect) / inputAspect > 0.03)
                throw new InvalidDataException("Proxy poster geometry does not match the proxy.");

            long length = new FileInfo(temporaryPoster).Length;
            if (length is < 100 or > MaximumJpegBytes)
                throw new InvalidDataException("Generated poster is not bounded.");
            string posterHash;
            await using (FileStream image = new(temporaryPoster, FileMode.Open,
                FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
                posterHash = Convert.ToHexString(await SHA256.HashDataAsync(image, token))
                    .ToLowerInvariant();
            VideoDerivativeCacheGuard.AssertNoLinks(directory);
            if (File.Exists(target))
                throw new InvalidDataException("Unregistered poster destination already exists.");
            File.Move(temporaryPoster, target, overwrite: false);
            moved = true;

            published = await lease.PublishProxyPosterAsync(new VideoStillPoster(
                revision, Profile, ffmpegHash, posterHash, relative,
                length, width, height, DateTimeOffset.UtcNow), token);
            return published;
        }
        finally
        {
            if (File.Exists(temporaryProxy)) File.Delete(temporaryProxy);
            if (File.Exists(temporaryPoster)) File.Delete(temporaryPoster);
            if (moved && !published && File.Exists(target)) File.Delete(target);
        }
    }

    private static void EnsureSafePosterDirectory(string root, string leaf)
    {
        VideoDerivativeCacheGuard.AssertNoLinks(root);
        string baseDir = Path.Combine(root, "video-posters");
        string profileDir = Path.Combine(baseDir, Profile);
        foreach (string dir in new[] { baseDir, profileDir, leaf })
        {
            if (Directory.Exists(dir))
                VideoDerivativeCacheGuard.AssertNoLinks(dir);
            else Directory.CreateDirectory(dir);
            VideoDerivativeCacheGuard.AssertNoLinks(dir);
        }
    }

    private static async Task RequireIdleVideoLaneAsync(
        PostgresCatalogueDatabase database, CancellationToken token)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand check = connection.CreateCommand();
        check.CommandText = """
            SELECT (enabled AND NOT paused)
                OR EXISTS (SELECT 1 FROM video_processing_jobs WHERE status = 'running')
            FROM video_processing_control WHERE singleton_id = 1;
            """;
        if (await check.ExecuteScalarAsync(token) is not false)
            throw new InvalidOperationException(
                "Pause the Archive and allow running video work to stop before poster backfill.");
    }

    private static async Task<string> RequirePinnedToolAsync(
        string path, string sha256, CancellationToken token)
    {
        string approved = VideoPilotMediaTools.ValidateExecutable(path);
        VideoDerivativeCacheGuard.AssertNoLinks(Path.GetDirectoryName(approved)!);
        if ((File.GetAttributes(approved) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Video tool must not be linked.");
        await using FileStream file = new(approved, FileMode.Open, FileAccess.Read, FileShare.Read);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token))
            .ToLowerInvariant();
        if (actual != sha256)
            throw new InvalidOperationException("Approved media tool digest differs.");
        return approved;
    }

    private static async Task<string> ValidatePosterRootAsync(
        PostgresCatalogueDatabase database, string raw, string cache, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Path.IsPathFullyQualified(raw) ||
            !Directory.Exists(raw) ||
            (OperatingSystem.IsWindows() && raw.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new ArgumentException("Poster root must be an existing local directory.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raw));
        VideoDerivativeCacheGuard.AssertNoLinks(root);
        if (VideoDerivativeCacheGuard.Overlaps(root, cache))
            throw new InvalidOperationException("Poster and proxy-cache roots must be disjoint.");
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand query = connection.CreateCommand();
        query.CommandText = "SELECT root_locator FROM sources WHERE kind = 'local-folder';";
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            string source = reader.GetString(0);
            if (Path.IsPathFullyQualified(source) &&
                VideoDerivativeCacheGuard.Overlaps(root, source))
                throw new InvalidOperationException("Poster root overlaps originals.");
        }
        return root;
    }

    private static async Task<string> RunFfmpegAsync(
        string ffmpeg, IReadOnlyList<string> arguments,
        int timeoutSeconds, CancellationToken token)
    {
        ProcessStartInfo start = new()
        {
            FileName = ffmpeg, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using Process process = new() { StartInfo = start };
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        bool started = false;
        try
        {
            started = process.Start();
            if (!started) throw new IOException("Poster renderer did not start.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(deadline.Token);
            string standardOutput = await stdout.WaitAsync(deadline.Token);
            string standardError = await stderr.WaitAsync(deadline.Token);
            if (standardOutput.Length > 65536 || standardError.Length > 65536)
                throw new InvalidDataException("Media tool diagnostic limit exceeded.");
            if (process.ExitCode != 0)
                throw new InvalidDataException("Verified proxy could not produce a poster.");
            return standardOutput + "\n" + standardError;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Poster renderer exceeded its execution deadline.");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
