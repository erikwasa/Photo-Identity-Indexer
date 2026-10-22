using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Cli;

/// <summary>
/// Separate, synthetic-only AppContainer file-access feasibility experiment.
/// NEVER accepts an original video, arbitrary executable, source key, job ID,
/// catalogue connection or any video-worker flag.
/// </summary>
public sealed record VideoAppContainerLabOptions(string ScratchRoot, bool Apply)
{
    public static VideoAppContainerLabOptions Parse(string[] args)
    {
        string? root = null;
        bool apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--apply" && !apply) { apply = true; continue; }
            if (args[i] == "--scratch-root" && root is null &&
                i + 1 < args.Length && !args[i + 1].StartsWith("--",
                    StringComparison.Ordinal))
            {
                root = args[++i];
                continue;
            }
            throw new ArgumentException(
                "AppContainer lab only accepts a marked local scratch root and an optional --apply.");
        }
        if (root is null || !Path.IsPathFullyQualified(root) ||
            (OperatingSystem.IsWindows() &&
             root.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new ArgumentException("AppContainer lab requires a local absolute scratch root.");
        return new(Path.GetFullPath(root), apply);
    }
}

public static class VideoAppContainerLabCommandRunner
{
    public const string Protocol = "wi0188-synthetic-appcontainer-feasibility-v1";
    private const string AllowedContent = "wi0188-lab-public-generated";
    private const string PrivateContent = "wi0188-lab-restricted-generated";
    private const string ProfileLimit = "profile-unavailable";
    private const string LaunchLimit = "sandbox-launch-unavailable";
    private const string GrantLimit = "sandbox-acl-unavailable";
    private const string DenialFailed = "ungranted-read-not-denied";
    private const string CleanupFailed = "sandbox-cleanup-unverified";

