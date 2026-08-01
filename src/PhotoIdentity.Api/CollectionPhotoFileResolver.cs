using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Api;

public sealed record CollectionPhotoFile(string Path, string ContentType);

/// <summary>
/// Resolves opaque collection revision identifiers to locally available source photos without
/// returning source roots or relative source keys to the browser.
/// </summary>
public sealed class CollectionPhotoFileResolver
{
    private const string LocalFolderSourceKind = "local-folder";

    private readonly SqliteLocalBatchRepository _repository;

    public CollectionPhotoFileResolver(SqliteLocalBatchRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<CollectionPhotoFile?> ResolveAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        CatalogueProcessingAssetRevision? revision = await _repository.GetAssetRevisionAsync(
            revisionId,
            cancellationToken);
        if (revision is null ||
            !string.Equals(revision.SourceKind, LocalFolderSourceKind, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(revision.RootLocator) ||
            string.IsNullOrWhiteSpace(revision.SourceKey))
        {
            return null;
        }

        string root;
        string path;
        try
        {
            root = Path.GetFullPath(revision.RootLocator);
            string platformPath = revision.SourceKey
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
                file.Length != revision.SizeBytes ||
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

        string? contentType = revision.MediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" => "image/jpeg",
            "image/png" => "image/png",
            "image/webp" => "image/webp",
            _ => null,
        };
        return contentType is null ? null : new CollectionPhotoFile(path, contentType);
    }
}
