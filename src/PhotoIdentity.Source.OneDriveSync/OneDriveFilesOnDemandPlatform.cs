using System.Diagnostics;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Source.OneDriveSync;

public sealed record OneDriveFilesOnDemandState(
    AssetAvailability Availability,
    bool IsPinned,
    bool IsUnpinned,
    string? Error = null);

public interface IOneDriveFilesOnDemandPlatform
{
    OneDriveFilesOnDemandState GetState(string path);

    Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default);

    Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default);
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

    public Task RequestHydrationAsync(string path, CancellationToken cancellationToken = default) =>
        RunAttribAsync("+p", path, cancellationToken);

    public async Task RequestOnlineOnlyAsync(string path, CancellationToken cancellationToken = default)
    {
        // Clear an app-owned pin first, then explicitly request online-only state. If the second
        // operation fails the ownership record remains active so the caller can safely retry.
        await RunAttribAsync("-p", path, cancellationToken);
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
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        string error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new IOException(string.IsNullOrWhiteSpace(error)
                ? "Windows rejected the Files On-Demand state change."
                : $"Windows rejected the Files On-Demand state change: {error.Trim()}");
        }
    }
}
