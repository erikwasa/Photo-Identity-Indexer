using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PhotoIdentity.Source.OneDriveSync;

/// <summary>
/// Opens already-local video bytes with no OneDrive recall request on Windows.
/// All callers must independently validate the source revision, local
/// availability, unchanged attributes and source directory ownership.
/// </summary>
public static class OneDriveVideoNoRecallReader
{
    public static FileStream OpenRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("An absolute local video path is required.");

        if (!OperatingSystem.IsWindows())
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan);

        // FILE_FLAG_OPEN_NO_RECALL. If a Cloud Files provider cannot serve
        // already-local bytes, fail instead of intentionally requesting
        // hydration. Never request write access or share writes.
        SafeFileHandle handle = CreateFileW(path, 0x80000000, 0x1, IntPtr.Zero,
            3, 0x00100000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new IOException("Already-local video bytes could not be opened without recall.");
        }
        return new FileStream(handle, FileAccess.Read, 65536, isAsync: false);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr templateFile);
}
