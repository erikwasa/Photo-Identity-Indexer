namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Strictly allowlisted temporary directories for video tooling and input
/// staging. A workspace marker is created before any external process starts,
/// enabling conservative operator-only cleanup after an abnormal shutdown.
/// Never pass a source archive as the scratch root.
/// </summary>
public static class VideoScratchOwnership
{
    public const string MarkerName = ".photoidentity-video-owned";
    public const string MarkerContents = "wi0188-private-scratch-v1";

    public static string Create(string scratchRoot, string prefix)
    {
        if (prefix is not ("video-tool" or "video-input"))
            throw new ArgumentException("Invalid video scratch category.", nameof(prefix));
        if (!Path.IsPathFullyQualified(scratchRoot) || !Directory.Exists(scratchRoot))
            throw new ArgumentException("An existing absolute scratch directory is required.");

        string directory = Path.Combine(scratchRoot,
            prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, MarkerName), MarkerContents);
            return directory;
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }
}

public sealed record VideoScratchSweepResult(
    int Considered, int Eligible, int Deleted, int Refused, bool DryRun);

/// <summary>
/// Conservative bounded cleanup for app-created scratch only. Age must be
/// sufficiently long that all bounded video jobs have expired. Doesn't
/// follow or delete reparse points, undocumented folders, source archives or
/// active workspaces. A new worker can invoke this during controlled startup,
/// but no hosted background janitor is registered by this checkpoint.
/// </summary>
public static class VideoScratchJanitor
{
    public static async Task<VideoScratchSweepResult> SweepAsync(
        string scratchRoot, TimeSpan minimumAge, int maximumDirectories = 64,
        bool apply = false, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(scratchRoot) || !Directory.Exists(scratchRoot))
            throw new ArgumentException("Scratch root must exist and be absolute.");
        if (OperatingSystem.IsWindows() &&
            scratchRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Network scratch is not allowed.");
        if (minimumAge < TimeSpan.FromHours(24) ||
            maximumDirectories is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(minimumAge),
                "Stale scratch cleanup requires at least 24 hours and a bounded scan.");
        for (string? dir = Path.GetFullPath(scratchRoot); dir is not null;
            dir = Path.GetDirectoryName(dir))
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Scratch root may not follow directory links.");
            if (Path.GetPathRoot(dir) == dir) break;
        }

        int considered = 0, eligible = 0, deleted = 0, refused = 0;
        DateTime cutoff = DateTime.UtcNow - minimumAge;
        foreach (string directory in Directory.EnumerateDirectories(scratchRoot))
        {
            token.ThrowIfCancellationRequested();
            if (++considered > maximumDirectories)
                throw new InvalidOperationException(
                    "Too many scratch candidates; operator inspection required.");
            string name = Path.GetFileName(directory);
            if (!IsManagedDirectory(name) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                refused++;
                continue;
            }
            string marker = Path.Combine(directory, VideoScratchOwnership.MarkerName);
            if (!File.Exists(marker) ||
                (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(marker).Length != VideoScratchOwnership.MarkerContents.Length ||
                File.GetLastWriteTimeUtc(marker) >= cutoff ||
                !string.Equals(await File.ReadAllTextAsync(marker, token),
                    VideoScratchOwnership.MarkerContents, StringComparison.Ordinal) ||
                !ContentsAreSafe(directory, limit: 4096))
            {
                refused++;
                continue;
            }

            eligible++;
            if (apply)
            {
                Directory.Delete(directory, recursive: true);
                deleted++;
            }
        }
        return new(considered, eligible, deleted, refused, !apply);
    }

    private static bool IsManagedDirectory(string name)
    {
        foreach (string prefix in new[] { "video-tool-", "video-input-" })
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.Length == prefix.Length + 32 &&
                name.Skip(prefix.Length).All(c => c is >= '0' and <= '9'
                    or >= 'a' and <= 'f'))
                return true;
        }
        return false;
    }

    private static bool ContentsAreSafe(string directory, int limit)
    {
        Stack<string> dirs = new();
        dirs.Push(directory);
        int count = 0;
        while (dirs.TryPop(out string? next))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(next))
            {
                if (++count > limit) return false;
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return false;
                if ((attributes & FileAttributes.Directory) != 0)
                    dirs.Push(entry);
            }
        }
        return true;
    }
}
