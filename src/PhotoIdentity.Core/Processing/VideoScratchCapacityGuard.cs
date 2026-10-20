namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Fail-closed *admission* check for video-only scratch volumes. This is not
/// a disk quota: free space can change after the check, so an eventual worker
/// still needs an OS-enforced quota or an equivalent isolated volume.
/// </summary>
public static class VideoScratchCapacityGuard
{
    public const long MinimumHeadroomBytes = 512L * 1024 * 1024;
    private const long MaximumEstimateBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>
    /// Require the full estimated video allocation plus 512 MiB of unrelated
    /// filesystem headroom before starting a copy or bounded video tool.
    /// Reject network, linked and non-fixed volumes rather than guessing their
    /// available local capacity. No filesystem path is included in errors.
    /// </summary>
    public static void EnsureCapacity(string scratchRoot, long estimatedBytes)
    {
        if (estimatedBytes is < 1 or > MaximumEstimateBytes)
            throw new ArgumentOutOfRangeException(nameof(estimatedBytes),
                "Video scratch estimates must be positive and bounded.");
        if (!Path.IsPathFullyQualified(scratchRoot) ||
            !Directory.Exists(scratchRoot) ||
            (OperatingSystem.IsWindows() &&
             scratchRoot.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new ArgumentException(
                "Video scratch requires an existing local absolute directory.",
                nameof(scratchRoot));

        try
        {
            string full = Path.GetFullPath(scratchRoot);
            for (string? path = full; path is not null;
                 path = Path.GetDirectoryName(path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked video scratch volumes are not allowed.");
                if (Path.GetPathRoot(path) == path) break;
            }

            string root = Path.GetPathRoot(full)
                ?? throw new IOException("Video scratch volume cannot be identified.");
            DriveInfo drive = new(root);
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed ||
                !HasCapacity(drive.AvailableFreeSpace, estimatedBytes))
                throw new IOException("Video scratch volume lacks reserved free space.");
        }
        catch (Exception ex) when (
            ex is ArgumentException or UnauthorizedAccessException or IOException
                or System.Security.SecurityException)
        {
            // Avoid retaining OS exception messages that might include a
            // private scratch path or volume name.
            throw new IOException(
                "Video scratch volume is unavailable or lacks reserved space.");
        }
    }

    /// <summary>Pure, overflow-safe capacity calculation for tests/admission.</summary>
    public static bool HasCapacity(long availableBytes, long estimatedBytes,
        long headroomBytes = MinimumHeadroomBytes)
    {
        if (availableBytes < 0 || estimatedBytes is < 1 or > MaximumEstimateBytes ||
            headroomBytes < MinimumHeadroomBytes)
            return false;
        return availableBytes >= estimatedBytes &&
            availableBytes - estimatedBytes >= headroomBytes;
    }
}
