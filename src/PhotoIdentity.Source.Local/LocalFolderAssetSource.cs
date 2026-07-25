using System.Runtime.CompilerServices;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Source.Local;

public sealed record UnsupportedSourceFile
{
    public UnsupportedSourceFile(string relativePath, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        RelativePath = relativePath.Trim();
        Reason = reason.Trim();
    }

    public string RelativePath { get; }
    public string Reason { get; }
}

public sealed record LocalFolderScanReport(
    IReadOnlyList<SourceAsset> Assets,
    IReadOnlyList<UnsupportedSourceFile> UnsupportedFiles);

/// <summary>
/// Enumerates JPEG and PNG files from a local directory without owning catalogue persistence.
/// </summary>
public sealed class LocalFolderAssetSource : IAssetSource
{
    private static readonly IReadOnlyDictionary<string, string> SupportedMediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
        };

    private readonly StringComparison _pathComparison;

    public LocalFolderAssetSource(SourceId sourceId, string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        SourceId = sourceId;
        RootPath = Path.GetFullPath(rootPath);
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public SourceId SourceId { get; }

    public string RootPath { get; }

    public async Task<LocalFolderScanReport> ScanAsync(
        SourceScanOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        string scanRoot = ResolveScanRoot(options.RelativeRoot);
        if (!Directory.Exists(scanRoot))
        {
            throw new DirectoryNotFoundException($"The local source directory does not exist: {scanRoot}");
        }

        EnumerationOptions enumerationOptions = new()
        {
            RecurseSubdirectories = options.Recursive,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };

        List<SourceAsset> assets = [];
        List<UnsupportedSourceFile> unsupported = [];

        foreach (string path in Directory.EnumerateFiles(scanRoot, "*", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = NormalizeRelativePath(Path.GetRelativePath(RootPath, path));
            string extension = Path.GetExtension(path);
            if (!SupportedMediaTypes.TryGetValue(extension, out string? mediaType))
            {
                unsupported.Add(new UnsupportedSourceFile(
                    relativePath,
                    string.IsNullOrEmpty(extension)
                        ? "The file has no supported extension."
                        : $"The '{extension}' extension is not supported."));
                continue;
            }

            FileInfo file = new(path);
            assets.Add(new SourceAsset(
                new SourceAssetReference(SourceId, relativePath),
                relativePath,
                mediaType,
                file.Length,
                file.LastWriteTimeUtc,
                AssetAvailability.Local));
        }

        assets.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        unsupported.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));

        await Task.CompletedTask;
        return new LocalFolderScanReport(assets, unsupported);
    }

    public async IAsyncEnumerable<SourceAsset> EnumerateAsync(
        SourceScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LocalFolderScanReport report = await ScanAsync(options, cancellationToken);
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

        string path = ResolveAssetPath(asset.ItemKey);
        AssetAvailability availability = File.Exists(path)
            ? AssetAvailability.Local
            : AssetAvailability.Unavailable;
        return Task.FromResult(availability);
    }

    public Task<Stream> OpenContentAsync(
        SourceAssetReference asset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSource(asset);

        string path = ResolveAssetPath(asset.ItemKey);
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    private string ResolveScanRoot(string? relativeRoot)
    {
        if (string.IsNullOrWhiteSpace(relativeRoot))
        {
            return RootPath;
        }

        return ResolveAssetPath(relativeRoot);
    }

    private string ResolveAssetPath(string itemKey)
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
            throw new ArgumentException("The source item must remain inside the configured root.", nameof(itemKey));
        }

        return resolved;
    }

    private void ValidateSource(SourceAssetReference asset)
    {
        if (asset.SourceId != SourceId)
        {
            throw new ArgumentException("The asset belongs to a different source.", nameof(asset));
        }
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/');
}
