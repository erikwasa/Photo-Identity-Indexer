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

        VideoToolExecutionBudget budget = new(
            TimeSpan.FromSeconds(5), 1024 * 1024,
            WindowsJobLimits: sample);
        budget.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (budget with { WindowsJobLimits = sample with { MaximumProcesses = 99 } })
            .Validate());
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
            VideoToolExecutionBudget limit = new(
                TimeSpan.FromSeconds(7), 1024 * 1024,
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
