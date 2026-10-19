using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Required upper bounds for one explicitly supervised Windows video tool.
/// Neither this record nor a Job Object is a filesystem/network sandbox.
/// </summary>
public sealed record WindowsVideoJobLimits(
    int MaximumJobMemoryMiB,
    int MaximumProcesses,
    int MaximumCpuPercent)
{
    public void Validate()
    {
        if (MaximumJobMemoryMiB is < 128 or > 4096 ||
            MaximumProcesses is < 1 or > 8 ||
            MaximumCpuPercent is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(WindowsVideoJobLimits),
                "Windows video limits must bound memory (128..4096 MiB), " +
                "processes (1..8), and CPU rate (1..50 percent).");
    }
}

/// <summary>
/// Windows Job Object resource controller, intended only for the future
/// explicitly enabled local video adapter. Applying the limits fails closed:
/// no unsupported-OS fallback and no unbounded video child on assignment
/// failure. The entire tree dies when the last job handle closes.
///
/// IMPORTANT: Process.Start followed by Assign has a brief uncontained
/// start-to-assignment interval. A production video worker must eventually
/// launch suspended/atomically into a job before it may process untrusted
/// media. This class does not claim that startup boundary or hard I/O,
/// scratch-disk, GPU, filesystem or network confinement.
/// </summary>
public sealed class WindowsVideoJobObject : IDisposable
{
    private const uint LimitActiveProcess = 0x00000008;
    private const uint LimitJobMemory = 0x00000200;
    private const uint LimitKillOnClose = 0x00002000;
    private const uint CpuEnable = 0x1;
    private const uint CpuHardCap = 0x4;
    private const int ExtendedLimitInformation = 9;
    private const int CpuRateControlInformation = 15;

    private readonly SafeFileHandle _handle;
    private bool _disposed;

    private WindowsVideoJobObject(SafeFileHandle handle) => _handle = handle;

    public static WindowsVideoJobObject Create(WindowsVideoJobLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Windows Job Object resource limits are required for this video profile.");

        SafeFileHandle handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "Video resource job could not be created.");
        }

        try
        {
            JobObjectExtendedLimitInformation extended = new()
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = LimitActiveProcess | LimitJobMemory | LimitKillOnClose,
                    ActiveProcessLimit = (uint)limits.MaximumProcesses
                },
                JobMemoryLimit = (nuint)((long)limits.MaximumJobMemoryMiB * 1024 * 1024)
            };
            SetLimits(handle, ExtendedLimitInformation, extended);

            JobObjectCpuRateControlInformation cpu = new()
            {
                ControlFlags = CpuEnable | CpuHardCap,
                CpuRate = (uint)(limits.MaximumCpuPercent * 100)
            };
            SetLimits(handle, CpuRateControlInformation, cpu);
            return new WindowsVideoJobObject(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Fail-closed assignment immediately after Process.Start().
    /// The start-to-assignment gap remains an explicit activation blocker.</summary>
    public void Assign(Process process)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(process);
        if (!AssignProcessToJobObject(_handle, process.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Video subprocess could not be assigned to its resource job.");
    }

    /// <summary>Close the last job handle; Windows terminates all members.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }

    private static void SetLimits<T>(SafeFileHandle handle, int infoClass, T value)
        where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, buffer, false);
            if (!SetInformationJobObject(handle, infoClass, buffer, (uint)size))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Windows rejected the bounded video resource profile.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectCpuRateControlInformation
    {
        public uint ControlFlags;
        public uint CpuRate;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(
        IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle job, int jobInfoClass, IntPtr jobInfo, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(
        SafeFileHandle job, IntPtr process);
}
