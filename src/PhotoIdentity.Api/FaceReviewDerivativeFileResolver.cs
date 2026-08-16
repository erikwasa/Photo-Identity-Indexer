using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Api;

public sealed record FaceReviewDerivativeFile(
    string Path,
    int Width,
    int Height);

/// <summary>
/// Resolves a durable face-review derivative under the configured permanent derivative root.
/// It never opens or probes the authoritative original.
/// </summary>
public sealed class FaceReviewDerivativeFileResolver
{
    private readonly SqliteFaceReviewDerivativeRepository _repository;
    private readonly ReviewProxyServingConfiguration _configuration;

    public FaceReviewDerivativeFileResolver(
        SqliteCatalogueDatabase database,
        ReviewProxyServingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(configuration);
        _repository = new SqliteFaceReviewDerivativeRepository(database);
        _configuration = configuration;
    }

    public async Task<FaceReviewDerivativeFile?> ResolveAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_configuration.RootPath))
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
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
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
            exception is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return null;
        }

        return new FaceReviewDerivativeFile(path, derivative.Width, derivative.Height);
    }
}
