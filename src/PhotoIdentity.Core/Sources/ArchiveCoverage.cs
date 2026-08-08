namespace PhotoIdentity.Core.Sources;

/// <summary>
/// Normalizes recursively included archive folders while keeping source identity rooted at one permanent path.
/// </summary>
public static class ArchiveCoverage
{
    public static IReadOnlyList<string> NormalizeIncludedFolders(IEnumerable<string> relativeFolders)
    {
        ArgumentNullException.ThrowIfNull(relativeFolders);

        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        string[] candidates = relativeFolders
            .Select(NormalizeRelativeFolder)
            .Distinct(comparer)
            .OrderBy(static value => value.Count(character => character == '/'))
            .ThenBy(static value => value.Length)
            .ThenBy(value => value, comparer)
            .ToArray();

        List<string> normalized = [];
        foreach (string candidate in candidates)
        {
            if (normalized.Any(parent => Covers(parent, candidate, comparer)))
            {
                continue;
            }

            normalized.Add(candidate);
        }

        return normalized;
    }

    public static string NormalizeRelativeFolder(string relativeFolder)
    {
        ArgumentNullException.ThrowIfNull(relativeFolder);
        string value = relativeFolder.Trim();
        if (value.Length == 0 || value == ".")
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(value) || value.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Archive folders must be relative to the configured source root.", nameof(relativeFolder));
        }

        string[] segments = value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Archive folders may not contain '.' or '..' path segments.", nameof(relativeFolder));
        }

        return string.Join('/', segments);
    }

    public static bool Covers(string includedFolder, string relativePath)
    {
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return Covers(
            NormalizeRelativeFolder(includedFolder),
            NormalizeRelativeFolder(relativePath),
            comparer);
    }

    private static bool Covers(string includedFolder, string relativePath, StringComparer comparer)
    {
        if (includedFolder.Length == 0)
        {
            return true;
        }

        if (comparer.Equals(includedFolder, relativePath))
        {
            return true;
        }

        return relativePath.Length > includedFolder.Length &&
               relativePath[includedFolder.Length] == '/' &&
               comparer.Equals(
                   includedFolder,
                   relativePath[..includedFolder.Length]);
    }
}
