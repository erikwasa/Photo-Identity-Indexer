using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Native suspended Windows launcher. No child instruction can execute until
/// its primary thread is assigned to the capped Job Object and resumed.
/// Stdout, stderr and stdin are inherited handles to the Windows NUL device;
/// no private media paths or subprocess logs are collected.
/// </summary>
internal static class WindowsVideoSuspendedLauncher
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint StartUseStdHandles = 0x00000100;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const nuint ProcThreadAttributeHandleList = 0x00020002;
    // Documented STARTUPINFOEX process creation attributes; set in the
    // suspended launcher BEFORE any native child instruction executes.
    private const nuint ProcThreadAttributeChildProcessPolicy = 0x0002000E;
    private const nuint ProcThreadAttributeSecurityCapabilities = 0x00020009;
    private const int TokenIsAppContainer = 29;
    private const int TokenAppContainerSid = 31;
    private const uint TokenQuery = 0x0008;
    private const uint ChildProcessRestricted = 0x00000001;

    // No executable-image or legacy-extension mitigation is installed yet:
    // the combined pilot caused DLL initialization failure (0xC0000142)
    // for cmd.exe in Windows CI and is not accepted for FFmpeg.
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint WaitTimeoutMillis = 5000;

    public static Process Start(
        WindowsVideoJobObject job, string executable, string directory,
        IReadOnlyList<string> arguments, IDictionary<string, string?> environment,
        CancellationToken cancellationToken,
        WindowsVideoAppContainerProfile? appContainer = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Suspended video launch requires Windows.");
        cancellationToken.ThrowIfCancellationRequested();

        string command = Quote(executable) + string.Concat(arguments.Select(arg => " " + Quote(arg)));
        string environmentBlock = string.Concat(environment.OrderBy(pair => pair.Key,
            StringComparer.OrdinalIgnoreCase).Select(pair => pair.Key + "=" + pair.Value + "\0")) + "\0";
        IntPtr nativeEnvironment = Marshal.StringToHGlobalUni(environmentBlock);
        IntPtr attributes = IntPtr.Zero;
        IntPtr inheritedList = IntPtr.Zero;
        IntPtr childProcessPolicy = IntPtr.Zero;
        IntPtr securityCapabilities = IntPtr.Zero;
        bool attributesInitialized = false;
        ProcessInformation native = default;
        Process? owned = null;
        SecurityAttributes inherit = new()
        {
            Length = (uint)Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true
        };

        try
        {
            using SafeFileHandle nullDevice = CreateFileW("NUL",
                GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                ref inherit, OpenExisting, 0, IntPtr.Zero);
            if (nullDevice.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Video child I/O sink could not be opened.");

            // An unrestricted bInheritHandles=true would leak unrelated
            // inheritable handles from the parent application. Use a single
            // explicit HANDLE_LIST containing only the NUL device.
            nuint bytes = 0;
            int attributeCount = 1 +
                (job.RequireChildProcessRestriction ? 1 : 0) +
                (appContainer is not null ? 1 : 0);
            _ = InitializeProcThreadAttributeList(
                IntPtr.Zero, attributeCount, 0, ref bytes);
            if (bytes == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Video child handle inheritance could not be bounded.");
            attributes = Marshal.AllocHGlobal(checked((nint)bytes));
            if (!InitializeProcThreadAttributeList(attributes, attributeCount, 0, ref bytes))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Video child inheritance list could not be initialized.");
            attributesInitialized = true;
            inheritedList = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(inheritedList, nullDevice.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0,
                    ProcThreadAttributeHandleList, inheritedList,
                    (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Video child inherited-handle allowlist could not be installed.");

            if (job.RequireChildProcessRestriction)
            {
                // Value must remain allocated until the attribute list is
                // destroyed. Creation fails closed if the OS refuses the
                // child process restriction; no fallback is attempted.
                childProcessPolicy = Marshal.AllocHGlobal(sizeof(uint));
                Marshal.WriteInt32(childProcessPolicy,
                    unchecked((int)ChildProcessRestricted));
                if (!UpdateProcThreadAttribute(attributes, 0,
                        ProcThreadAttributeChildProcessPolicy, childProcessPolicy,
                        (nuint)sizeof(uint), IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Native video child-process restriction could not be installed.");
            }

            if (appContainer is not null)
            {
                // No named network, COM, file-access or other capability is
                // requested. The package SID is unique to this synthetic
                // profile and has only the owned-lab ACL grants.
                SecurityCapabilities caps = new()
                {
                    AppContainerSid = appContainer.NativeSid,
                    Capabilities = IntPtr.Zero,
                    CapabilityCount = 0,
                    Reserved = 0
                };
                securityCapabilities = Marshal.AllocHGlobal(
                    Marshal.SizeOf<SecurityCapabilities>());
                Marshal.StructureToPtr(caps, securityCapabilities, false);
                if (!UpdateProcThreadAttribute(attributes, 0,
                        ProcThreadAttributeSecurityCapabilities,
                        securityCapabilities,
                        (nuint)Marshal.SizeOf<SecurityCapabilities>(),
                        IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "Synthetic AppContainer security capabilities could not be installed.");
            }

            StartupInfoEx startup = new()
            {
                StartupInfo = new StartupInfo
                {
                    Size = (uint)Marshal.SizeOf<StartupInfoEx>(),
                    Flags = StartUseStdHandles,
                    StdInput = nullDevice.DangerousGetHandle(),
                    StdOutput = nullDevice.DangerousGetHandle(),
                    StdError = nullDevice.DangerousGetHandle()
                },
                AttributeList = attributes
            };
            if (!CreateProcessW(executable, new StringBuilder(command),
                    IntPtr.Zero, IntPtr.Zero, true,
                    CreateSuspended | CreateNoWindow | CreateUnicodeEnvironment |
                    ExtendedStartupInfoPresent,
                    nativeEnvironment, directory, ref startup, out native))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Approved bounded video tool could not be launched suspended.");

            // The AppContainer token is inspected while the process remains
            // suspended. A silently ignored SECURITY_CAPABILITIES attribute
            // must never turn into an unrestricted successful lab result.
            if (appContainer is not null)
                AssertNativeAppContainerToken(
                    native.Process, appContainer.NativeSid);

            // This must happen BEFORE ResumeThread. On any failure the
            // suspended process is terminated and no unbounded process runs.
            job.AssignNative(native.Process);
            owned = Process.GetProcessById(checked((int)native.ProcessId));
            _ = owned.Handle; // Pin an independent waitable process handle before resume.
            cancellationToken.ThrowIfCancellationRequested();
            if (ResumeThread(native.Thread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Bounded video tool could not resume.");
            Process result = owned;
            owned = null;
            return result;
        }
        catch
        {
            if (native.Process != IntPtr.Zero)
            {
                // Fail closed before returning even when job assignment failed.
                _ = TerminateProcess(native.Process, 1);
                _ = WaitForSingleObject(native.Process, WaitTimeoutMillis);
            }
            throw;
        }
        finally
        {
            owned?.Dispose();
            if (native.Thread != IntPtr.Zero) _ = CloseHandle(native.Thread);
            if (native.Process != IntPtr.Zero) _ = CloseHandle(native.Process);
            if (attributesInitialized)
                DeleteProcThreadAttributeList(attributes);
            if (inheritedList != IntPtr.Zero)
                Marshal.FreeHGlobal(inheritedList);
            if (childProcessPolicy != IntPtr.Zero)
                Marshal.FreeHGlobal(childProcessPolicy);
            if (securityCapabilities != IntPtr.Zero)
                Marshal.FreeHGlobal(securityCapabilities);
            if (attributes != IntPtr.Zero)
                Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(nativeEnvironment);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    private static void AssertNativeAppContainerToken(
        IntPtr process, IntPtr expectedPackageSid)
    {
        if (!OpenProcessToken(process, TokenQuery, out IntPtr token))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Synthetic AppContainer child token could not be inspected.");
        try
        {
            if (!GetTokenInformation(token, TokenIsAppContainer,
                    out int isAppContainer, sizeof(int), out int length) ||
                length != sizeof(int) || isAppContainer != 1)
                throw new InvalidOperationException(
                    "Windows did not establish an AppContainer child security token.");

            // TokenIsAppContainer alone is insufficient. It must be this
            // newly created lab's EXACT SID, not any other Package identity.
            if (!GetTokenInformation(token, TokenAppContainerSid,
                    out IntPtr actualSid, IntPtr.Size, out int sidSize) ||
                sidSize != IntPtr.Size || actualSid == IntPtr.Zero ||
                expectedPackageSid == IntPtr.Zero ||
                !EqualSid(actualSid, expectedPackageSid))
                throw new InvalidOperationException(
                    "Windows AppContainer child identity did not match the disposable lab.");
        }
        finally { _ = CloseHandle(token); }
    }

    private static string Quote(string argument)
    {
        // Mirror ProcessStartInfo.ArgumentList: do NOT quote simple switch
        // arguments (notably /d and /c for cmd.exe). Some Windows programs
        // parse quoted switches differently even though C argv treats them
        // as equivalent.
        if (argument.Length > 0 &&
            argument.All(c => !char.IsWhiteSpace(c) && c != '"'))
            return argument;
        // Windows CommandLineToArgv-compatible escaping for literal argv.
        StringBuilder result = new("\"");
        int slashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"')
            {
                result.Append('\\', slashes * 2 + 1);
                result.Append('"');
                slashes = 0;
                continue;
            }
            result.Append('\\', slashes);
            slashes = 0;
            result.Append(c);
        }
        result.Append('\\', slashes * 2);
        result.Append('"');
        return result.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access,
        uint shareMode, ref SecurityAttributes attributes, uint creation,
        uint flags, IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string applicationName,
        StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags,
        IntPtr environment, string currentDirectory, ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributes, int attributeCount, int flags, ref nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributes, uint flags, nuint attribute,
        IntPtr value, nuint size, IntPtr previous, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr token, int tokenInformationClass,
        out int tokenInformation, int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr token, int tokenInformationClass,
        out IntPtr tokenInformation, int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EqualSid(IntPtr first, IntPtr second);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
