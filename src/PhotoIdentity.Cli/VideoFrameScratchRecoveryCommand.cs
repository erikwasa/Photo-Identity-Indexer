using System.Text.Json;

namespace PhotoIdentity.Cli;

/// <summary>
/// Operator-only, local scratch cleanup. No DB, original path, model, cloud,
/// source media, video jobs or frame evidence can be opened by this command.
/// </summary>
public sealed record VideoFrameScratchRecoveryOptions(
    string ScratchRoot, bool Apply = false, bool AcknowledgeDeletion = false,
    int MinimumAgeHours = 24)
{
    public static VideoFrameScratchRecoveryOptions Parse(string[] args)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        bool apply = false, acknowledgement = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply)
            {
                apply = true;
                continue;
            }
            if (args[i] == "--acknowledge-private-scratch-deletion" && !acknowledgement)
            {
                acknowledgement = true;
                continue;
            }
            if (args[i] is not ("--scratch-root" or "--minimum-age-hours") ||
                i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) ||
                !fields.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Unknown or repeated scratch recovery option.");
        }
        if (!fields.TryGetValue("--scratch-root", out string? root) ||
            string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) ||
            (OperatingSystem.IsWindows() && root.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new ArgumentException("An existing local absolute frame scratch root is required.");
        int hours = 24;
        if (fields.TryGetValue("--minimum-age-hours", out string? value) &&
            (!int.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out hours) ||
             hours is < 6 or > 720))
            throw new ArgumentException("Scratch recovery age must be between 6 and 720 hours.");
        if (acknowledgement && !apply)
            throw new ArgumentException("Deletion acknowledgment is valid only with --apply.");
        if (apply && !acknowledgement)
            throw new ArgumentException("Private scratch deletion requires explicit acknowledgment.");
        return new(Path.GetFullPath(root), apply, acknowledgement, hours);
    }
}

public sealed record VideoFrameScratchRecoveryResult(
    int OwnedWorkspaces, int RecoverableWorkspaces,
    int ActiveWorkspaces, int YoungWorkspaces,
    int RefusedWorkspaces, int RemovedWorkspaces,
    bool Applied, bool Blocked);

/// <summary>
/// Not an unattended janitor. Older legacy workspaces without a valid child
/// marker or lock are *never* deleted. The active job keeps a lock open while
/// using a workspace, and each new run uses an unrepeatable directory name.
/// </summary>
public static class VideoFrameScratchRecovery
{
    public const string WorkspacePrefix = "wi0190-evidence-";
    public const string WorkspaceMarker = ".photoidentity-frame-evidence-workspace";
    public const string WorkspaceMarkerContents = "wi0190-registered-frame-workspace-v1";
    public const string LockName = ".photoidentity-frame-evidence-active.lock";
    private const int MaximumWorkspaceEntries = 256;
    private const int MaximumChildEntries = 256;
    private const long MaximumWorkspaceBytes = 320L * 1024 * 1024;

    public static FileStream CreateActiveWorkspace(string workspace)
    {
        // Called only on a newly created random-named child, never on a
        // recovered or existing workspace. The root was previously checked
        // with the stricter *empty* ownership guard.
        if (!Directory.Exists(workspace) ||
            !Path.GetFileName(workspace).StartsWith(WorkspacePrefix, StringComparison.Ordinal) ||
            Directory.EnumerateFileSystemEntries(workspace).Any())
            throw new InvalidOperationException("A fresh private frame workspace is required.");
        File.WriteAllText(Path.Combine(workspace, WorkspaceMarker), WorkspaceMarkerContents);
        return new FileStream(Path.Combine(workspace, LockName),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);
    }

