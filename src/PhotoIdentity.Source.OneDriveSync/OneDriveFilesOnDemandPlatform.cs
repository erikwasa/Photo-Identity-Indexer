using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Source.OneDriveSync;

public sealed record OneDriveFilesOnDemandState(
    AssetAvailability Availability,
    bool IsPinned,
    bool IsUnpinned,
    string? Error = null);

public enum OneDriveSyncClientAvailability
{
    Unknown,
    Available,
    Unavailable,
}

public sealed class OneDriveSyncClientUnavailableException : IOException
{
    public const string UserMessage =
        "OneDrive is not available. Start OneDrive on this computer, then retry.";

    public OneDriveSyncClientUnavailableException()
        : base(UserMessage)
    {
    }
}

public interface IOneDriveFilesOnDemandPlatform
{
    OneDriveFilesOnDemandState GetState(string path);

    OneDriveSyncClientAvailability GetSyncClientAvailability() =>
        OneDriveSyncClientAvailability.Unknown;

    Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default);

    Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Video-only transient hydration: uses the existing Windows cloud sync
    /// provider without setting a persistent pin. Fail closed on alternate
    /// platform implementations until explicitly supported.
    /// </summary>
    Task RequestUnpinnedVideoHydrationAsync(
        string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Transient video hydration is unsupported.");

    /// <summary>
    /// Video-only release: never clears a user pin. Request online-only only
    /// if Files On-Demand still reports local and unpinned.
    /// </summary>
    Task RequestUnpinnedVideoReleaseAsync(
        string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Unpinned video release is unsupported.");
}

/// <summary>
/// Uses the documented Windows Files On-Demand file attributes to explicitly pin and unpin
/// OneDrive placeholders. Pin/unpin requests are asynchronous; callers must observe state until
/// the sync client reports the requested availability.
/// </summary>
public sealed class WindowsOneDriveFilesOnDemandPlatform : IOneDriveFilesOnDemandPlatform
{
    internal const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    internal const FileAttributes Pinned = (FileAttributes)0x00080000;
    internal const FileAttributes Unpinned = (FileAttributes)0x00100000;
    internal const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public OneDriveFilesOnDemandState GetState(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return new OneDriveFilesOnDemandState(AssetAvailability.Unavailable, false, false);
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            return Classify(attributes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new OneDriveFilesOnDemandState(
                AssetAvailability.Error,
                false,
                false,
                exception.Message);
        }
    }

    public OneDriveSyncClientAvailability GetSyncClientAvailability()
    {
        if (!OperatingSystem.IsWindows())
        {
            return OneDriveSyncClientAvailability.Unknown;
        }

        try
        {
            Process[] processes = Process.GetProcessesByName("OneDrive");
            try
            {
                return ClassifySyncClientProcessCount(processes.Length);
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Process enumeration is only a bounded one-way absence signal. If Windows cannot
            // establish that absence safely, preserve existing Files On-Demand behavior.
            return OneDriveSyncClientAvailability.Unknown;
        }
    }

    public Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default) =>
        RunAttribAsync("+p", path, cancellationToken);

    public async Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default)
    {
        // Clear an app-owned pin first, then explicitly request online-only state. If the second
        // operation fails the ownership record remains active so the caller can safely retry.
        await RunAttribAsync("-p", path, cancellationToken);
        await RunAttribAsync("+u", path, cancellationToken);
    }

    // Cloud Files API: both these calls are serviced by the already
    // registered OneDrive sync provider. There is no new cloud connector.
    [DllImport("CldApi.dll", ExactSpelling = true)]
    private static extern int CfHydratePlaceholder(
        SafeFileHandle fileHandle, long startingOffset, long length,
        uint flags, IntPtr overlapped);

    public async Task RequestUnpinnedVideoHydrationAsync(
        string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Transient cloud-file video hydration requires Windows.");

        OneDriveFilesOnDemandState before = GetState(path);
        if (before.Availability != AssetAvailability.OnlineOnly || before.IsPinned)
            throw new InvalidOperationException(
                "Video hydration requires a currently unpinned online-only source.");

        // CfHydratePlaceholder is synchronous with this handle. Run on a
        // worker thread so operator pause/timeout can cancel the wait.
        // A native request already issued can continue after cancellation;
        // the durable managed receipt remains active for safe recovery.
        await Task.Run(() =>
        {
            using SafeFileHandle handle = File.OpenHandle(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, FileOptions.None);
            // CF_EOF (-1) requests the entire bounded video original.
            int result = CfHydratePlaceholder(handle, 0L, -1L, 0, IntPtr.Zero);
            if (result < 0)
                throw new IOException(
                    "Windows Cloud Files rejected transient video hydration.");
        }, cancellationToken).WaitAsync(cancellationToken);

        // A user can pin the source while it is being hydrated; never
        // clear that pin, and let later cleanup observe it as protected.
    }

    public async Task RequestUnpinnedVideoReleaseAsync(
        string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Unpinned OneDrive video release requires Windows.");

        OneDriveFilesOnDemandState current = GetState(path);
        if (current.Availability != AssetAvailability.Local || current.IsPinned)
            throw new InvalidOperationException(
                "An unpinned local video is required for managed release.");

        // Deliberately do NOT execute attrib -p: that could remove a
        // user-requested Always keep on this device pin. +u asks the
        // existing sync provider to evict only unpinned local bytes.
        // A user pin set in the tiny check-to-attrib race remains a
        // known Windows attribute limitation; verify in real pilot.
        await RunAttribAsync("+u", path, cancellationToken);
    }

    internal static OneDriveFilesOnDemandState Classify(FileAttributes attributes)
    {
        bool pinned = (attributes & Pinned) != 0;
        bool unpinned = (attributes & Unpinned) != 0;
        bool contentMissing = (attributes &
            (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;

        AssetAvailability availability = contentMissing
            ? pinned ? AssetAvailability.Downloading : AssetAvailability.OnlineOnly
            : AssetAvailability.Local;
        return new OneDriveFilesOnDemandState(availability, pinned, unpinned);
    }

    internal static OneDriveSyncClientAvailability ClassifySyncClientProcessCount(int processCount) =>
        processCount > 0
            ? OneDriveSyncClientAvailability.Available
            : OneDriveSyncClientAvailability.Unavailable;

    private static async Task RunAttribAsync(
        string attribute,
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Explicit OneDrive Files On-Demand state changes require Windows.");
        }

        ProcessStartInfo startInfo = new("attrib.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(attribute);
        startInfo.ArgumentList.Add(path);

        using Process process = Process.Start(startInfo)
            ?? throw new IOException("Windows could not start the Files On-Demand attribute command.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        _ = await outputTask;
        _ = await errorTask;
        if (process.ExitCode != 0)
        {
            // Do not surface attrib output because Windows may include a private source path.
            throw new IOException("Windows rejected the Files On-Demand state change.");
        }
    }
}
