using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Source.OneDriveSync;

public sealed class StagingVerificationException : IOException
{
    public StagingVerificationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Creates content-addressed, independently verified staging copies for locally hydrated
/// OneDrive items. Cleanup only removes files carrying a matching verification manifest.
/// </summary>
public sealed class OneDriveSyncAssetStager : IAssetStager
{
    public const string VerificationManifestSuffix = ".photoidentity-stage.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly OneDriveSyncAssetSource _source;
    private readonly StringComparison _pathComparison;

    public OneDriveSyncAssetStager(OneDriveSyncAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public async Task<StagedAsset> StageAsync(
        SourceAssetReference asset,
        StagingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.VerifyContentHash)
        {
            throw new ArgumentException(
                "OneDrive staging requires content-hash verification.",
                nameof(options));
        }

        _source.ValidateSource(asset);
        string sourcePath = _source.ResolveAssetPath(asset.ItemKey);
        OneDriveFileStatus status = _source.ReadStatus(sourcePath);
        switch (status.Availability)
        {
            case AssetAvailability.Local:
                break;
            case AssetAvailability.OnlineOnly:
            case AssetAvailability.Downloading:
                throw new OneDriveHydrationRequiredException(asset, status.Availability);
            case AssetAvailability.Unavailable:
                throw new FileNotFoundException($"The OneDrive item is unavailable: {asset.ItemKey}", sourcePath);
            case AssetAvailability.Error:
                throw new OneDriveAvailabilityException(asset, status.Error);
            default:
                throw new InvalidOperationException(
                    $"OneDrive item '{asset.ItemKey}' has unsupported availability state {status.Availability}.");
        }

        string targetDirectory = Path.GetFullPath(options.TargetDirectory);
        EnsureTargetOutsideSource(targetDirectory);
        Directory.CreateDirectory(targetDirectory);
        EnsureNoReparsePointAncestors(targetDirectory);

        string temporaryPath = Path.Combine(targetDirectory, $".{Guid.NewGuid():N}.partial");
        try
        {
            CopyFingerprint copied = await CopyAndHashAsync(
                asset,
                temporaryPath,
                cancellationToken);
            CopyFingerprint verified = await HashFileAsync(temporaryPath, cancellationToken);
            if (copied.SizeBytes != verified.SizeBytes || copied.ContentHash != verified.ContentHash)
            {
                throw new StagingVerificationException(
                    $"The staged copy for '{asset.ItemKey}' did not match the bytes copied from OneDrive.");
            }

            string extension = SafeExtension(Path.GetExtension(sourcePath));
            string destinationPath = Path.Combine(
                targetDirectory,
                $"{copied.ContentHash}{extension}");
            if (File.Exists(destinationPath))
            {
                CopyFingerprint existing = await HashFileAsync(destinationPath, cancellationToken);
                if (existing.SizeBytes != copied.SizeBytes || existing.ContentHash != copied.ContentHash)
                {
                    throw new StagingVerificationException(
                        $"Existing staging file '{destinationPath}' does not match its content-addressed name.");
                }

                File.Delete(temporaryPath);
            }
            else
            {
                File.Move(temporaryPath, destinationPath);
            }

            VerifiedStageManifest manifest = new(
                SchemaVersion: 1,
                SourceId: asset.SourceId.ToString(),
                ItemKey: asset.ItemKey,
                FileName: Path.GetFileName(destinationPath),
                SizeBytes: copied.SizeBytes,
                Sha256: copied.ContentHash.ToString(),
                VerifiedAtUtc: DateTimeOffset.UtcNow);
            await WriteManifestAsync(destinationPath, manifest, cancellationToken);

            return new StagedAsset(
                asset,
                destinationPath,
                copied.SizeBytes,
                copied.ContentHash);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task CleanupAsync(
        StagedAsset stagedAsset,
        StagingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stagedAsset);
        ArgumentNullException.ThrowIfNull(options);
        _source.ValidateSource(stagedAsset.Source);
        cancellationToken.ThrowIfCancellationRequested();

        string targetDirectory = Path.GetFullPath(options.TargetDirectory);
        EnsureTargetOutsideSource(targetDirectory);
        EnsureNoReparsePointAncestors(targetDirectory);

        string stagedPath = Path.GetFullPath(stagedAsset.LocalPath);
        if (!IsSameOrDescendant(stagedPath, targetDirectory))
        {
            throw new StagingVerificationException(
                "Cleanup refused a file outside the configured staging directory.");
        }

        if (IsSameOrDescendant(stagedPath, _source.RootPath))
        {
            throw new StagingVerificationException(
                "Cleanup refused a path inside the OneDrive source root.");
        }

        if (!File.Exists(stagedPath))
        {
            return;
        }

        if ((File.GetAttributes(stagedPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new StagingVerificationException(
                "Cleanup refused a staging file represented by a reparse point.");
        }

        string manifestPath = stagedPath + VerificationManifestSuffix;
        if (!File.Exists(manifestPath))
        {
            throw new StagingVerificationException(
                "Cleanup refused an unverified staging file with no verification manifest.");
        }

        VerifiedStageManifest manifest = await ReadManifestAsync(manifestPath, cancellationToken);
        ValidateManifest(manifest, stagedAsset, stagedPath);
        CopyFingerprint current = await HashFileAsync(stagedPath, cancellationToken);
        if (current.SizeBytes != stagedAsset.SizeBytes || current.ContentHash != stagedAsset.ContentHash)
        {
            throw new StagingVerificationException(
                "Cleanup refused a staging file whose current bytes no longer match the verified fingerprint.");
        }

        File.Delete(stagedPath);
        File.Delete(manifestPath);
    }

    private async Task<CopyFingerprint> CopyAndHashAsync(
        SourceAssetReference asset,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using Stream source = await _source.OpenContentAsync(asset, cancellationToken);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long size = 0;
        try
        {
            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                size = checked(size + read);
            }

            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new CopyFingerprint(size, Digest(hash.GetHashAndReset()));
    }

    private static async Task<CopyFingerprint> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new CopyFingerprint(stream.Length, Digest(hash));
    }

    private static async Task WriteManifestAsync(
        string stagedPath,
        VerifiedStageManifest manifest,
        CancellationToken cancellationToken)
    {
        string manifestPath = stagedPath + VerificationManifestSuffix;
        string temporaryManifest = manifestPath + $".{Guid.NewGuid():N}.partial";
        try
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            await File.WriteAllBytesAsync(temporaryManifest, json, cancellationToken);
            File.Move(temporaryManifest, manifestPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryManifest))
            {
                File.Delete(temporaryManifest);
            }
        }
    }

