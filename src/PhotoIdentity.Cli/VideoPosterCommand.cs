using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

/// <summary>
/// Operator-only still-poster materialization. Catalogue reads and source verification
/// occur without --apply; it is the sole switch authorizing files and poster SQL writes.
/// </summary>
public sealed record VideoPosterOptions(
    string SourceRoot, string DerivativeRoot, string FfmpegPath,
    string ConnectionEnvironmentVariable, string DatabaseName,
    int MaximumVideos, int TimeoutSeconds, bool Apply)
{
    public static VideoPosterOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool enabled = false, apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--enable-video" && !enabled) { enabled = true; continue; }
            if (args[i] == "--apply" && !apply) { apply = true; continue; }
            string key = args[i];
            if (key is not ("--source" or "--derivatives" or "--ffmpeg" or
                            "--postgres-connection-env" or "--database" or
                            "--max-videos" or "--timeout-seconds") ||
                i == args.Length - 1 || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(key, args[++i]))
                throw new ArgumentException("Unknown, repeated, or incomplete video poster option.");
        }
        if (!enabled) throw new ArgumentException("Video posters require --enable-video.");
        string Required(string key) => values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) ? value :
                throw new ArgumentException($"Missing {key}.");
        int Bounded(string key, int fallback, int max) =>
            values.TryGetValue(key, out string? raw)
                ? int.TryParse(raw, out int number) && number is >= 1 && number <= max
                    ? number : throw new ArgumentException($"{key} must be 1..{max}.")
                : fallback;
        string env = Required("--postgres-connection-env");
        string database = Required("--database");
        if (!Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(database, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Supply only a valid environment-variable name and explicit database name.");
        return new(Required("--source"), Required("--derivatives"), Required("--ffmpeg"),
            env, database, Bounded("--max-videos", 8, 25),
            Bounded("--timeout-seconds", 20, 120), apply);
    }
}

public static class VideoPosterCommandRunner
{
    public const string Profile = "wi0151-sdr-still-v1";
    private const long MaximumPosterBytes = 8L * 1024 * 1024;

    public static bool IsEligible(VideoRevisionInspection? inspection) =>
        inspection is { InspectionStatus: "supported" } &&
        inspection.ColorTransfer is not ("arib-std-b67" or "smpte2084");

