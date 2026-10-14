using System.Runtime.CompilerServices;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Source.OneDriveSync;

/// <summary>
/// Explicit video-only OneDrive discovery. Never extends the photo scanner's supported
/// extensions and never opens source bytes during enumeration.
/// </summary>
public sealed class OneDriveVideoAssetSource : IAssetSource
{
    private readonly IOneDriveFileStatusProvider _statuses;
    private readonly StringComparison _comparison;

    public OneDriveVideoAssetSource(SourceId sourceId, string rootPath)
        : this(sourceId, rootPath, new OneDriveFileAttributeStatusProvider())
    {
    }

    internal OneDriveVideoAssetSource(SourceId sourceId, string rootPath, IOneDriveFileStatusProvider statuses)
    {
        if (sourceId.IsEmpty) throw new ArgumentException("Source ID must be set.", nameof(sourceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _statuses = statuses ?? throw new ArgumentNullException(nameof(statuses));
        SourceId = sourceId;
        RootPath = Path.GetFullPath(rootPath);
        _comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public SourceId SourceId { get; }
    public string RootPath { get; }

    public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
        SourceScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        string root = ResolvePath(options.RelativeRoot ?? "");
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("Video source directory does not exist.");
        }

        Queue<string> pending = new();
        pending.Enqueue(root);
        while (pending.TryDequeue(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string path in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string mediaType = Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".mov" => "video/quicktime",
                    ".mp4" => "video/mp4",
                    _ => "",
                };
                if (mediaType.Length == 0) continue;
                string key = Path.GetRelativePath(RootPath, path).Replace('\\', '/');
                OneDriveFileStatus status = _statuses.GetStatus(path);
                // Enumerating FileInfo reads filesystem metadata, never content.
                FileInfo info = new(path);
                long length;
                DateTime lastWrite;
                try
                {
                    length = info.Length;
                    lastWrite = info.LastWriteTimeUtc;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                yield return new SourceAsset(
                    new SourceAssetReference(SourceId, key),
                    key, mediaType, length, lastWrite, status.Availability);
            }

            if (options.Recursive)
            {
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Never traverse directory junctions, symlinks, or OneDrive directory reparse points.
                    if (OneDriveSyncAssetSource.ShouldTraverseDirectory(File.GetAttributes(child)))
                        pending.Enqueue(child);
                }
            }
        }
        await Task.CompletedTask;
    }

    public Task<AssetAvailability> GetAvailabilityAsync(
        SourceAssetReference asset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_statuses.GetStatus(ResolveVideoPath(asset)).Availability);
    }

    public Task<Stream> OpenContentAsync(SourceAssetReference asset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = ResolveVideoPath(asset);
        AssetAvailability availability = _statuses.GetStatus(path).Availability;
        return availability switch
        {
            AssetAvailability.Local => Task.FromResult<Stream>(new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan)),
            AssetAvailability.OnlineOnly or AssetAvailability.Downloading =>
                throw new OneDriveHydrationRequiredException(asset, availability),
            AssetAvailability.Unavailable => throw new FileNotFoundException("Video source unavailable."),
            _ => throw new IOException("Video source availability could not be established."),
        };
    }

    /// <summary>Only the caller-controlled video pilot receives this local path.</summary>
    public string GetLocalPath(SourceAssetReference asset)
    {
        string path = ResolveVideoPath(asset);
        if (_statuses.GetStatus(path).Availability != AssetAvailability.Local)
            throw new IOException("Video is not confirmed locally available.");
        return path;
    }

    private string ResolveVideoPath(SourceAssetReference asset)
    {
        if (asset.SourceId != SourceId) throw new ArgumentException("Incorrect source ID.", nameof(asset));
        if (Path.GetExtension(asset.ItemKey) is not string extension ||
            (extension.ToLowerInvariant() is not (".mov" or ".mp4")))
            throw new ArgumentException("Unsupported video extension.", nameof(asset));
        return ResolvePath(asset.ItemKey);
    }

    private string ResolvePath(string relative)
    {
        string resolved = Path.GetFullPath(Path.Combine(
            RootPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (resolved != RootPath &&
            !resolved.StartsWith(RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, _comparison))
            throw new ArgumentException("Source item must remain inside its configured root.");
        return resolved;
    }
}
