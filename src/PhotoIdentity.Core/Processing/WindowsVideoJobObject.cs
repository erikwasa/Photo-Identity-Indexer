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
    int MaximumCpuPercent,
    long? MaximumObservedReadBytes = null,
    long? MaximumObservedWriteBytes = null,
    bool RequireProcessCreationMitigations = false)
{
    public void Validate()
    {
        if (MaximumJobMemoryMiB is < 128 or > 4096 ||
            MaximumProcesses is < 1 or > 8 ||
            MaximumCpuPercent is < 1 or > 50 ||
            (MaximumObservedReadBytes is null) != (MaximumObservedWriteBytes is null) ||
            (MaximumObservedReadBytes is long read &&
             read is < 1048576 or > 2147483648L) ||
            (MaximumObservedWriteBytes is long write &&
             write is < 1048576 or > 2147483648L))
            throw new ArgumentOutOfRangeException(nameof(WindowsVideoJobLimits),
                "Windows video limits must bound memory (128..4096 MiB), " +
                "processes (1..8), CPU rate (1..50 percent), and " +
                "paired observed read/write bounds (1 MiB..2 GiB).");
    }
}

/// <summary>
/// Measured per-job Windows read/write transfer counters. Counts can include
/// cached operations and are NOT an OS-enforced file, rate or volume quota.
/// </summary>
public sealed record VideoJobIoSnapshot(ulong ReadBytes, ulong WriteBytes)
{
    public static bool WithinLimits(VideoJobIoSnapshot snapshot,
        long maximumReadBytes, long maximumWriteBytes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximumReadBytes < 1 || maximumWriteBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumReadBytes));
        return snapshot.ReadBytes <= (ulong)maximumReadBytes &&
               snapshot.WriteBytes <= (ulong)maximumWriteBytes;
    }
}

/// <summary>
/// Windows Job Object resource controller, intended only for the future
/// explicitly enabled local video adapter. Applying the limits fails closed:
/// no unsupported-OS fallback and no unbounded video child on assignment
/// failure. The entire tree dies when the last job handle closes.
///
/// A production worker must use the internal suspended native launcher,
/// which assigns a process to the job before its first instruction executes.
/// The legacy public Assign method remains only for test compatibility and
/// must not authorize video execution. This class does not claim hard I/O,
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
    private readonly WindowsVideoJobLimits _limits;
    private bool _disposed;

    private WindowsVideoJobObject(SafeFileHandle handle, WindowsVideoJobLimits limits)
    {
        _handle = handle;
        _limits = limits;
    }

    // Defense-in-depth for explicitly opted-in tools, not a token sandbox.
    // Allows the suspended launcher to make mitigation setup mandatory.
    internal bool RequireProcessCreationMitigations =>
        _limits.RequireProcessCreationMitigations;

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
            return new WindowsVideoJobObject(handle, limits);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Native job read/write transfer counters (not peak bandwidth or a hard
    /// kernel quota). If an observed per-job limit is requested, fail closed
    /// on missing accounting data or an exceeded total. This is a best-effort
    /// polling cutoff; an untrusted child can exceed limits between samples.
    /// </summary>
    internal VideoJobIoSnapshot? CheckObservedIo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_limits.MaximumObservedReadBytes is not long readLimit ||
            _limits.MaximumObservedWriteBytes is not long writeLimit)
            return null;

        int size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(_handle, ExtendedLimitInformation,
                    buffer, (uint)size, out uint returned) ||
                returned < size)
                throw VideoToolFailureEvidence.Mark(
                    new InvalidOperationException(
                        "Video job I/O accounting is not available."),
                    "io-accounting-unavailable");
            JobObjectExtendedLimitInformation info =
                Marshal.PtrToStructure<JobObjectExtendedLimitInformation>(buffer);
            var observed = new VideoJobIoSnapshot(
                info.IoInfo.ReadTransferCount, info.IoInfo.WriteTransferCount);
            if (!VideoJobIoSnapshot.WithinLimits(observed, readLimit, writeLimit))
                throw VideoToolFailureEvidence.Mark(
                    new InvalidOperationException(
                        "Video job exceeded its observed disk-I/O transfer budget."),
                    "io-budget-exceeded");
            return observed;
        }
        finally { Marshal.FreeHGlobal(buffer); }
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

    /// <summary>Internal suspended-launch-only assignment before ResumeThread.</summary>
    internal void AssignNative(IntPtr processHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (processHandle == IntPtr.Zero ||
            !AssignProcessToJobObject(_handle, processHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Suspended video child could not enter its limited process job.");
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
    private static extern bool QueryInformationJobObject(
        SafeFileHandle job, int infoClass, IntPtr result, uint length,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(
        SafeFileHandle job, IntPtr process);
}
