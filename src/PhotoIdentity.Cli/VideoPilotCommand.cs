using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

public sealed record VideoPilotOptions(
    string SourceRoot,
    string DerivativeRoot,
    string FfprobePath,
    string FfmpegPath,
    int MaximumVideos,
    int TimeoutSeconds)
{
    public static VideoPilotOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool enabled = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--enable-video")
            {
                enabled = true;
                continue;
            }

            string key = args[i];
            if (key is not ("--source" or "--derivatives" or "--ffprobe" or "--ffmpeg" or
                            "--max-videos" or "--timeout-seconds") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(key, args[++i]))
                throw new ArgumentException("Unknown, repeated, or incomplete video pilot option.");
        }
        if (!enabled)
            throw new ArgumentException("Video pilot requires the explicit --enable-video flag.");

        string Required(string name) => values.TryGetValue(name, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Missing {name}.");
        int Limit(string name, int fallback, int min, int max)
        {
            if (!values.TryGetValue(name, out string? value)) return fallback;
            if (!int.TryParse(value, out int n) || n < min || n > max)
                throw new ArgumentException($"{name} must be between {min} and {max}.");
            return n;
        }
        return new VideoPilotOptions(
            Required("--source"), Required("--derivatives"),
            Required("--ffprobe"), Required("--ffmpeg"),
            Limit("--max-videos", 8, 1, 50),
            Limit("--timeout-seconds", 20, 1, 120));
    }
}

public sealed record VideoPilotRow(
    string KeyHash,
    string MediaType,
    string Outcome,
    VideoPilotMetadata? Metadata,
    string? PosterRelativePath);

public sealed record VideoPilotReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    int Enumerated,
    int OnlineOnly,
    int Downloading,
    int UnavailableOrError,
    int LocalAvailable,
    int SkippedAboveSizeLimit,
    int SkippedByPilotLimit,
    IReadOnlyList<VideoPilotRow> SampledVideos,
    string Note);

/// <summary>
/// Deliberately isolated/operator-invoked video metadata and poster pilot.
/// NOT the authoritative catalogue: no source ID creation, SQL writes, photo-query changes,
/// background jobs, hydration, or browser serving. WI-0151 catalogue integration follows.
/// </summary>
public static class VideoPilotCommandRunner
{
    private const long MaximumSourceBytes = 1024L * 1024 * 1024;
    private const string PilotProfile = "pilot-poster-v1";