    private static async Task<VerifiedStageManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        byte[] json = await File.ReadAllBytesAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<VerifiedStageManifest>(json, JsonOptions)
            ?? throw new StagingVerificationException("The staging verification manifest was empty.");
    }

    private static void ValidateManifest(
        VerifiedStageManifest manifest,
        StagedAsset stagedAsset,
        string stagedPath)
    {
        bool valid = manifest.SchemaVersion == 1 &&
            manifest.SourceId == stagedAsset.Source.SourceId.ToString() &&
            manifest.ItemKey == stagedAsset.Source.ItemKey &&
            manifest.FileName == Path.GetFileName(stagedPath) &&
            manifest.SizeBytes == stagedAsset.SizeBytes &&
            manifest.Sha256 == stagedAsset.ContentHash.ToString();
        if (!valid)
        {
            throw new StagingVerificationException(
                "Cleanup refused a staging file whose verification manifest did not match the requested asset.");
        }
    }

    private void EnsureTargetOutsideSource(string targetDirectory)
    {
        if (IsSameOrDescendant(targetDirectory, _source.RootPath))
        {
            throw new ArgumentException(
                "The staging directory must remain outside the OneDrive source root.",
                nameof(targetDirectory));
        }
    }

    private static void EnsureNoReparsePointAncestors(string directory)
    {
        DirectoryInfo? current = new(directory);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new StagingVerificationException(
                    $"The staging path cannot traverse reparse-point directory '{current.FullName}'.");
            }

            current = current.Parent;
        }
    }

    private bool IsSameOrDescendant(string candidate, string root)
    {
        string normalizedCandidate = Path.GetFullPath(candidate);
        string normalizedRoot = Path.GetFullPath(root);
        string rootPrefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.Equals(normalizedRoot, _pathComparison) ||
            normalizedCandidate.StartsWith(rootPrefix, _pathComparison);
    }

    private static string SafeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) ||
            extension.Length > 10 ||
            extension[0] != '.' ||
            extension.Skip(1).Any(character => !char.IsLetterOrDigit(character)))
        {
            return ".bin";
        }

        return extension.ToLowerInvariant();
    }

    private static Sha256Digest Digest(ReadOnlySpan<byte> hash) =>
        new(Convert.ToHexString(hash).ToLowerInvariant());

    private sealed record CopyFingerprint(long SizeBytes, Sha256Digest ContentHash);

    private sealed record VerifiedStageManifest(
        int SchemaVersion,
        string SourceId,
        string ItemKey,
        string FileName,
        long SizeBytes,
        string Sha256,
        DateTimeOffset VerifiedAtUtc);
}
