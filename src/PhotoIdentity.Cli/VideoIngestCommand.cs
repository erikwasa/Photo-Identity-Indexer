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

/// <summary>Opt-in, bounded, dry-run-first MOV/MP4 revision and metadata ingestion.</summary>
public sealed record VideoIngestOptions(
    string SourceRoot, string FfprobePath, int MaximumVideos, int TimeoutSeconds,
    string? ConnectionEnvironmentVariable, string? DatabaseName, bool Apply)
{
    public static VideoIngestOptions Parse(string[] arguments)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool enabled = false, apply = false;
        for (int i = 0; i < arguments.Length; i++)
        {
            string key = arguments[i];
            if (key == "--enable-video" && !enabled) { enabled = true; continue; }
            if (key == "--apply" && !apply) { apply = true; continue; }
            if (key is not ("--source" or "--ffprobe" or "--max-videos" or
                           "--timeout-seconds" or "--postgres-connection-env" or "--database") ||
                i + 1 == arguments.Length || arguments[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(key, arguments[++i]))
                throw new ArgumentException("Unknown, repeated or incomplete video ingest option.");
        }
        if (!enabled) throw new ArgumentException("Video ingestion requires --enable-video.");
        string Required(string key) => values.TryGetValue(key, out string? raw) &&
            !string.IsNullOrWhiteSpace(raw) ? raw : throw new ArgumentException($"Missing {key}.");
        int Bounded(string key, int fallback, int max) =>
            values.TryGetValue(key, out string? raw)
                ? int.TryParse(raw, out int n) && n >= 1 && n <= max ? n :
                    throw new ArgumentException($"{key} must be 1..{max}.")
                : fallback;
        string? env = values.GetValueOrDefault("--postgres-connection-env");
        if (apply && string.IsNullOrWhiteSpace(env))
            throw new ArgumentException("--apply requires --postgres-connection-env NAME.");
        string? databaseName = values.GetValueOrDefault("--database");
        if (apply && string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("--apply requires an explicit --database NAME.");
        if (databaseName is not null &&
            !Regex.IsMatch(databaseName, @"^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Database must be a simple, explicit PostgreSQL database name.");
        if (env is not null && !Regex.IsMatch(env, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            throw new ArgumentException("Provide an environment variable NAME, not a connection string.");
        return new(Required("--source"), Required("--ffprobe"),
            Bounded("--max-videos", 8, 25), Bounded("--timeout-seconds", 20, 120),
            env, databaseName, apply);
    }
}

public static class VideoIngestCommandRunner
{
    public const long MaximumSourceBytes = 1024L * 1024 * 1024;
    public const string InspectionProfile = "wi0151-ffprobe-metadata-v1";

    public static VideoRevisionInspection BuildInspection(
        AssetRevisionId revision, VideoPilotMetadata media, string toolHash, DateTimeOffset inspectedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(media);
        bool supported = VideoPilotCommandRunner.IsSupported(media);
        decimal? duration = media.DurationSeconds is double s && double.IsFinite(s) && s >= 0 && s <= 600
            ? decimal.Round((decimal)s, 6, MidpointRounding.AwayFromZero) : null;
        string? rate = media.AverageFrameRate is string raw &&
                       Regex.IsMatch(raw, @"^[0-9]+/[1-9][0-9]*$") ? raw : null;
        // FFprobe pilot currently exposes only tag PRESENCE, not resolved capture/GPS values.
        // Never synthesize those fields from file modification timestamps.
        return new(revision, supported ? "supported" : "unsupported",
            supported ? null : "unsupported_codec_or_profile",
            media.Container, media.VideoCodec, media.Profile, media.AudioCodec,
            media.PixelFormat, media.ColorTransfer, media.Width, media.Height,
            media.RotationDegrees, duration, rate,
            null, null, null, null, null, null,
            inspectedAtUtc.ToUniversalTime(), toolHash, InspectionProfile);
    }

    public static bool MatchesObservation(SourceAsset asset, FileInfo file)
    {
        file.Refresh();
        return file.Exists && file.Length == asset.SizeBytes &&
               file.LastWriteTimeUtc == asset.LastWriteTimeUtc.UtcDateTime;
    }

    public static async Task<int> RunAsync(
        VideoIngestOptions options, TextWriter output, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string root = Path.GetFullPath(options.SourceRoot);
        if (!Directory.Exists(root))
            throw new ArgumentException("Video source directory not found.");
        VideoPilotMediaTools tools = new(options.FfprobePath,
            TimeSpan.FromSeconds(options.TimeoutSeconds));
        string ffprobe = VideoPilotMediaTools.ValidateExecutable(options.FfprobePath);

        PostgresCatalogueDatabase? db = null;
        if (options.Apply)
        {
            string? raw = Environment.GetEnvironmentVariable(options.ConnectionEnvironmentVariable!);
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("PostgreSQL environment variable is not set.");
            NpgsqlConnectionStringBuilder connection = new(raw);
            if (connection.Host is not ("127.0.0.1" or "localhost" or "::1"))
                throw new ArgumentException("--apply requires a localhost PostgreSQL host.");
            // The maintained Windows launcher may intentionally omit Database.
            // The operator must name the target explicitly; no implicit default or
            // forgotten process environment can choose the catalogue for a write.
            connection.Database = options.DatabaseName!;
            db = new PostgresCatalogueDatabase(connection.ConnectionString);
        }

        try
        {
            byte[] binaryHash;
            await using (FileStream toolFile = new(ffprobe, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                binaryHash = await SHA256.HashDataAsync(toolFile, token);
            string toolHash = Convert.ToHexString(binaryHash).ToLowerInvariant();

            ArchiveCatalogueSource? source = null;
            PostgresVideoAssetRepository? assetRepository = null;
            PostgresVideoEvidenceRepository? evidenceRepository = null;
            PostgresSourceCopyExclusionRepository? exclusions = null;
            if (db is not null)
            {
                await db.InitializeAsync(token);
                source = await new PostgresCatalogueSourceRepository(db)
                    .GetOrCreateLocalFolderSourceAsync(root, DateTimeOffset.UtcNow, token);
                assetRepository = new(db);
                evidenceRepository = new(db);
                exclusions = new(db);
            }

            OneDriveVideoAssetSource videos = new(source?.SourceId ?? SourceId.New(), root);
            int total = 0, local = 0, placeholders = 0, unavailable = 0;
            int oversized = 0, beyondLimit = 0, excluded = 0, attempted = 0;
            int supported = 0, unsupported = 0, changed = 0, failures = 0;
            int inserted = 0, newAssets = 0, newRevisions = 0;
            await foreach (SourceAsset asset in videos.EnumerateAsync(new SourceScanOptions(), token))
            {
                total++;
                if (asset.Availability is AssetAvailability.OnlineOnly or AssetAvailability.Downloading)
                { placeholders++; continue; }
                if (asset.Availability != AssetAvailability.Local) { unavailable++; continue; }
                local++;
                if (asset.SizeBytes <= 0 || asset.SizeBytes > MaximumSourceBytes)
                { oversized++; continue; }
                if (attempted >= options.MaximumVideos) { beyondLimit++; continue; }

                if (exclusions is not null &&
                    await exclusions.GetAsync(videos.SourceId, asset.Reference.ItemKey, token) is not null)
                { excluded++; continue; }
                attempted++;

                try
                {
                    string path = videos.GetLocalPath(asset.Reference);
                    if (!MatchesObservation(asset, new FileInfo(path))) { changed++; continue; }
                    VideoPilotMetadata media = await tools.ProbeAsync(path, token);
                    byte[] hash;
                    await using (Stream content = await videos.OpenContentAsync(asset.Reference, token))
                        hash = await SHA256.HashDataAsync(content, token);
                    if (await videos.GetAvailabilityAsync(asset.Reference, token) != AssetAvailability.Local ||
                        !MatchesObservation(asset, new FileInfo(path)))
                    { changed++; continue; }
                    bool valid = VideoPilotCommandRunner.IsSupported(media);
                    if (valid) supported++; else unsupported++;

                    if (source is not null && assetRepository is not null && evidenceRepository is not null)
                    {
                        VerifiedLocalVideo verified = new(source, asset,
                            new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant()),
                            DateTimeOffset.UtcNow);
                        RegisteredVideoRevision saved =
                            await assetRepository.RegisterVerifiedAsync(verified, token);
                        await evidenceRepository.SaveInspectionAsync(
                            BuildInspection(saved.RevisionId, media, toolHash, DateTimeOffset.UtcNow), token);
                        inserted++;
                        if (saved.NewAsset) newAssets++;
                        if (saved.NewRevision) newRevisions++;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) when (error is IOException or InvalidDataException or
                    TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    // Never print private paths, metadata, diagnostics or original content.
                    failures++;
                }
            }
            output.WriteLine(options.Apply ? "Opt-in video metadata ingestion completed." :
                "Video ingestion dry run completed: no database writes.");
            output.WriteLine($"Found {total}; local {local}; placeholders {placeholders}; unavailable {unavailable}; oversized {oversized}; beyond limit {beyondLimit}; excluded {excluded}.");
            output.WriteLine($"Attempted {attempted}; supported {supported}; unsupported {unsupported}; changed during read {changed}; failures {failures}.");
            if (options.Apply)
                output.WriteLine($"Registered {inserted}; new video assets {newAssets}; new content revisions {newRevisions}. No poster or photo processing.");
            return failures == 0 && changed == 0 ? 0 : 1;
        }
        finally
        {
            if (db is not null) await db.DisposeAsync();
        }
    }
}
