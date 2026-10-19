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
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint WaitTimeoutMillis = 5000;

    public static Process Start(
        WindowsVideoJobObject job, string executable, string directory,
        IReadOnlyList<string> arguments, IDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Suspended video launch requires Windows.");
        cancellationToken.ThrowIfCancellationRequested();

        string command = Quote(executable) + string.Concat(arguments.Select(arg => " " + Quote(arg)));
        string environmentBlock = string.Concat(environment.OrderBy(pair => pair.Key,
            StringComparer.OrdinalIgnoreCase).Select(pair => pair.Key + "=" + pair.Value + "\0")) + "\0";
        IntPtr nativeEnvironment = Marshal.StringToHGlobalUni(environmentBlock);
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

            StartupInfo startup = new()
            {
                Size = (uint)Marshal.SizeOf<StartupInfo>(),
                Flags = StartUseStdHandles,
                StdInput = nullDevice.DangerousGetHandle(),
                StdOutput = nullDevice.DangerousGetHandle(),
                StdError = nullDevice.DangerousGetHandle()
            };
            if (!CreateProcessW(executable, new StringBuilder(command),
                    IntPtr.Zero, IntPtr.Zero, true,
                    CreateSuspended | CreateNoWindow | CreateUnicodeEnvironment,
                    nativeEnvironment, directory, ref startup, out native))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Approved bounded video tool could not be launched suspended.");

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
            Marshal.FreeHGlobal(nativeEnvironment);
        }
    }

    private static string Quote(string argument)
    {
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
        IntPtr environment, string currentDirectory, ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