    public static async Task<int> RunAsync(
        VideoPilotOptions options, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string sourceRoot = Path.GetFullPath(options.SourceRoot);
        string derivatives = Path.GetFullPath(options.DerivativeRoot);
        if (!Directory.Exists(sourceRoot))
            throw new ArgumentException("Source directory does not exist.");
        if (IsWithin(derivatives, sourceRoot) || IsWithin(sourceRoot, derivatives))
            throw new ArgumentException("Derivative root must be independent of the source root.");
        // Explicit absolute trusted paths; never execute ffmpeg/ffprobe from the archive.
        VideoPilotMediaTools tools = new(options.FfprobePath, options.FfmpegPath,
            TimeSpan.FromSeconds(options.TimeoutSeconds));
        Directory.CreateDirectory(derivatives);
        string posterRoot = Path.Combine(derivatives, "video-posters", PilotProfile);
        Directory.CreateDirectory(posterRoot);

        // Pilot-only ephemeral source reference. Catalogue IDs are not created by this command.
        OneDriveVideoAssetSource videos = new(SourceId.New(), sourceRoot);
        int enumerated = 0, online = 0, downloading = 0, unavailable = 0;
        int local = 0, oversized = 0, beyondLimit = 0;
        List<VideoPilotRow> results = [];
        await foreach (SourceAsset asset in videos.EnumerateAsync(
            new SourceScanOptions(), cancellationToken))
        {
            enumerated++;
            if (asset.Availability == AssetAvailability.OnlineOnly) { online++; continue; }
            if (asset.Availability == AssetAvailability.Downloading) { downloading++; continue; }
            if (asset.Availability != AssetAvailability.Local) { unavailable++; continue; }
            local++;
            if (asset.SizeBytes > MaximumSourceBytes) { oversized++; continue; }
            if (results.Count >= options.MaximumVideos) { beyondLimit++; continue; }

            // Hash of the private source key and observation, not an asset/revision identifier.
            // Changes in observed file size/write time yield a different disposable poster.
            string keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{asset.Reference.ItemKey}\n{asset.SizeBytes}\n{asset.LastWriteTimeUtc.UtcTicks}")))
                .ToLowerInvariant();
            string relativePoster = Path.Combine("video-posters", PilotProfile, keyHash + ".jpg");
            string destination = Path.Combine(derivatives, relativePoster);
            VideoPilotMetadata? metadata = null;
            string outcome = "probe_failed";
            string? poster = null;
            try
            {
                // Recheck just before launching the external tools; never pin or hydrate.
                string path = videos.GetLocalPath(asset.Reference);
                metadata = await tools.ProbeAsync(path, cancellationToken);
                if (!IsSupported(metadata))
                {
                    outcome = "unsupported_codec_or_profile";
                }
                else if (metadata.ColorTransfer is "arib-std-b67" or "smpte2084")
                {
                    // HLG/PQ -> SDR tone mapping needs validated colour pipeline (WI-0151).
                    outcome = "hdr_poster_needs_tone_mapping";
                }
                else
                {
                    // Repeated runs are allowed; regenerated output is atomically published.
                    string tmp = Path.Combine(posterRoot, Guid.NewGuid().ToString("N") + ".jpg");
                    try
                    {
                        path = videos.GetLocalPath(asset.Reference);
                        await tools.RenderPosterAsync(path, tmp, cancellationToken);
                        File.Move(tmp, destination, overwrite: true);
                        poster = relativePoster.Replace('\\', '/');
                        outcome = "poster_created";
                    }
                    finally
                    {
                        if (File.Exists(tmp)) File.Delete(tmp);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                                             TimeoutException or UnauthorizedAccessException or
                                             JsonException)
            {
                // No raw paths, ffmpeg diagnostics, GPS or metadata values in error summaries.
                outcome = "local_probe_or_poster_failed";
            }

            results.Add(new VideoPilotRow(keyHash, asset.MediaType, outcome, metadata, poster));
        }

        VideoPilotReport report = new(
            1, DateTimeOffset.UtcNow, enumerated, online, downloading, unavailable,
            local, oversized, beyondLimit, results,
            "Operator-only video pilot. This is not catalogue ingestion; source IDs/revisions, exclusions, capture dates and browser visibility are not persisted. Do not upload the manifest without review.");
        string reportPath = Path.Combine(derivatives, "video-pilot-report.json");
        string temporary = reportPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(
                report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
                cancellationToken);
            File.Move(temporary, reportPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        output.WriteLine($"Video pilot: found {enumerated}, local {local}, online-only {online}, downloading {downloading}, unavailable {unavailable}.");
        output.WriteLine($"Probed {results.Count} local videos; posters {results.Count(row => row.Outcome == "poster_created")}; HDR deferred {results.Count(row => row.Outcome == "hdr_poster_needs_tone_mapping")}.");
        output.WriteLine("Private pilot report written beneath the configured derivative root. No catalogue changes or OneDrive hydration were requested.");
        return 0;
    }

    public static bool IsSupported(VideoPilotMetadata m) =>
        (m.VideoCodec == "h264" && m.Profile is "Main" or "High" ||
         m.VideoCodec == "hevc" && m.Profile is "Main" or "Main 10") &&
        m.Width is > 0 and <= 8192 && m.Height is > 0 and <= 8192 &&
        m.DurationSeconds is >= 0 and <= 600;

    private static bool IsWithin(string candidate, string root)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return candidate.Equals(root, comparison) ||
               candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison);
    }
}