    public static VideoFrameScratchRecoveryResult InspectAndRecover(
        string scratchRoot, bool apply, DateTimeOffset nowUtc, int minimumAgeHours = 24)
    {
        if (minimumAgeHours is < 6 or > 720 || (apply && !OperatingSystem.IsWindows()))
            throw new ArgumentException("Invalid recovery age or platform.");
        string root = VideoFrameEvidenceScratchGuard.ValidateOwnedRoot(scratchRoot);
        string marker = Path.Combine(root, VideoFrameEvidenceScratchGuard.MarkerName);
        string[] children = Directory.GetFileSystemEntries(root);
        if (children.Length > MaximumWorkspaceEntries + 1)
            throw new InvalidOperationException("Too many entries for bounded frame scratch recovery.");

        var candidates = new List<string>();
        int owned = 0, active = 0, young = 0, refused = 0;
        // A suspicious or unknown sibling makes the entire operation
        // fail closed. Never delete a subset of a root that is not exclusively
        // known to be ours.
        foreach (string path in children)
        {
            if (StringComparer.Ordinal.Equals(path, marker)) continue;
            if (!Directory.Exists(path) ||
                !Path.GetFileName(path).StartsWith(WorkspacePrefix, StringComparison.Ordinal) ||
                !IsCanonicalWorkspaceName(Path.GetFileName(path)) ||
                !TryInspectTree(path, out DateTime markerModifiedUtc))
            {
                refused++;
                continue;
            }

            owned++;
            // Marker age, not the directory's last-write time: the latter
            // can change during an active job. Future timestamps fail closed.
            DateTimeOffset modified = new(markerModifiedUtc, TimeSpan.Zero);
            if (modified > nowUtc ||
                nowUtc - modified < TimeSpan.FromHours(minimumAgeHours))
            {
                young++;
                continue;
            }
            if (!CanOpenInactiveLock(path))
            {
                active++;
                continue;
            }
            candidates.Add(path);
        }

        int deleted = 0;
        bool blocked = refused != 0;
        if (apply && !blocked)
        {
            foreach (string path in candidates)
            {
                // Second check immediately before each deletion. The same
                // GUID child cannot be activated by the producer after crash;
                // new runs always create a distinct random child.
                if (!TryInspectTree(path, out DateTime markerTime) ||
                    new DateTimeOffset(markerTime, TimeSpan.Zero) > nowUtc -
                        TimeSpan.FromHours(minimumAgeHours) ||
                    !CanOpenInactiveLock(path))
                {
                    blocked = true;
                    break;
                }
                Directory.Delete(path, recursive: true);
                deleted++;
            }
        }
        return new(owned, candidates.Count, active, young, refused, deleted,
            apply, blocked);
    }

    private static bool IsCanonicalWorkspaceName(string name)
    {
        if (!name.StartsWith(WorkspacePrefix, StringComparison.Ordinal)) return false;
        string id = name[WorkspacePrefix.Length..];
        return id.Length == 32 && id.All(c => c is >= '0' and <= '9'
            or >= 'a' and <= 'f') && Guid.TryParseExact(id, "N", out Guid guid) &&
            guid != Guid.Empty;
    }

    private static bool TryInspectTree(string workspace, out DateTime markerTime)
    {
        markerTime = default;
        try
        {
            if (!Directory.Exists(workspace) ||
                (File.GetAttributes(workspace) & FileAttributes.ReparsePoint) != 0)
                return false;
            string marker = Path.Combine(workspace, WorkspaceMarker);
            string lockPath = Path.Combine(workspace, LockName);
            if (!File.Exists(marker) || !File.Exists(lockPath) ||
                (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(marker).Length != WorkspaceMarkerContents.Length ||
                File.ReadAllText(marker) != WorkspaceMarkerContents)
                return false;

            // Depth-first without following any symlinks, mount points or
            // junctions. A capped file count and byte sum limits deletion.
            Stack<string> pending = new();
            pending.Push(workspace);
            int entries = 0;
            long bytes = 0;
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (++entries > MaximumChildEntries)
                        return false;
                    FileAttributes attr = File.GetAttributes(entry);
                    if ((attr & FileAttributes.ReparsePoint) != 0)
                        return false;
                    if ((attr & FileAttributes.Directory) != 0)
                        pending.Push(entry);
                    else
                    {
                        bytes = checked(bytes + new FileInfo(entry).Length);
                        if (bytes > MaximumWorkspaceBytes)
                            return false;
                    }
                }
            }
            markerTime = File.GetLastWriteTimeUtc(marker);
            return markerTime.Kind == DateTimeKind.Utc;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or OverflowException)
        {
            return false;
        }
    }

    private static bool CanOpenInactiveLock(string workspace)
    {
        try
        {
            using FileStream unused = new(Path.Combine(workspace, LockName),
                FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

public static class VideoFrameScratchRecoveryCommandRunner
{
    public static int Run(VideoFrameScratchRecoveryOptions options, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        if (options.Apply && !options.AcknowledgeDeletion)
            throw new ArgumentException("Private scratch deletion requires explicit acknowledgment.");
        VideoFrameScratchRecoveryResult report;
        try
        {
            report = VideoFrameScratchRecovery.InspectAndRecover(
                options.ScratchRoot, options.Apply,
                DateTimeOffset.UtcNow, options.MinimumAgeHours);
        }
        catch (Exception error) when (error is IOException or
            UnauthorizedAccessException or InvalidOperationException)
        {
            // Native filesystem exceptions can include private directory or
            // scratch-file names; never echo those from a CLI failure.
            throw new InvalidOperationException(
                "Owned frame scratch recovery was refused or could not be completed safely.");
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = "wi0190-frame-scratch-recovery-v1",
            dryRun = !options.Apply,
            rootMarkerVerified = true,
            minimumAgeHours = options.MinimumAgeHours,
            report,
            originalsOpened = false, oneDriveHydrationRequested = false,
            captionsReturned = false, embeddingsReturned = false,
            videoJobsEnqueued = false, schemaChanged = false,
            databaseOpened = false, azureInvoked = false,
            activeWorkspacesDeleted = false
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return report.Blocked ? 1 : 0;
    }
}
