using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
    private readonly string _labRoot;
    private IntPtr _sid;
    private bool _disposed;

    private WindowsVideoAppContainerProfile(string name, IntPtr sid, string labRoot)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "AppContainer identity construction requires Windows.");
        _name = name;
        _labRoot = labRoot;
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

    public static WindowsVideoAppContainerProfile CreateSyntheticLab(
        string markedLabRoot)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "AppContainer feasibility is Windows-only.");
        string labRoot = ValidateLabRoot(markedLabRoot);
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
        try { return new WindowsVideoAppContainerProfile(name, sid, labRoot); }
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
        // Never become a general-purpose ACL grant API: only the exact
        // predefined synthetic lab directory may receive this SID's ACE.
        string expected = Path.Combine(_labRoot,
            readOnly ? "sandbox-allowed" : "sandbox-working");
        if (!string.Equals(Path.GetFullPath(directory), expected,
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "AppContainer SID may only be granted to predefined synthetic lab directories.");
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

    /// <summary>
    /// Allow directory traversal of only the newly created lab root. This
    /// ACE never inherits to the ungranted sibling directory or its files.
    /// </summary>
    public void GrantOwnedRootTraverse(string directory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!OperatingSystem.IsWindows() ||
            !Path.IsPathFullyQualified(directory) ||
            !Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Synthetic lab root must exist locally.");
        if (!string.Equals(Path.GetFullPath(directory), _labRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "AppContainer traversal is restricted to the marked lab root.");
        DirectoryInfo info = new(directory);
        DirectorySecurity acl = info.GetAccessControl(AccessControlSections.Access);
        acl.AddAccessRule(new FileSystemAccessRule(Identity,
            FileSystemRights.Traverse,
            InheritanceFlags.None, PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(acl);
    }


    /// <summary>
    /// Independently inspect the synthetic DACL shape before executing any
    /// child. This checks only generated lab paths and does not constitute
    /// an audit of the real archive, runtime/tool directories or cache.
    /// The observed allow/deny probes are still required afterwards.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public bool AuditSyntheticAcl()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string working = Path.Combine(_labRoot, "sandbox-working");
        string allowed = Path.Combine(_labRoot, "sandbox-allowed");
        string blocked = Path.Combine(_labRoot, "sandbox-ungranted");
        string allowedFile = Path.Combine(allowed, "generated-public.txt");
        string blockedFile = Path.Combine(blocked, "generated-restricted.txt");

        foreach (string directory in new[] { _labRoot, working, allowed, blocked })
            if (!Directory.Exists(directory) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                return false;
        foreach (string file in new[] { allowedFile, blockedFile })
            if (!File.Exists(file) ||
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                return false;

        FileSystemAccessRule[] rootRules = Rules(
            new DirectoryInfo(_labRoot).GetAccessControl(AccessControlSections.Access));
        FileSystemAccessRule[] allowedRules = Rules(
            new DirectoryInfo(allowed).GetAccessControl(AccessControlSections.Access));
        FileSystemAccessRule[] workingRules = Rules(
            new DirectoryInfo(working).GetAccessControl(AccessControlSections.Access));
        FileSystemAccessRule[] blockedRules = Rules(
            new DirectoryInfo(blocked).GetAccessControl(AccessControlSections.Access));
        FileSystemAccessRule[] allowedFileRules = Rules(
            new FileInfo(allowedFile).GetAccessControl(AccessControlSections.Access));
        FileSystemAccessRule[] blockedFileRules = Rules(
            new FileInfo(blockedFile).GetAccessControl(AccessControlSections.Access));

        // Neither a parent inherited wildcard ACE nor the Windows
        // ALL APPLICATION PACKAGES principals may bypass the unique lab SID.
        foreach (FileSystemAccessRule[] rules in new[]
            { rootRules, allowedRules, workingRules, blockedRules,
              allowedFileRules, blockedFileRules })
            if (rules.Any(rule => rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference is SecurityIdentifier sid &&
                sid.Value is "S-1-15-2-1" or "S-1-15-2-2"))
                return false;

        bool ExactPackageRule(FileSystemAccessRule[] rules,
            FileSystemRights required, bool inherited,
            InheritanceFlags inheritance, bool mayWrite)
        {
            FileSystemAccessRule[] matching = rules.Where(rule =>
                rule.IdentityReference.Equals(Identity)).ToArray();
            if (matching.Length != 1) return false;
            FileSystemAccessRule rule = matching[0];
            if (rule.AccessControlType != AccessControlType.Allow ||
                rule.IsInherited != inherited ||
                rule.InheritanceFlags != inheritance ||
                (rule.FileSystemRights & required) != required)
                return false;
            const FileSystemRights forbiddenWrite =
                FileSystemRights.WriteData | FileSystemRights.AppendData |
                FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
                FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership |
                FileSystemRights.WriteAttributes |
                FileSystemRights.WriteExtendedAttributes;
            return mayWrite || (rule.FileSystemRights & forbiddenWrite) == 0;
        }

        InheritanceFlags descendants = InheritanceFlags.ContainerInherit |
            InheritanceFlags.ObjectInherit;
        // The root grant is traversal only. In particular, listing the
        // sibling names or reading data is not part of the package grant.
        return rootRules.Where(rule => rule.IdentityReference.Equals(Identity))
                   .All(rule => (rule.FileSystemRights &
                       ~(FileSystemRights.Traverse | FileSystemRights.Synchronize)) == 0) &&
               ExactPackageRule(rootRules, FileSystemRights.Traverse,
                   inherited: false, InheritanceFlags.None, mayWrite: false) &&
               ExactPackageRule(allowedRules, FileSystemRights.ReadAndExecute,
                   inherited: false, descendants, mayWrite: false) &&
               ExactPackageRule(workingRules, FileSystemRights.Modify,
                   inherited: false, descendants, mayWrite: true) &&
               ExactPackageRule(allowedFileRules, FileSystemRights.ReadAndExecute,
                   inherited: true, InheritanceFlags.None, mayWrite: false) &&
               !blockedRules.Any(rule => rule.IdentityReference.Equals(Identity)) &&
               !blockedFileRules.Any(rule => rule.IdentityReference.Equals(Identity));
    }

    [SupportedOSPlatform("windows")]
    private static FileSystemAccessRule[] Rules(FileSystemSecurity security) =>
        security.GetAccessRules(includeExplicit: true, includeInherited: true,
            targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().ToArray();

    private static string ValidateLabRoot(string labRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labRoot);
        if (!Path.IsPathFullyQualified(labRoot) ||
            labRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("AppContainer requires a local marked synthetic root.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(labRoot));
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!root.StartsWith(temp + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(root))
            throw new ArgumentException("AppContainer root must be under the OS temporary directory.");
        for (string? cursor = root; cursor is not null;
             cursor = Path.GetDirectoryName(cursor))
        {
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Linked AppContainer scratch directories are forbidden.");
            if (string.Equals(cursor, temp, StringComparison.OrdinalIgnoreCase))
                break;
        }
        string marker = Path.Combine(root, ".photoidentity-synthetic-video-lab");
        if (!File.Exists(marker) ||
            (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
            File.ReadAllText(marker) != "wi0188-synthetic-video-lab-v1" ||
            Directory.EnumerateFileSystemEntries(root).Count() != 1)
            throw new ArgumentException("AppContainer requires an otherwise empty owned synthetic root.");
        return root;
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
