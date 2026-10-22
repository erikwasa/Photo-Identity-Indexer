using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Explicit synthetic-lab-only AppContainer identity. It is not selected by
/// normal video workers, FFmpeg adapters, one-shot runs or the synthetic
/// encoder. A unique Windows profile is created and MUST be removed after
/// the owned-lab proof. Zero capabilities means no network capabilities
/// were requested; it is not independent evidence of socket denial.
/// </summary>
public sealed class WindowsVideoAppContainerProfile : IDisposable
{
    private const string Prefix = "PhotoIdentity.WI0188.Lab.";
    private readonly string _name;
    private IntPtr _sid;
    private bool _disposed;

    private WindowsVideoAppContainerProfile(string name, IntPtr sid)
    {
        _name = name;
        _sid = sid;
        Identity = new SecurityIdentifier(sid);
    }

    public SecurityIdentifier Identity { get; }

    internal IntPtr NativeSid
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _sid;
        }
    }

    public static WindowsVideoAppContainerProfile CreateSyntheticLab()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "AppContainer feasibility is Windows-only.");
        // A fresh name is mandatory: never attach to an existing user's
        // installed AppContainer profile or delete another app's profile.
        string name = Prefix + Guid.NewGuid().ToString("N");
        int hresult = CreateAppContainerProfile(
            name, "PhotoIdentity disposable synthetic lab",
            "WI-0188 generated files only", IntPtr.Zero, 0, out IntPtr sid);
        if (hresult != 0 || sid == IntPtr.Zero)
        {
            if (sid != IntPtr.Zero) _ = FreeSid(sid);
            throw new Win32Exception(hresult,
                "Windows AppContainer profile creation was refused.");
        }
        try { return new WindowsVideoAppContainerProfile(name, sid); }
        catch
        {
            _ = FreeSid(sid);
            _ = DeleteAppContainerProfile(name);
            throw;
        }
    }

    /// <summary>
    /// Grant the exact new lab directory only. Never operate on an archive,
    /// user-selected path or a directory with existing non-synthetic media.
    /// </summary>
    public void GrantOwnedDirectory(string directory, bool readOnly)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!OperatingSystem.IsWindows() ||
            !Path.IsPathFullyQualified(directory) ||
            !Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Owned synthetic ACL target must exist locally.");
        // The caller validates and owns the marked sandbox root before any
        // grant. This method does not mutate existing entry inheritance:
        // its sole grant applies to this AppContainer's unique SID.
        DirectoryInfo info = new(directory);
        DirectorySecurity acl = info.GetAccessControl(AccessControlSections.Access);
        FileSystemRights rights = readOnly
            ? FileSystemRights.ReadAndExecute
            : FileSystemRights.Modify | FileSystemRights.Synchronize;
        acl.AddAccessRule(new FileSystemAccessRule(
            Identity, rights, InheritanceFlags.ContainerInherit |
            InheritanceFlags.ObjectInherit, PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(acl);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_sid != IntPtr.Zero)
        {
            _ = FreeSid(_sid);
            _sid = IntPtr.Zero;
        }
        // Never silently ignore a leaked persistent Windows profile.
        int result = DeleteAppContainerProfile(_name);
        if (result != 0)
            throw new InvalidOperationException(
                "Disposable synthetic AppContainer profile cleanup could not be verified.");
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode, ExactSpelling = true,
        EntryPoint = "CreateAppContainerProfile")]
    private static extern int CreateAppContainerProfile(
        string name, string displayName, string description,
        IntPtr capabilities, uint capabilityCount, out IntPtr appContainerSid);

    [DllImport("userenv.dll", CharSet = CharSet.Unicode, ExactSpelling = true,
        EntryPoint = "DeleteAppContainerProfile")]
    private static extern int DeleteAppContainerProfile(string name);

    [DllImport("advapi32.dll")]
    private static extern IntPtr FreeSid(IntPtr sid);
}