    public static string RelativePosterPath(AssetRevisionId revision, string ffmpegSha256)
    {
        if (revision.IsEmpty ||
            ffmpegSha256.Length != 64 ||
            ffmpegSha256.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Poster requires immutable revision and lowercase tool SHA-256.");
        return $"video-posters/{Profile}/{revision.Value:D}/{ffmpegSha256}.jpg";
    }

    public static (int Width, int Height) ReadJpegDimensions(string file)
    {
        using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 100 or > MaximumPosterBytes ||
            stream.ReadByte() != 0xff || stream.ReadByte() != 0xd8)
            throw new InvalidDataException("Poster is not a bounded JPEG.");
        while (stream.Position + 4 <= stream.Length)
        {
            if (stream.ReadByte() != 0xff)
                throw new InvalidDataException("Malformed JPEG segment prefix.");
            int marker;
            do marker = stream.ReadByte(); while (marker == 0xff);
            if (marker is < 0 or 0xd9 or 0xda) break;
            if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
            int hi = stream.ReadByte(), lo = stream.ReadByte();
            int len = (hi << 8) | lo;
            if (len < 2 || stream.Position + len - 2 > stream.Length)
                throw new InvalidDataException("Invalid JPEG segment length.");
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (len < 7 || stream.ReadByte() != 8)
                    throw new InvalidDataException("Unsupported JPEG frame.");
                int h = (stream.ReadByte() << 8) | stream.ReadByte();
                int w = (stream.ReadByte() << 8) | stream.ReadByte();
                if (w is < 1 or > 1280 || h is < 1 or > 1280)
                    throw new InvalidDataException("Poster dimensions exceed the profile.");
                return (w, h);
            }
            stream.Seek(len - 2, SeekOrigin.Current);
        }
        throw new InvalidDataException("JPEG dimensions could not be verified.");
    }

    public static void ValidateRoots(string sourceRoot, string derivativeRoot)
    {
        if (!Directory.Exists(sourceRoot))
            throw new ArgumentException("Source directory is missing.");
        if (!Directory.Exists(derivativeRoot))
            throw new ArgumentException("Derivative directory must exist before processing.");
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        static string Prefix(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (sourceRoot.Equals(derivativeRoot, comparison) ||
            sourceRoot.StartsWith(Prefix(derivativeRoot), comparison) ||
            derivativeRoot.StartsWith(Prefix(sourceRoot), comparison))
            throw new ArgumentException("Derivative directory must be independent of the source archive.");
        // Only traverse/create within operator-selected real directories, not junctions
        // or symlinks that could redirect writes back into OneDrive.
        for (string? check = derivativeRoot; check is not null; check = Path.GetDirectoryName(check))
        {
            if (Directory.Exists(check) &&
                (File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Derivative directory must not contain a reparse-point ancestor.");
            if (Path.GetPathRoot(check) == check) break;
        }
    }

    public static async Task<int> RunAsync(
        VideoPosterOptions options, TextWriter output, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string sourceRoot = Path.GetFullPath(options.SourceRoot);
        string derivativeRoot = Path.GetFullPath(options.DerivativeRoot);
        ValidateRoots(sourceRoot, derivativeRoot);
        string ffmpeg = VideoPilotMediaTools.ValidateExecutable(options.FfmpegPath);
        string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("PostgreSQL connection environment variable is not configured.");
        NpgsqlConnectionStringBuilder config = new(raw);
        if (config.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new ArgumentException("Video posters require localhost PostgreSQL.");
        config.Database = options.DatabaseName;

        byte[] exeHash;
        await using (FileStream exe = new(ffmpeg, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            exeHash = await SHA256.HashDataAsync(exe, token);
        string ffmpegHash = Convert.ToHexString(exeHash).ToLowerInvariant();

        await using PostgresCatalogueDatabase db = new(config.ConnectionString);
        ArchiveCatalogueSource source = await new PostgresCatalogueSourceRepository(db)
            .FindLocalFolderSourceAsync(sourceRoot, token) ??
            throw new InvalidOperationException(
                "Video source is not registered. Run explicit video ingest first.");
        PostgresVideoAssetRepository videos = new(db);
        PostgresVideoEvidenceRepository evidence = new(db);
        VideoPilotMediaTools tools = new(ffmpeg, ffmpeg, TimeSpan.FromSeconds(options.TimeoutSeconds));
        OneDriveVideoAssetSource original = new(source.SourceId, sourceRoot);
        int enumerated = 0, candidates = 0, skipped = 0, deferredHdr = 0;
        int created = 0, existing = 0, changed = 0, failed = 0;

        await foreach (SourceAsset asset in original.EnumerateAsync(new SourceScanOptions(), token))
        {
            enumerated++;
            if (asset.Availability != AssetAvailability.Local || asset.SizeBytes <= 0 ||
                asset.SizeBytes > VideoIngestCommandRunner.MaximumSourceBytes)
            { skipped++; continue; }
            if (candidates >= options.MaximumVideos) { skipped++; continue; }
            try
            {
                string sourcePath = original.GetLocalPath(asset.Reference);
                if (!VideoIngestCommandRunner.MatchesObservation(asset, new FileInfo(sourcePath)))
                { changed++; continue; }
                string hash = await HashSourceAsync(original, asset.Reference, token);
                if (await original.GetAvailabilityAsync(asset.Reference, token) != AssetAvailability.Local ||
                    !VideoIngestCommandRunner.MatchesObservation(asset, new FileInfo(sourcePath)))
                { changed++; continue; }

                AssetRevisionId? current = await videos.GetCurrentVisibleRevisionForHashAsync(
                    source.SourceId, asset.Reference.ItemKey, new Sha256Digest(hash), token);
                if (current is not AssetRevisionId revision)
                { skipped++; continue; }
                VideoRevisionInspection? inspection = await evidence.GetInspectionAsync(revision, token);
                if (!IsEligible(inspection))
                {
                    if (inspection?.ColorTransfer is "arib-std-b67" or "smpte2084")
                        deferredHdr++;
                    else skipped++;
                    continue;
                }

                candidates++;
                string relativePath = RelativePosterPath(revision, ffmpegHash);
                string destination = Path.Combine(derivativeRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar));
                VideoStillPoster? stored = await evidence.GetPosterAsync(revision, Profile, ffmpegHash);
                if (stored is not null)
                {
                    if (stored.PosterRelativePath != relativePath ||
                        !File.Exists(destination) ||
                        new FileInfo(destination).Length != stored.EncodedBytes ||
                        await FileHashAsync(destination, token) != stored.PosterSha256 ||
                        ReadJpegDimensions(destination) != (stored.Width, stored.Height))
                        throw new InvalidDataException(
                            "Durable poster file does not match its immutable evidence.");
                    existing++;
                    continue;
                }
                if (!options.Apply) continue;

                string directory = Path.GetDirectoryName(destination)!;
                // A symlink or junction beneath the derivative root must not redirect
                // output into a different volume/archive.
                string profileRoot = Path.Combine(derivativeRoot, "video-posters");
                string policyRoot = Path.Combine(profileRoot, Profile);
                foreach (string segment in new[] { profileRoot, policyRoot, directory })
                {
                    if (Directory.Exists(segment) &&
                        (File.GetAttributes(segment) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Unsafe poster output directory.");
                }
                Directory.CreateDirectory(directory);
                string temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp.jpg");
                try
                {
                    if (!File.Exists(destination))
                    {
                        await tools.RenderPosterAsync(sourcePath, temp, token);
                        // A changed source must not publish a poster under the earlier hash.
                        if (await original.GetAvailabilityAsync(asset.Reference, token) != AssetAvailability.Local ||
                            !VideoIngestCommandRunner.MatchesObservation(asset, new FileInfo(sourcePath)) ||
                            await HashSourceAsync(original, asset.Reference, token) != hash)
                        { changed++; continue; }
                        _ = ReadJpegDimensions(temp);
                        // Concurrent output with the same revision/tool may only be reused,
                        // never overwritten. Recovery from a prior crash is possible.
                        File.Move(temp, destination, overwrite: false);
                    }

                    (int width, int height) = ReadJpegDimensions(destination);
                    long bytes = new FileInfo(destination).Length;
                    string posterHash = await FileHashAsync(destination, token);

                    // Exclusion or supersession that occurs during rendering must not
                    // result in a newly registered, visible stale poster.
                    if (await videos.GetCurrentVisibleRevisionForHashAsync(
                            source.SourceId, asset.Reference.ItemKey,
                            new Sha256Digest(hash), token) != revision)
                    { changed++; continue; }
                    VideoStillPoster poster = new(revision, Profile, ffmpegHash,
                        posterHash, relativePath, bytes, width, height, DateTimeOffset.UtcNow);
                    await evidence.SavePosterAsync(poster, token);
                    created++;
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                InvalidDataException or TimeoutException)
            {
                // No file paths, video metadata, FFmpeg diagnostics or GPS in output.
                failed++;
            }
        }
        output.WriteLine(options.Apply
            ? "Opt-in durable SDR poster materialization finished."
            : "Video poster dry run finished; no poster files or records were created.");
        output.WriteLine($"Enumerated {enumerated}; eligible {candidates}; existing {existing}; new posters {created}; HDR deferred {deferredHdr}; skipped {skipped}; changed {changed}; failures {failed}.");
        output.WriteLine("Poster reads remain revision-bound and exclusion-aware. No originals, photo analysis or cloud hydration were modified.");
        return failed == 0 && changed == 0 ? 0 : 1;
    }

    private static async Task<string> HashSourceAsync(
        OneDriveVideoAssetSource videos, SourceAssetReference reference, CancellationToken token)
    {
        await using Stream content = await videos.OpenContentAsync(reference, token);
        return Convert.ToHexString(await SHA256.HashDataAsync(content, token)).ToLowerInvariant();
    }

    private static async Task<string> FileHashAsync(string path, CancellationToken token)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }
}
