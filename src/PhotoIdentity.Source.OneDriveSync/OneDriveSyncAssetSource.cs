using System.Runtime.CompilerServices;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Source.OneDriveSync;

public sealed record OneDriveUnsupportedFile
{
    public OneDriveUnsupportedFile(string relativePath, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RelativePath = relativePath.Trim();
        Reason = reason.Trim();
    }

    public string RelativePath { get; }
    public string Reason { get; }
}

public sealed record OneDriveAvailabilityFailure
{
    public OneDriveAvailabilityFailure(string relativePath, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        RelativePath = relativePath.Trim();
        Error = error.Trim();
    }

    public string RelativePath { get; }
    public string Error { get; }
}

public sealed record OneDriveSyncScanReport(
    IReadOnlyList<SourceAsset> Assets,
    IReadOnlyList<OneDriveUnsupportedFile> UnsupportedFiles,
    IReadOnlyList<OneDriveAvailabilityFailure> AvailabilityFailures);

public sealed class OneDriveHydrationRequiredException : IOException
{
    public OneDriveHydrationRequiredException(SourceAssetReference asset, AssetAvailability availability)
        : base(
            $"OneDrive item '{asset.ItemKey}' is {availability.ToString().ToLowerInvariant()} and is not fully available locally. " +
            "Hydrate it with the OneDrive sync client before retrying.")
    {
        Asset = asset;
        Availability = availability;
    }

    public SourceAssetReference Asset { get; }
    public AssetAvailability Availability { get; }
}

public sealed class OneDriveAvailabilityException : IOException
{
    public OneDriveAvailabilityException(SourceAssetReference asset, string? error)
        : base($"OneDrive availability could not be determined for '{asset.ItemKey}': {error ?? "unknown error"}")
    {
        Asset = asset;
    }

    public SourceAssetReference Asset { get; }
}

