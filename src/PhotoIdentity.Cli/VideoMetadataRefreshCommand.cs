using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

/// <summary>Single selected current video revision; always read-only unless explicitly acknowledged.</summary>
public sealed record VideoMetadataRefreshOptions(
    string SourceRoot, Guid RevisionId, string FfprobePath, string FfprobeSha256,
    string ConnectionVariable, string DatabaseName, int TimeoutSeconds, bool Apply)
{
    public string? VendorExifToolPath { get; init; }
    public string? VendorExifToolSha256 { get; init; }

    public static VideoMetadataRefreshOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool enabled = false, apply = false, acknowledged = false, vendorEnabled = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--enable-video" && !enabled) { enabled = true; continue; }
            if (arg == "--apply" && !apply) { apply = true; continue; }
            if (arg == "--enable-samsung-vendor-camera" && !vendorEnabled)
            { vendorEnabled = true; continue; }
            if (arg == "--acknowledge-metadata-only" && !acknowledged)
            { acknowledged = true; continue; }
            if (arg is not ("--source-root" or "--revision" or "--ffprobe" or
                            "--ffprobe-sha256" or "--postgres-connection-env" or
                            "--database" or "--timeout-seconds" or
                            "--exiftool" or "--exiftool-sha256") ||
                i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !values.TryAdd(arg, args[++i]))
                throw new ArgumentException("Unknown, repeated or incomplete metadata refresh argument.");
        }
        if (!enabled)
            throw new ArgumentException("Metadata refresh requires --enable-video.");
        if (apply != acknowledged)
            throw new ArgumentException("--apply requires --acknowledge-metadata-only, which is invalid in a dry run.");
        string Required(string key) =>
            values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException($"Missing {key}.");
        string root = Required("--source-root");
        string ffprobe = Required("--ffprobe");
        if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(ffprobe))
            throw new ArgumentException("Source root and ffprobe must be fully qualified.");
        if (!Guid.TryParse(Required("--revision"), out Guid revision) || revision == Guid.Empty)
            throw new ArgumentException("Provide the existing Video Details revision UUID.");
        string hash = Required("--ffprobe-sha256").ToLowerInvariant();
        if (!Regex.IsMatch(hash, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Provide the approved FFprobe SHA-256 digest.");
        string variable = Required("--postgres-connection-env");
        if (!Regex.IsMatch(variable, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Provide a PostgreSQL environment variable name.");
        string database = Required("--database");
        if (!Regex.IsMatch(database, "^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Explicit PostgreSQL database name is required.");
        int timeout = values.TryGetValue("--timeout-seconds", out string? raw)
            ? int.TryParse(raw, out int n) && n is >= 1 and <= 120
                ? n : throw new ArgumentException("Timeout must be 1..120 seconds.")
            : 20;
        bool hasVendorPath = values.TryGetValue("--exiftool", out string? vendorTool);
        bool hasVendorHash = values.TryGetValue("--exiftool-sha256", out string? vendorHash);
        if (vendorEnabled != hasVendorPath || vendorEnabled != hasVendorHash)
            throw new ArgumentException(
                "Optional Samsung extraction requires --enable-samsung-vendor-camera, --exiftool and --exiftool-sha256 together.");
        if (vendorEnabled)
        {
            if (!Path.IsPathFullyQualified(vendorTool!) ||
                !Regex.IsMatch(vendorHash!, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
                throw new ArgumentException("Samsung ExifTool requires an absolute executable path and approved SHA-256.");
        }
        return new(Path.GetFullPath(root), revision, Path.GetFullPath(ffprobe),
            hash, variable, database, timeout, apply)
        {
            VendorExifToolPath = vendorEnabled ? Path.GetFullPath(vendorTool!) : null,
            VendorExifToolSha256 = vendorEnabled ? vendorHash!.ToLowerInvariant() : null
        };
    }
}

public static class VideoMetadataRefreshCommandRunner
{
    public const long MaximumSourceBytes = VideoIngestCommandRunner.MaximumSourceBytes;

    public static async Task<int> RunAsync(
        VideoMetadataRefreshOptions options, TextWriter output, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        string? connectionString = Environment.GetEnvironmentVariable(options.ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Selected PostgreSQL environment variable is not set.");
        NpgsqlConnectionStringBuilder settings = new(connectionString);
        if (settings.Host is not ("127.0.0.1" or "localhost" or "::1"))
            throw new ArgumentException("Video metadata refresh requires localhost PostgreSQL.");
        settings.Database = options.DatabaseName;
        if (!Directory.Exists(options.SourceRoot))
            throw new ArgumentException("Registered local source root does not exist.");

        // Do NOT call database.InitializeAsync here: even a dry run must not
        // migrate v39 production to v41. Operator rehearses migration separately.
        await using PostgresCatalogueDatabase database = new(settings.ConnectionString);
        await using (NpgsqlConnection check = await database.OpenConnectionAsync(token))
        await using (NpgsqlCommand version = check.CreateCommand())
        {
            version.CommandText = """
                SELECT COALESCE(MAX(version), 0)
                FROM photo_identity_schema_migrations;
                """;
            if (Convert.ToInt32(await version.ExecuteScalarAsync(token)) !=
                PostgresCatalogueDatabase.CurrentSchemaVersion)
                throw new InvalidOperationException(
                    "Catalogue schema is not the approved metadata-refresh version; do not start or migrate it here.");
        }

        PostgresVideoTechnicalMetadataRefreshRepository repository = new(database);
        AssetRevisionId revision = AssetRevisionId.From(options.RevisionId);
        VideoTechnicalMetadataRefreshTarget? target =
            await repository.GetCurrentTargetAsync(revision, options.SourceRoot, token);
        if (target is null || target.SizeBytes is <= 0 or > MaximumSourceBytes)
            throw new InvalidOperationException(
                "Selected revision is not a current, included, bounded video at the named source.");

        OneDriveVideoAssetSource source = new(target.SourceId, options.SourceRoot);
        SourceAssetReference reference = new(target.SourceId, target.SourceKey);
        string path = source.GetLocalPath(reference); // rejects online-only and downloading
        RejectSymbolicLinks(options.SourceRoot, path);

        FileInfo before = new(path);
        before.Refresh();
        if (!before.Exists || before.Length != target.SizeBytes)
            throw new InvalidOperationException("Selected local file differs in size from its catalogue revision.");
        DateTime lastWriteBefore = before.LastWriteTimeUtc;

        string ffprobePath = VideoPilotMediaTools.ValidateExecutable(options.FfprobePath);
        string verifiedToolHash = await HashFileAsync(ffprobePath, token);
        if (!string.Equals(verifiedToolHash, options.FfprobeSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("FFprobe digest is not the operator-approved SHA-256.");

        string? vendorToolPath = null, vendorToolHash = null;
        if (options.VendorExifToolPath is not null)
        {
            if (options.VendorExifToolSha256 is null)
                throw new ArgumentException("Approved ExifTool SHA-256 is missing.");
            vendorToolPath = VideoPilotMediaTools.ValidateExecutable(options.VendorExifToolPath);
            vendorToolHash = await HashFileAsync(vendorToolPath, token);
            if (vendorToolHash != options.VendorExifToolSha256)
                throw new InvalidOperationException("ExifTool does not match the operator-approved SHA-256.");
        }

        VideoPilotMediaTools tools = new(ffprobePath, TimeSpan.FromSeconds(options.TimeoutSeconds));
        VideoPilotMetadata media;
        SamsungVideoCameraMetadata? vendorCamera = null;
        string contentHash;
        try
        {
            media = await tools.ProbeAsync(path, token);
            if (vendorToolPath is not null)
                vendorCamera = await new VideoSamsungExifTool(
                    vendorToolPath, TimeSpan.FromSeconds(options.TimeoutSeconds))
                    .ProbeAsync(path, token);
            contentHash = await HashFileAsync(path, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                                 InvalidDataException or TimeoutException or
                                 OperationCanceledException or
                                 System.ComponentModel.Win32Exception or
                                 System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("Selected local video metadata inspection failed; no write was attempted.");
        }

        before.Refresh();
        if (!before.Exists || before.Length != target.SizeBytes ||
            before.LastWriteTimeUtc != lastWriteBefore ||
            !string.Equals(contentHash, target.ContentSha256, StringComparison.Ordinal) ||
            !string.Equals(verifiedToolHash, await HashFileAsync(ffprobePath, token), StringComparison.Ordinal) ||
            (vendorToolPath is not null &&
             !string.Equals(vendorToolHash, await HashFileAsync(vendorToolPath, token),
                 StringComparison.Ordinal)) ||
            await source.GetAvailabilityAsync(reference, token) != AssetAvailability.Local)
            throw new InvalidOperationException(
                "Selected source or tool changed during inspection; no refresh was written.");

        // Only the newly inspected fields are candidates for completion.
        // Preserve every pre-existing source inspection and manual action.
        VideoTechnicalMetadataRefreshFacts facts = new(
            media.CameraMake, media.CameraModel, media.VideoBitrateBps,
            media.AudioSampleRateHz, media.AudioChannels, media.AudioChannelLayout,
            verifiedToolHash, VideoIngestCommandRunner.InspectionProfile,
            DateTimeOffset.UtcNow);

        // Re-check current revision and source exclusion after expensive probing.
        VideoTechnicalMetadataRefreshTarget? latest =
            await repository.GetCurrentTargetAsync(revision, options.SourceRoot, token);
        if (latest != target)
            throw new InvalidOperationException(
                "Catalogue video revision changed during inspection; no refresh was written.");

        if (!options.Apply)
        {
            output.WriteLine("Video metadata refresh dry run passed: no database writes.");
            output.WriteLine("One selected current, locally available, SHA-256-verified video.");
            if (vendorToolPath is not null)
                output.WriteLine(vendorCamera is null
                    ? "Approved Samsung vendor tag: not present; no camera identity inferred."
                    : "Approved Samsung vendor tag: present; only missing camera fields eligible.");
            output.WriteLine("Run with --apply --acknowledge-metadata-only after the approved v41 rehearsal.");
            return 0;
        }

        // Each tool's contribution has its own source-specific audit action:
        // the v41 schema stores the physical SHA-256 and inspection profile
        // per action, so never attribute ExifTool camera evidence to FFprobe.
        int changed = await repository.FillMissingAsync(
            revision, options.SourceRoot, contentHash, facts, token);
        if (vendorCamera is not null && vendorToolHash is not null)
        {
            VideoTechnicalMetadataRefreshFacts vendorFacts = new(
                vendorCamera.CameraMake, vendorCamera.CameraModel,
                null, null, null, null,
                vendorToolHash, "wi0212-exiftool-samsung-camera-v1", DateTimeOffset.UtcNow);
            changed += await repository.FillMissingAsync(
                revision, options.SourceRoot, contentHash, vendorFacts, token);
        }
        output.WriteLine("Video metadata refresh completed for exactly one verified local revision.");
        output.WriteLine($"Previously missing technical fields populated: {changed}.");
        output.WriteLine("Existing values and manual metadata were not overwritten.");
        return 0;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    private static void RejectSymbolicLinks(string root, string selectedPath)
    {
        string rootFull = Path.GetFullPath(root);
        for (DirectoryInfo? directory = new(Path.GetDirectoryName(selectedPath)!);
             directory is not null; directory = directory.Parent)
        {
            if (directory.LinkTarget is not null)
                throw new InvalidOperationException("Symbolic-link directory is not an approved video source.");
            if (string.Equals(directory.FullName, rootFull,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                    StringComparison.Ordinal))
                break;
        }
        // OneDrive locally hydrated files may carry a cloud reparse marker.
        // Reject actual symlinks, not the OneDrive-specific hydration state.
        if (new FileInfo(selectedPath).LinkTarget is not null)
            throw new InvalidOperationException("Symbolic-link video is not an approved source.");
    }
}
