using System.Diagnostics;
using System.Runtime.InteropServices;
using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

// This test collection temporarily changes the process-wide Windows error mode
// while probing an unsupported native mitigation. Do not run it alongside
// other test collections in the same xUnit test host.
[CollectionDefinition("WindowsVideoJobObjectTests", DisableParallelization = true)]
public sealed class WindowsVideoJobObjectTestsCollection { }

[Collection("WindowsVideoJobObjectTests")]
public sealed class WindowsVideoJobObjectTests
{
    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;

    [DllImport("kernel32.dll")]
    private static extern uint GetErrorMode();

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint errorMode);

    [Fact]
    public void Resource_limits_reject_unbounded_or_impractical_profiles()
    {
        WindowsVideoJobLimits sample = new(512, 2, 25);
        sample.Validate();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (sample with { MaximumJobMemoryMiB = 127 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (sample with { MaximumJobMemoryMiB = 4097 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (sample with { MaximumProcesses = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (sample with { MaximumProcesses = 9 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (sample with { MaximumCpuPercent = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (sample with { MaximumCpuPercent = 51 }).Validate());

        // A one-sided observed I/O limit must never silently leave the
        // other transfer direction unbounded.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (sample with { MaximumObservedReadBytes = 2L * 1024 * 1024 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (sample with { MaximumObservedWriteBytes = 2L * 1024 * 1024 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (sample with { MaximumObservedReadBytes = 0,
                           MaximumObservedWriteBytes = 2L * 1024 * 1024 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (sample with { MaximumObservedReadBytes = 3L * 1024 * 1024 * 1024,
                           MaximumObservedWriteBytes = 2L * 1024 * 1024 }).Validate());
        (sample with { MaximumObservedReadBytes = 2L * 1024 * 1024,
                       MaximumObservedWriteBytes = 2L * 1024 * 1024 }).Validate();

        VideoToolExecutionBudget budget = new(
            TimeSpan.FromSeconds(5), 1024 * 1024,
            WindowsJobLimits: sample);
        budget.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (budget with { WindowsJobLimits = sample with { MaximumProcesses = 99 } })
            .Validate());
    }

    [Fact]
    public void Measured_job_io_budget_rejects_either_direction_without_overflow()
    {
        long mib = 1024L * 1024;
        Assert.True(VideoJobIoSnapshot.WithinLimits(
            new VideoJobIoSnapshot((ulong)mib, (ulong)mib), mib, mib));
        Assert.False(VideoJobIoSnapshot.WithinLimits(
            new VideoJobIoSnapshot((ulong)mib + 1, (ulong)mib), mib, mib));
        Assert.False(VideoJobIoSnapshot.WithinLimits(
            new VideoJobIoSnapshot((ulong)mib, (ulong)mib + 1), mib, mib));
        Assert.False(VideoJobIoSnapshot.WithinLimits(
            new VideoJobIoSnapshot(ulong.MaxValue, ulong.MaxValue), mib, mib));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoJobIoSnapshot.WithinLimits(
                new VideoJobIoSnapshot(0, 0), 0, mib));
    }

    [Fact]
    public async Task Experimental_child_process_policy_is_never_assumed_compatible_without_native_proof()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(),
            "wi0188-mitigated-video-tool-" + Guid.NewGuid().ToString("N"));
        uint previousErrorMode = GetErrorMode();
        try
        {
            // A child-process mitigation may cause STATUS_DLL_INIT_FAILED before
            // cmd.exe starts. Suppress inherited Windows error dialogs so the
            // test can observe the real nonzero exit code rather than hang.
            // The xUnit collection is nonparallel because SetErrorMode is global.
            _ = SetErrorMode(previousErrorMode | SemFailCriticalErrors | SemNoGpFaultErrorBox);
            Directory.CreateDirectory(root);
            string cmd = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.System), "cmd.exe");
            await using FileStream file = File.OpenRead(cmd);
            string sha = Convert.ToHexString(
                await System.Security.Cryptography.SHA256.HashDataAsync(file))
                .ToLowerInvariant();
            var restricted = new VideoToolExecutionBudget(
                TimeSpan.FromSeconds(15), 1024 * 1024,
                MaximumTemporaryEntries: 8,
                WindowsJobLimits: new WindowsVideoJobLimits(
                    MaximumJobMemoryMiB: 512,
                    MaximumProcesses: 2,
                    MaximumCpuPercent: 25,
                    RequireChildProcessRestriction: true));
            var regular = restricted with
            {
                WindowsJobLimits = restricted.WindowsJobLimits! with
                {
                    RequireChildProcessRestriction = false
                }
            };
            var supervisor = new VideoToolProcessSupervisor();

            // Control: both single and nested cmd.exe calls work with the
            // normal bounded Job Object profile on this Windows host.
            string[] nested = ["/d", "/c", "cmd.exe /d /c exit /b 0"];
            var singleControl = await supervisor.RunAsync(
                cmd, sha, root, _ => ["/d", "/c", "exit /b 0"], regular);
            Assert.Equal(0, singleControl.ExitCode);
            var nestedControl = await supervisor.RunAsync(
                cmd, sha, root, _ => nested, regular);
            Assert.Equal(0, nestedControl.ExitCode);

            // A Windows CI runner returned STATUS_DLL_INIT_FAILED
            // (0xC0000142) before even the first cmd.exe statement could
            // execute with this policy. That is INCOMPATIBLE, not proof of
            // secure child denial. If an OS can run the restricted single
            // command, then additionally test its direct child refusal.
            // In neither case does this opt-in belong in real/synthetic
            // FFmpeg until an independent sandbox/compatibility review.
            var restrictedSingle = await supervisor.RunAsync(
                cmd, sha, root, _ => ["/d", "/c", "exit /b 0"], restricted);
            if (restrictedSingle.ExitCode == 0)
            {
                var refused = await supervisor.RunAsync(
                    cmd, sha, root, _ => nested, restricted);
                Assert.NotEqual(0, refused.ExitCode);
            }
            else
            {
                Assert.NotEqual(0, restrictedSingle.ExitCode);
            }

            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            _ = SetErrorMode(previousErrorMode);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Windows_native_job_io_counters_are_read_after_supervised_process_exit()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(),
            "wi0188-native-io-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string cmd = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            await using FileStream file = File.OpenRead(cmd);
            string sha = Convert.ToHexString(
                await System.Security.Cryptography.SHA256.HashDataAsync(file))
                .ToLowerInvariant();

            var limits = new WindowsVideoJobLimits(
                MaximumJobMemoryMiB: 512,
                MaximumProcesses: 1,
                MaximumCpuPercent: 25,
                MaximumObservedReadBytes: 16L * 1024 * 1024,
                MaximumObservedWriteBytes: 16L * 1024 * 1024);
            var budget = new VideoToolExecutionBudget(
                TimeSpan.FromSeconds(30), 1024 * 1024,
                MaximumTemporaryEntries: 8,
                InspectionInterval: TimeSpan.FromMilliseconds(100),
                WindowsJobLimits: limits);
            var result = await new VideoToolProcessSupervisor().RunAsync(
                cmd, sha, root,
                _ => ["/d", "/c", "echo bounded-native-io > output.txt"],
                budget);
            Assert.Equal(0, result.ExitCode);
            Assert.NotNull(result.ObservedReadBytes);
            Assert.NotNull(result.ObservedWriteBytes);
            Assert.InRange(result.ObservedReadBytes.Value, 0UL,
                (ulong)limits.MaximumObservedReadBytes.GetValueOrDefault());
            Assert.InRange(result.ObservedWriteBytes.Value, 0UL,
                (ulong)limits.MaximumObservedWriteBytes.GetValueOrDefault());
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Native_limits_are_not_silently_skipped_on_non_windows()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Throws<PlatformNotSupportedException>(() =>
            WindowsVideoJobObject.Create(new WindowsVideoJobLimits(512, 2, 25)));
    }

    [Fact]
    public async Task Suspended_limited_tool_runs_only_after_job_assignment()
    {
        if (!OperatingSystem.IsWindows()) return;
        string scratch = Path.Combine(Path.GetTempPath(),
            "photoidentity-suspended-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            string cmd = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.System), "cmd.exe");
            await using FileStream input = File.OpenRead(cmd);
            string hash = Convert.ToHexString(
                await System.Security.Cryptography.SHA256.HashDataAsync(input))
                .ToLowerInvariant();
            var supervisor = new VideoToolProcessSupervisor();
            // This test verifies suspended launch, Job Object assignment,
            // and caller cancellation, not the production wall-time ceiling.
            // Allow for slow process startup on a busy Windows CI host;
            // the explicit 450 ms caller cancellation is still asserted below.
            VideoToolExecutionBudget limit = new(
                TimeSpan.FromSeconds(20), 1024 * 1024,
                WindowsJobLimits: new WindowsVideoJobLimits(512, 2, 25),
                InspectionInterval: TimeSpan.FromMilliseconds(100));

            VideoToolExecutionResult success = await supervisor.RunAsync(
                cmd, hash, scratch, _ => ["/d", "/c", "exit /b 0"], limit);
            Assert.Equal(0, success.ExitCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));

            using CancellationTokenSource stop =
                new(TimeSpan.FromMilliseconds(450));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                supervisor.RunAsync(cmd, hash, scratch,
                    _ => ["/d", "/c", "ping -n 15 127.0.0.1 >NUL"],
                    limit, stop.Token));
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public async Task Windows_job_close_terminates_a_long_running_process()
    {
        if (!OperatingSystem.IsWindows()) return;

        string executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("/d");
        process.StartInfo.ArgumentList.Add("/c");
        process.StartInfo.ArgumentList.Add("ping -n 15 127.0.0.1 >NUL");

        using WindowsVideoJobObject job =
            WindowsVideoJobObject.Create(new WindowsVideoJobLimits(512, 2, 25));
        try
        {
            Assert.True(process.Start());
            job.Assign(process);
            Assert.False(process.HasExited);
            job.Dispose(); // The test must prove job-close termination, not a fallback Kill.
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.HasExited);
        }
        finally
        {
            job.Dispose();
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* exited concurrently */ }
            }
        }
    }
}