/// <summary>
/// Enumerates supported image files from a Windows OneDrive sync root without requesting
/// credentials or opening placeholders that would trigger hydration.
/// </summary>
public sealed class OneDriveSyncAssetSource : IAssetSource
{
    private static readonly IReadOnlyDictionary<string, string> SupportedMediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
        };

    private readonly IOneDriveFileStatusProvider _statusProvider;
    private readonly StringComparison _pathComparison;

    public OneDriveSyncAssetSource(SourceId sourceId, string rootPath)
        : this(sourceId, rootPath, new OneDriveFileAttributeStatusProvider())
    {
    }

    internal OneDriveSyncAssetSource(
        SourceId sourceId,
        string rootPath,
        IOneDriveFileStatusProvider statusProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(statusProvider);
        SourceId = sourceId;
        RootPath = Path.GetFullPath(rootPath);
        _statusProvider = statusProvider;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public SourceId SourceId { get; }
    public string RootPath { get; }

    public async Task<OneDriveSyncScanReport> ScanAsync(
        SourceScanOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        string scanRoot = ResolveScanRoot(options.RelativeRoot);
        if (!Directory.Exists(scanRoot))
        {
            throw new DirectoryNotFoundException($"The OneDrive sync directory does not exist: {scanRoot}");
        }

        List<SourceAsset> assets = [];
        List<OneDriveUnsupportedFile> unsupported = [];
        List<OneDriveAvailabilityFailure> failures = [];

        foreach (string path in EnumerateFiles(scanRoot, options.Recursive, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(RootPath, path));
            string extension = Path.GetExtension(path);
            if (!SupportedMediaTypes.TryGetValue(extension, out string? mediaType))
            {
                unsupported.Add(new OneDriveUnsupportedFile(
                    relativePath,
                    string.IsNullOrEmpty(extension)
                        ? "The file has no supported extension."
                        : $"The '{extension}' extension is not supported."));
                continue;
            }

            OneDriveFileStatus status = ReadStatus(path);
            if (status.Availability == AssetAvailability.Error)
            {
                failures.Add(new OneDriveAvailabilityFailure(relativePath, status.Error ?? "unknown error"));
            }

            try
            {
                FileInfo file = new(path);
                assets.Add(new SourceAsset(
                    new SourceAssetReference(SourceId, relativePath),
                    relativePath,
                    mediaType,
                    file.Length,
                    file.LastWriteTimeUtc,
                    status.Availability));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new OneDriveAvailabilityFailure(relativePath, exception.Message));
            }
        }

        assets.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        unsupported.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        failures.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));

        await Task.CompletedTask;
        return new OneDriveSyncScanReport(assets, unsupported, failures);
    }

    public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
        SourceScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        OneDriveSyncScanReport report = await ScanAsync(options, cancellationToken);
        foreach (SourceAsset asset in report.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return asset;
        }
    }

    public Task<AssetAvailability> GetAvailabilityAsync(
        SourceAssetReference asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSource(asset);
        return Task.FromResult(ReadStatus(ResolveAssetPath(asset.ItemKey)).Availability);
    }

    public Task<Stream> OpenContentAsync(
        SourceAssetReference asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSource(asset);
        string path = ResolveAssetPath(asset.ItemKey);
        OneDriveFileStatus status = ReadStatus(path);

        return status.Availability switch
        {
            AssetAvailability.Local => Task.FromResult<Stream>(new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan)),
            AssetAvailability.OnlineOnly or AssetAvailability.Downloading =>
                throw new OneDriveHydrationRequiredException(asset, status.Availability),
            AssetAvailability.Unavailable =>
                throw new FileNotFoundException($"The OneDrive item is unavailable: {asset.ItemKey}", path),
            AssetAvailability.Error =>
                throw new OneDriveAvailabilityException(asset, status.Error),
            _ => throw new InvalidOperationException(
                $"OneDrive item '{asset.ItemKey}' has unsupported availability state {status.Availability}."),
        };
    }

    internal OneDriveFileStatus ReadStatus(string path) => _statusProvider.GetStatus(path);

    internal string ResolveAssetPath(string itemKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);
        string platformPath = itemKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.Combine(RootPath, platformPath));
        string rootPrefix = RootPath.EndsWith(Path.DirectorySeparatorChar)
            ? RootPath
            : RootPath + Path.DirectorySeparatorChar;

        if (!resolved.Equals(RootPath, _pathComparison) &&
            !resolved.StartsWith(rootPrefix, _pathComparison))
        {
            throw new ArgumentException("The source item must remain inside the configured OneDrive root.", nameof(itemKey));
        }

        return resolved;
    }

    internal void ValidateSource(SourceAssetReference asset)
    {
        if (asset.SourceId != SourceId)
        {
            throw new ArgumentException("The asset belongs to a different OneDrive source.", nameof(asset));
        }
    }

    internal static bool ShouldTraverseDirectory(FileAttributes attributes) =>
        (attributes & FileAttributes.ReparsePoint) == 0;

    private static IEnumerable<string> EnumerateFiles(
        string root,
        bool recursive,
        CancellationToken cancellationToken)
    {
        Queue<string> directories = new();
        directories.Enqueue(root);

        while (directories.TryDequeue(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            if (!recursive)
            {
                continue;
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ShouldTraverseDirectory(File.GetAttributes(child)))
                {
                    directories.Enqueue(child);
                }
            }
        }
    }

    private string ResolveScanRoot(string? relativeRoot) =>
        string.IsNullOrWhiteSpace(relativeRoot) ? RootPath : ResolveAssetPath(relativeRoot);

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');
}

internal sealed record OneDriveFileStatus(AssetAvailability Availability, string? Error = null);

internal interface IOneDriveFileStatusProvider
{
    OneDriveFileStatus GetStatus(string path);
}

internal sealed class OneDriveFileAttributeStatusProvider : IOneDriveFileStatusProvider
{
    internal const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    internal const FileAttributes Pinned = (FileAttributes)0x00080000;
    internal const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public OneDriveFileStatus GetStatus(string path)
    {
        if (!File.Exists(path))
        {
            return new OneDriveFileStatus(AssetAvailability.Unavailable);
        }

        try
        {
            return new OneDriveFileStatus(Classify(File.GetAttributes(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new OneDriveFileStatus(AssetAvailability.Error, exception.Message);
        }
    }

    internal static AssetAvailability Classify(FileAttributes attributes)
    {
        bool contentMissing = (attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;
        if (!contentMissing)
        {
            return AssetAvailability.Local;
        }

        return (attributes & Pinned) != 0
            ? AssetAvailability.Downloading
            : AssetAvailability.OnlineOnly;
    }
}
