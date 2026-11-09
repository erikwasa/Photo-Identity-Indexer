using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Optional Samsung-only, FFprobe-first camera fallback shared by Archive
/// first inspection and explicit video ingestion. The tool path/hash must
/// be supplied by the local operator, never by a source file or web request.
/// </summary>
public sealed class VideoSamsungAutomaticMetadata(
    string approvedExifToolPath, string approvedExifToolSha256)
{
    public const string InspectionProfile = "wi0212-exiftool-samsung-camera-v1";
    private readonly string _executable = ValidatePath(approvedExifToolPath);
    public string ToolSha256 { get; } = ValidateDigest(approvedExifToolSha256);

    public static bool ShouldProbe(VideoPilotMetadata media)
    {
        ArgumentNullException.ThrowIfNull(media);
        return (media.CameraMake is null || media.CameraModel is null) &&
            (media.CameraMake is null ||
             string.Equals(media.CameraMake, "Samsung", StringComparison.OrdinalIgnoreCase));
    }

    public static bool Compatible(VideoPilotMetadata media, SamsungVideoCameraMetadata candidate) =>
        (media.CameraMake is null ||
         string.Equals(media.CameraMake, candidate.CameraMake, StringComparison.OrdinalIgnoreCase)) &&
        (media.CameraModel is null ||
         string.Equals(media.CameraModel, candidate.CameraModel, StringComparison.Ordinal));

    /// <summary>Enabling the setting fails before any source hydration if the tool is not approved.</summary>
    public async Task VerifyToolAsync(CancellationToken token = default)
    {
        string digest;
        await using (FileStream file = new(_executable, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            digest = Convert.ToHexString(await SHA256.HashDataAsync(file, token))
                .ToLowerInvariant();
        if (!string.Equals(digest, ToolSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Approved Samsung metadata tool digest mismatch.");
    }

    /// <summary>
    /// Never start ExifTool when FFprobe already supplied both camera fields,
    /// or when a non-Samsung make was explicitly identified. When a verified
    /// Samsung tag conflicts with an existing model, reject rather than mix.
    /// </summary>
    public async Task<SamsungVideoCameraMetadata?> ProbeIfMissingAsync(
        VideoPilotMetadata media, string localPath, TimeSpan timeout,
        CancellationToken token = default)
    {
        if (!ShouldProbe(media))
            return null;
        await VerifyToolAsync(token);
        SamsungVideoCameraMetadata? candidate =
            await new VideoSamsungExifTool(_executable, timeout)
                .ProbeAsync(localPath, token);
        await VerifyToolAsync(token);
        return candidate is not null && Compatible(media, candidate)
            ? candidate : null;
    }

    /// <summary>
    /// Separate revision-scoped, fill-only transaction and ExifTool audit:
    /// never attribute camera evidence to the FFprobe inspection profile.
    /// Existing capture dates, Place and people data are not changed.
    /// </summary>
    public Task<int> FillMissingAsync(
        PostgresCatalogueDatabase database, AssetRevisionId revisionId,
        string sourceRoot, string verifiedContentSha256,
        SamsungVideoCameraMetadata camera, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(camera);
        return new PostgresVideoTechnicalMetadataRefreshRepository(database)
            .FillMissingAsync(revisionId, sourceRoot, verifiedContentSha256,
                new VideoTechnicalMetadataRefreshFacts(
                    camera.CameraMake, camera.CameraModel,
                    null, null, null, null,
                    ToolSha256, InspectionProfile, DateTimeOffset.UtcNow), token);
    }

    private static string ValidateDigest(string hash)
    {
        if (hash is null ||
            !Regex.IsMatch(hash, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Samsung metadata tool requires a lowercase pinned SHA-256.");
        return hash;
    }

    private static string ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Samsung metadata tool requires an absolute physical executable.");
        string full = VideoPilotMediaTools.ValidateExecutable(path);
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Samsung metadata tool must not be linked.");
        for (DirectoryInfo? parent = new(Path.GetDirectoryName(full)!);
             parent is not null; parent = parent.Parent)
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Samsung metadata tool directory must not be linked.");
        }
        return full;
    }
}
