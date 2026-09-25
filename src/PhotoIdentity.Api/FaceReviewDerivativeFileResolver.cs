using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public sealed record FaceReviewDerivativeFile(
    string Path,
    int Width,
    int Height);

/// <summary>
/// Resolves a durable face-review derivative under the configured permanent derivative root.
/// It never opens or probes the authoritative original and denies excluded source copies before
/// touching derivative metadata or files.
/// </summary>
public sealed class FaceReviewDerivativeFileResolver
{
    private readonly IFaceReviewDerivativeRepository _repository;
    private readonly ISourceCopyExclusionRepository? _exclusions;
    private readonly ReviewProxyServingConfiguration _configuration;

    public FaceReviewDerivativeFileResolver(
        IFaceReviewDerivativeRepository repository,
        ReviewProxyServingConfiguration configuration)
        : this(repository, exclusions: null, configuration)
    {
    }

    public FaceReviewDerivativeFileResolver(
        IFaceReviewDerivativeRepository repository,
        ISourceCopyExclusionRepository? exclusions,
        ReviewProxyServingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(configuration);
        _repository = repository;
        _exclusions = exclusions;
        _configuration = configuration;
    }

    public async Task<FaceReviewDerivativeFile?> ResolveAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_configuration.RootPath) ||
            (_exclusions is not null && await _exclusions.IsFaceOccurrenceExcludedAsync(faceOccurrenceId, cancellationToken)))
        {
            return null;
        }

        FaceReviewDerivativeRecord? derivative = await _repository.GetAsync(
            faceOccurrenceId,
            ArchiveFaceReviewDerivativeWriter.ProfileId,
            cancellationToken);
        if (derivative is null)
        {
            return null;
        }

        string root;
        string path;
        try
        {
            root = Path.GetFullPath(_configuration.RootPath!);
            string platformPath = derivative.RelativePath
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
                file.Length != derivative.EncodedByteLength ||
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

        return new FaceReviewDerivativeFile(path, derivative.Width, derivative.Height);
    }
}