    public static async Task<int> RunAsync(VideoAppContainerLabOptions options,
        TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        // This existing guard demands a clean marked directory BELOW the OS
        // temp directory and rejects all non-marker contents and junctions.
        string root = VideoSyntheticProxyPilotCommandRunner.ValidateRoot(
            options.ScratchRoot);
        // cmd.exe is deliberately used as a fixed synthetic ACL probe.
        // Reject shell metacharacters in the owned root before constructing
        // any command line; no user media paths are ever accepted.
        if (root.IndexOfAny(['%', '!', '^', '&', '|', '<', '>', '"']) >= 0)
            throw new ArgumentException(
                "AppContainer lab root contains unsupported command metacharacters.");
        if (!options.Apply)
        {
            Write(output, true, "not-run", false, false, false, true);
            return 0;
        }
        if (!OperatingSystem.IsWindows())
        {
            Write(output, false, LaunchLimit, false, false, false, true);
            return 1;
        }

        string working = Path.Combine(root, "sandbox-working");
        string allowed = Path.Combine(root, "sandbox-allowed");
        string blocked = Path.Combine(root, "sandbox-ungranted");
        string state = ProfileLimit;
        bool tokenVerified = false;
        bool allowedVerified = false;
        bool forbiddenDenied = false;
        bool cleaned = true;
        WindowsVideoAppContainerProfile? profile = null;
        DirectorySecurity? originalLabRootAcl = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The profile factory independently requires ONLY the original
            // owned marker, so create the disposable SID before adding any
            // test directories. Its ACL methods accept only fixed lab paths.
            profile = WindowsVideoAppContainerProfile.CreateSyntheticLab(root);
            state = GrantLimit;
            // Only newly created directories and known synthetic bytes.
            foreach (string path in new[] { working, allowed, blocked })
            {
                if (Directory.Exists(path) || File.Exists(path))
                    throw new InvalidOperationException(
                        "Synthetic lab contained an unexpected existing entry.");
                Directory.CreateDirectory(path);
            }
            VideoScratchCapacityGuard.EnsureCapacity(working, 32 * 1024 * 1024);
            // The marked root survives this experiment. Preserve its exact
            // original DACL before temporarily granting only traversal.
            originalLabRootAcl = new DirectoryInfo(root)
                .GetAccessControl(AccessControlSections.Access);
            profile.GrantOwnedRootTraverse(root);
            profile.GrantOwnedDirectory(allowed, readOnly: true);
            profile.GrantOwnedDirectory(working, readOnly: false);

            // Write files only AFTER the directory ACEs are installed, so
            // the generated file inherits the AppContainer's requested
            // read-only access on Windows. The separate sibling gets no ACE.
            string accessible = Path.Combine(allowed, "generated-public.txt");
            string inaccessible = Path.Combine(blocked, "generated-restricted.txt");
            await File.WriteAllTextAsync(accessible, AllowedContent, cancellationToken);
            await File.WriteAllTextAsync(inaccessible, PrivateContent, cancellationToken);

            // Windows system cmd.exe is used ONLY to probe access to these
            // two known generated text files. Never allow caller-selected
            // executables or FFmpeg media arguments in this experiment.
            string cmd = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.System), "cmd.exe");
            if (!File.Exists(cmd) ||
                (File.GetAttributes(cmd) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(
                    "Native synthetic system-command probe is unavailable.");
            await using FileStream systemBinary = new(cmd, FileMode.Open,
                FileAccess.Read, FileShare.Read);
            string hash = Convert.ToHexString(
                await SHA256.HashDataAsync(systemBinary, cancellationToken))
                .ToLowerInvariant();

            VideoToolExecutionBudget budget = new(
                MaximumWallTime: TimeSpan.FromSeconds(15),
                MaximumTemporaryBytes: 1024 * 1024,
                MaximumTemporaryEntries: 8,
                InspectionInterval: TimeSpan.FromMilliseconds(100),
                WindowsJobLimits: new WindowsVideoJobLimits(
                    MaximumJobMemoryMiB: 512,
                    MaximumProcesses: 1,
                    MaximumCpuPercent: 25,
                    MaximumObservedReadBytes: 64L * 1024 * 1024,
                    MaximumObservedWriteBytes: 16L * 1024 * 1024));
            var supervisor = new VideoToolProcessSupervisor();

            state = LaunchLimit;
            VideoToolExecutionResult positive = await supervisor.RunAsync(
                cmd, hash, working,
                workspace =>
                [
                    "/d", "/c", "type", accessible, ">",
                    Path.Combine(workspace, "copied.txt")
                ],
                budget, cancellationToken,
                afterSuccessfulExit: async (workspace, token) =>
                {
                    string copy = Path.Combine(workspace, "copied.txt");
                    if (!File.Exists(copy) ||
                        new FileInfo(copy).Length > 100 ||
                        (File.GetAttributes(copy) & FileAttributes.ReparsePoint) != 0)
                        return;
                    string value = await File.ReadAllTextAsync(copy, token);
                    allowedVerified = value.TrimEnd('\r', '\n') == AllowedContent;
                },
                syntheticAppContainer: profile);
            tokenVerified = true; // launcher checked TokenIsAppContainer before ResumeThread
            if (positive.ExitCode != 0 || !allowedVerified)
            {
                state = "allowed-read-write-unavailable";
            }
            else
            {
                // Do not grant the AppContainer SID to this sibling; it has
                // only a non-inheriting Traverse ACE on the lab root.
                // A successful read is a security FAIL, never a pass.
                bool didCopyForbidden = false;
                bool denialMarkerObserved = false;
                VideoToolExecutionResult negative = await supervisor.RunAsync(
                    cmd, hash, working,
                    workspace =>
                    [
                        "/d", "/c", "type", inaccessible, ">",
                        Path.Combine(workspace, "forbidden-copied.txt"),
                        "&", "if", "errorlevel", "1", "echo", "denied", ">",
                        Path.Combine(workspace, "denial-marker.txt")
                    ],
                    budget, cancellationToken,
                    afterSuccessfulExit: async (workspace, token) =>
                    {
                        string result = Path.Combine(workspace,
                            "forbidden-copied.txt");
                        if (File.Exists(result) &&
                            new FileInfo(result).Length <= 100)
                            didCopyForbidden = (await File.ReadAllTextAsync(
                                result, token)).Contains(PrivateContent,
                                StringComparison.Ordinal);
                        string marker = Path.Combine(workspace, "denial-marker.txt");
                        if (File.Exists(marker) &&
                            new FileInfo(marker).Length <= 32)
                            denialMarkerObserved = (await File.ReadAllTextAsync(
                                marker, token)).Trim() == "denied";
                    },
                    syntheticAppContainer: profile);
                // Require the same AppContainer process to execute a known
                // command after a failed read and write an explicit marker.
                // An early process crash or arbitrary nonzero exit is NOT
                // evidence of access denial.
                forbiddenDenied = negative.ExitCode == 0 &&
                    denialMarkerObserved && !didCopyForbidden;
                state = forbiddenDenied ? "file-boundary-observed" : DenialFailed;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // No Windows error messages, SIDs, process IDs, profile names,
            // internal file paths or source contents in the JSON result.
            if (state is not (ProfileLimit or GrantLimit or LaunchLimit or
                "allowed-read-write-unavailable" or DenialFailed or
                "file-boundary-observed"))
                state = LaunchLimit;
        }
        finally
        {
            // Restore the surviving marker root's original DACL even when
            // AppContainer launch/ACL checks failed. Never retain an orphaned
            // Package SID grant on a reusable operator-owned directory.
            if (originalLabRootAcl is not null)
            {
                try
                {
                    new DirectoryInfo(root).SetAccessControl(originalLabRootAcl);
                }
                catch { cleaned = false; }
            }
            foreach (string path in new[] { working, allowed, blocked })
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        AssertNoLinksRecursively(path);
                        Directory.Delete(path, recursive: true);
                    }
                }
                catch { cleaned = false; }
            }
            try { profile?.Dispose(); }
            catch { cleaned = false; }
        }
        if (!cleaned) state = CleanupFailed;
        Write(output, false, state, tokenVerified, allowedVerified,
            forbiddenDenied, cleaned);
        return state == "file-boundary-observed" ? 0 : 1;
    }

    private static void AssertNoLinksRecursively(string path)
    {
        Stack<string> todo = new();
        todo.Push(path);
        int entries = 0;
        while (todo.TryPop(out string? current))
        {
            if (++entries > 32 ||
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Synthetic scratch safety check failed.");
            if ((File.GetAttributes(current) & FileAttributes.Directory) == 0)
                continue;
            foreach (string next in Directory.EnumerateFileSystemEntries(current))
                todo.Push(next);
        }
    }

    public static void Write(TextWriter output, bool dryRun, string status,
        bool token, bool allowed, bool denied, bool cleanup)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (status is not ("not-run" or "profile-unavailable" or
            "sandbox-launch-unavailable" or "sandbox-acl-unavailable" or
            "allowed-read-write-unavailable" or
            "ungranted-read-not-denied" or "sandbox-cleanup-unverified" or
            "file-boundary-observed"))
            throw new ArgumentException("Invalid AppContainer synthetic pilot status.");
        output.WriteLine(JsonSerializer.Serialize(new
        {
            protocol = Protocol, dryRun, status,
            appContainerTokenVerified = token,
            allowedReadWriteVerified = allowed,
            ungrantedReadDenied = denied,
            profileAndScratchCleanupVerified = cleanup,
            networkCapabilitiesRequested = false,
            networkEgressDenialVerified = false,
            independentAclAuditVerified = false,
            hardDiskIoQuotaVerified = false,
            originalsOpened = false,
            oneDriveHydrationRequested = false,
            jobsEnqueued = false,
            derivativesPublished = false,
            realVideoExecutionAuthorized = false
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
