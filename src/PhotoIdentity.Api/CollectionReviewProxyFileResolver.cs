using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Api;

public sealed record ReviewProxyServingConfiguration(
    string? RootPath,
    string? ProfileId)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(RootPath) &&
        !string.IsNullOrWhiteSpace(ProfileId);
}

public sealed record ResolvedCollectionReviewProxy(
    CollectionPhotoFile File,
    ArchiveReviewProxyMetadata Metadata);

/// <summary>
/// Resolves durable review derivative metadata to verified paths under the configured derivative
/// root. It never falls back to or opens the authoritative source original. Excluded source copies
/// are denied before derivative metadata or files are resolved.
/// </summary>
public sealed class CollectionReviewProxyFileResolver
{
    private readonly IArchiveReviewProxyRepository _repository;
    private readonly IFaceReviewDerivativeRepository? _derivatives;
    private readonly ISourceCopyExclusionRepository? _exclusions;
    private readonly ReviewProxyServingConfiguration _configuration;

    public CollectionReviewProxyFileResolver(
        IArchiveReviewProxyRepository repository,
        ReviewProxyServingConfiguration configuration)
        : this(repository, derivatives: null, exclusions: null, configuration)
    {
    }

    public CollectionReviewProxyFileResolver(
        IArchiveReviewProxyRepository repository,
        IFaceReviewDerivativeRepository? derivatives,
        ReviewProxyServingConfiguration configuration)
        : this(repository, derivatives, exclusions: null, configuration)
    {
    }

    public CollectionReviewProxyFileResolver(
        IArchiveReviewProxyRepository repository,
        IFaceReviewDerivativeRepository? derivatives,
        ISourceCopyExclusionRepository? exclusions,
        ReviewProxyServingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(configuration);
        _repository = repository;
        _derivatives = derivatives;
        _exclusions = exclusions;
        _configuration = configuration;
    }

    public async Task<CollectionPhotoFile?> ResolveAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        ResolvedCollectionReviewProxy? resolved = await ResolveWithMetadataAsync(
            revisionId,
            cancellationToken);
        return resolved?.File;
    }

    public async Task<ResolvedCollectionReviewProxy?> ResolveWithMetadataAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        if (!_configuration.IsConfigured ||
            (_exclusions is not null && await _exclusions.IsRevisionExcludedAsync(revisionId, cancellationToken)))
        {
            return null;
        }

        ArchiveReviewProxyMetadata? proxy = await _repository.GetAsync(
            revisionId,
            _configuration.ProfileId!,
            cancellationToken);
        return proxy is null ? null : ResolveMetadata(proxy);
    }

    public async Task<IReadOnlyDictionary<AssetRevisionId, ResolvedCollectionReviewProxy>> ResolveManyWithMetadataAsync(
        IReadOnlyCollection<AssetRevisionId> revisionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        if (!_configuration.IsConfigured || revisionIds.Count == 0)
        {
            return new Dictionary<AssetRevisionId, ResolvedCollectionReviewProxy>();
        }

        AssetRevisionId[] distinct = revisionIds.Distinct().ToArray();
        IReadOnlySet<AssetRevisionId> excluded = _exclusions is null
            ? new HashSet<AssetRevisionId>()
            : await _exclusions.GetExcludedRevisionIdsAsync(distinct, cancellationToken);
        AssetRevisionId[] allowed = distinct
            .Where(id => !excluded.Contains(id))
            .ToArray();
        if (allowed.Length == 0)
        {
            return new Dictionary<AssetRevisionId, ResolvedCollectionReviewProxy>();
        }

        IReadOnlyDictionary<AssetRevisionId, ArchiveReviewProxyMetadata> proxies =
            await _repository.GetManyAsync(
                allowed,
                _configuration.ProfileId!,
                cancellationToken);
        Dictionary<AssetRevisionId, ResolvedCollectionReviewProxy> resolved = [];
        foreach ((AssetRevisionId revisionId, ArchiveReviewProxyMetadata proxy) in proxies)
        {
            ResolvedCollectionReviewProxy? file = ResolveMetadata(proxy);
            if (file is not null)
            {
                resolved[revisionId] = file;
            }
        }

        return resolved;
    }

    public async Task<FaceReviewDerivativeFile?> ResolveFaceReviewAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        if (_derivatives is null ||
            (_exclusions is not null && await _exclusions.IsFaceOccurrenceExcludedAsync(faceOccurrenceId, cancellationToken)))
        {
            return null;
        }

        return await new FaceReviewDerivativeFileResolver(_derivatives, _exclusions, _configuration)
            .ResolveAsync(faceOccurrenceId, cancellationToken);
    }

    private ResolvedCollectionReviewProxy? ResolveMetadata(ArchiveReviewProxyMetadata proxy)
    {
        string? path = ResolveStoredPath(proxy.RelativePath, proxy.EncodedByteLength);
        return path is null
            ? null
            : new ResolvedCollectionReviewProxy(
                new CollectionPhotoFile(path, "image/jpeg"),
                proxy);
    }

    private string? ResolveStoredPath(string relativePath, long encodedByteLength)
    {
        if (string.IsNullOrWhiteSpace(_configuration.RootPath))
        {
            return null;
        }

        string root;
        string path;
        try
        {
            root = Path.GetFullPath(_configuration.RootPath!);
            string platformPath = relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            path = Path.GetFullPath(Path.Combine(root, platformPath));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!path.Equals(root, comparison) && !path.StartsWith(rootPrefix, comparison))
        {
            return null;
        }

        try
        {
            FileInfo file = new(path);
            if (!file.Exists ||
                file.Length != encodedByteLength ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }

        return path;
    }
}
