using System.Text.Json;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Only explicit --private-report permits a file write. Output must be a brand-new
/// regular file outside the repository and both input folders.
/// </summary>
public static class VideoFacePrivateReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task WriteAsync(
        string outputPath,
        string repositoryRoot,
        string frameDirectory,
        string referenceDirectory,
        VideoFacePrivateReview report,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(frameDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceDirectory);
        ArgumentNullException.ThrowIfNull(report);

        if (!Path.IsPathFullyQualified(outputPath) ||
            !string.Equals(Path.GetExtension(outputPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The explicit report path must be an absolute JSON filename.");
        }

        string path = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The report path has no parent directory.");
        if (!Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
            File.Exists(path) ||
            IsWithin(repositoryRoot, path) ||
            IsWithin(frameDirectory, path) ||
            IsWithin(referenceDirectory, path))
        {
            throw new ArgumentException(
                "The private report must be a new file in a regular folder outside the repository and inputs.");
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        bool created = false;
        try
        {
            await using (FileStream stream = new(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                useAsync: true))
            {
                created = true;
                await stream.WriteAsync(bytes, cancellationToken);
            }
        }
        catch
        {
            if (created)
            {
                File.Delete(path);
            }

            throw;
        }
    }

    private static bool IsWithin(string parent, string file)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(parent), file);
        return relative == "." ||
            (!Path.IsPathRooted(relative) &&
             relative != ".." &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
