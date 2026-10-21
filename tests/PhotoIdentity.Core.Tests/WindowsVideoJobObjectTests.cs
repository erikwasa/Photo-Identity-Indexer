using System.Diagnostics;
using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class WindowsVideoJobObjectTests
{
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
                (ulong)limits.MaximumObservedReadBytes.Value);
            Assert.InRange(result.ObservedWriteBytes.Value, 0UL,
                (ulong)limits.MaximumObservedWriteBytes.Value);
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
