using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Shared, fail-closed local cache root verification for the v36 publisher
/// and a future operator-only reconciler. Does NOT create the cache root,
/// access media originals, or implicitly migrate the database.
/// </summary>
public static class VideoDerivativeCacheGuard
{
    public static async Task<string> ValidateAsync(
        PostgresCatalogueDatabase database, string cacheRoot,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (string.IsNullOrWhiteSpace(cacheRoot) ||
            !Path.IsPathFullyQualified(cacheRoot) ||
            !Directory.Exists(cacheRoot) ||
            (OperatingSystem.IsWindows() &&
             cacheRoot.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new ArgumentException("An existing absolute local video cache is required.");

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheRoot));
        AssertNoLinks(root);
        string marker = Path.Combine(root, VideoDerivativeFilePublisher.CacheMarker);
        if (!File.Exists(marker) ||
            (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(marker).Length != VideoDerivativeFilePublisher.CacheMarkerContents.Length ||
            await File.ReadAllTextAsync(marker, token) !=
                VideoDerivativeFilePublisher.CacheMarkerContents)
            throw new InvalidOperationException("Video cache ownership cannot be verified.");

        // Check known catalogue source roots without touching any source.
        // Relative/synthetic catalogued locators have no local filesystem
        // identity, and are deliberately not resolved using a current folder.
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand query = connection.CreateCommand();
        query.CommandText = "SELECT root_locator FROM sources WHERE kind='local-folder';";
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            string locator = reader.GetString(0);
            if (!Path.IsPathFullyQualified(locator)) continue;
            string sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(locator));
            if (Overlaps(root, sourceRoot))
                throw new InvalidOperationException(
                    "Private video cache cannot overlap an archive source.");
        }
        return root;
    }

    public static void AssertNoLinks(string existingPath)
    {
        for (string? path = Path.GetFullPath(existingPath); path is not null;
             path = Path.GetDirectoryName(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked video cache entries are not permitted.");
            if (Path.GetPathRoot(path) == path) break;
        }
    }

    public static bool Overlaps(string first, string second)
    {
        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        StringComparison cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(a, b, cmp) ||
            a.StartsWith(b + Path.DirectorySeparatorChar, cmp) ||
            b.StartsWith(a + Path.DirectorySeparatorChar, cmp);
    }
}
