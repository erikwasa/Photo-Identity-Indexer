using System.Security.Cryptography;
using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoToolProcessSupervisorTests
{
    private static readonly VideoToolExecutionBudget SmallBudget = new(
        TimeSpan.FromSeconds(10), 1024 * 1024, MaximumTemporaryEntries: 32,
        InspectionInterval: TimeSpan.FromMilliseconds(100));

    [Fact]
    public void Execution_budget_rejects_unbounded_durations_and_temporary_resources()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (SmallBudget with { MaximumWallTime = TimeSpan.FromHours(1) }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (SmallBudget with { MaximumTemporaryBytes = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (SmallBudget with { MaximumTemporaryEntries = 100000 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (SmallBudget with { InspectionInterval = TimeSpan.FromMilliseconds(1) }).Validate());
        SmallBudget.Validate();
    }

    [Fact]
    public async Task Invalid_executable_digest_is_rejected_before_any_workspace_is_created()
    {
        if (!OperatingSystem.IsWindows()) return;
        string scratch = MakeScratch();
        try
        {
            string executable = CmdPath();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new VideoToolProcessSupervisor().RunAsync(
                    executable, new string('0', 64), scratch,
                    _ => ["/d", "/c", "echo should-not-run"], SmallBudget));
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public async Task Runs_trusted_command_with_discarded_output_and_cleans_scratch()
    {
        if (!OperatingSystem.IsWindows()) return;
        string scratch = MakeScratch();
        try
        {
            string executable = CmdPath();
            string hash = await HashAsync(executable);
            var supervisor = new VideoToolProcessSupervisor();
            VideoToolExecutionResult result = await supervisor.RunAsync(
                executable, hash, scratch,
                _ => ["/d", "/c", "echo this-can-contain-a-private-path"],
                SmallBudget);
            Assert.Equal(0, result.ExitCode);
            Assert.True(result.Elapsed >= TimeSpan.Zero);
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));

            VideoToolExecutionResult failed = await supervisor.RunAsync(
                executable, hash, scratch,
                _ => ["/d", "/c", "exit /b 17"], SmallBudget);
            Assert.Equal(17, failed.ExitCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public async Task Scratch_overage_fails_and_removes_only_its_temporary_child()
    {
        if (!OperatingSystem.IsWindows()) return;
        string scratch = MakeScratch();
        string keeper = Path.Combine(scratch, "unrelated-file.txt");
        await File.WriteAllTextAsync(keeper, "keep");
        try
        {
            string executable = CmdPath();
            string hash = await HashAsync(executable);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new VideoToolProcessSupervisor().RunAsync(
                    executable, hash, scratch,
                    _ => ["/d", "/c", "echo this-is-bigger-than-four-bytes > generated.txt"],
                    SmallBudget with { MaximumTemporaryBytes = 4 }));
            Assert.True(File.Exists(keeper));
            Assert.Single(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public async Task Cancelled_external_process_is_terminated_and_scratch_cleaned()
    {
        if (!OperatingSystem.IsWindows()) return;
        string scratch = MakeScratch();
        try
        {
            string executable = CmdPath();
            string hash = await HashAsync(executable);
            using CancellationTokenSource stop = new(TimeSpan.FromMilliseconds(350));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new VideoToolProcessSupervisor().RunAsync(
                    executable, hash, scratch,
                    _ => ["/d", "/c", "ping -n 15 127.0.0.1 >NUL"],
                    SmallBudget, stop.Token));
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static string MakeScratch()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-tool-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CmdPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    private static async Task<string> HashAsync(string path)
    {
        await using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant();
    }
}
