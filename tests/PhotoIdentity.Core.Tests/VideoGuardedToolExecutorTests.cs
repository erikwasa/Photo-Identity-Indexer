using System.Security.Cryptography;
using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoGuardedToolExecutorTests
{
    private static VideoToolExecutionBudget Budget => new(
        TimeSpan.FromSeconds(10), 1024 * 1024,
        InspectionInterval: TimeSpan.FromMilliseconds(100));

    private static VideoToolExecutionBudget PublicationBudget => Budget with
    {
        WindowsJobLimits = new WindowsVideoJobLimits(512, 1, 25)
    };

    [Fact]
    public async Task Revoked_initial_permit_never_starts_or_creates_scratch()
    {
        string scratch = MakeScratch();
        try
        {
            var executor = new VideoGuardedToolExecutor(new VideoToolProcessSupervisor());
            VideoGuardedToolExecutionResult result = await executor.RunAsync(
                "not-an-executable", new string('0', 64), scratch,
                _ => [], Budget,
                _ => Task.FromResult(VideoExecutionPermit.Deny("video-disabled")),
                TimeSpan.FromMilliseconds(100));
            Assert.False(result.Started);
            Assert.False(result.Completed);
            Assert.Equal("video-disabled", result.ReasonCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public async Task Invalid_inspection_interval_is_rejected_before_any_source_or_process_access()
    {
        var executor = new VideoGuardedToolExecutor(new VideoToolProcessSupervisor());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => executor.RunAsync(
            "never-open", "no-hash", "never-create", _ => [], Budget,
            _ => Task.FromResult(VideoExecutionPermit.Ready),
            TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Photo_priority_revokes_child_and_cleans_scratch()
    {
        if (!OperatingSystem.IsWindows()) return;
        string scratch = MakeScratch();
        try
        {
            string executable = CmdPath();
            int observations = 0;
            var executor = new VideoGuardedToolExecutor(new VideoToolProcessSupervisor());
            VideoGuardedToolExecutionResult result = await executor.RunAsync(
                executable, await HashAsync(executable), scratch,
                _ => ["/d", "/c", "ping -n 15 127.0.0.1 >NUL"],
                Budget,
                _ => Task.FromResult(Interlocked.Increment(ref observations) == 1
                    ? VideoExecutionPermit.Ready
                    : VideoExecutionPermit.Deny("photo-priority")),
                TimeSpan.FromMilliseconds(100));
            Assert.True(result.Started);
            Assert.False(result.Completed);
            Assert.Equal("photo-priority", result.ReasonCode);
            Assert.True(observations >= 2);
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public async Task Probe_failure_revokes_child_and_never_echoes_private_error()
    {
        if (!OperatingSystem.IsWindows()) return;
        string scratch = MakeScratch();
        try
        {
            string executable = CmdPath();
            string hash = await HashAsync(executable);
            int observations = 0;
            var executor = new VideoGuardedToolExecutor(new VideoToolProcessSupervisor());
            InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => executor.RunAsync(
                    executable, hash, scratch,
                    _ => ["/d", "/c", "ping -n 15 127.0.0.1 >NUL"],
                    Budget,
                    _ => Interlocked.Increment(ref observations) == 1
                        ? Task.FromResult(VideoExecutionPermit.Ready)
                        : Task.FromException<VideoExecutionPermit>(
                            new IOException("sensitive-private-media-path")),
                    TimeSpan.FromMilliseconds(100)));
            Assert.Contains("fail closed", thrown.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sensitive-private-media-path", thrown.Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public async Task Successful_child_requires_final_safety_observation()
    {
        if (!OperatingSystem.IsWindows()) return;
        string scratch = MakeScratch();
        try
        {
            string executable = CmdPath();
            var executor = new VideoGuardedToolExecutor(new VideoToolProcessSupervisor());
            VideoGuardedToolExecutionResult completed = await executor.RunAsync(
                executable, await HashAsync(executable), scratch,
                _ => ["/d", "/c", "exit /b 0"], Budget,
                _ => Task.FromResult(VideoExecutionPermit.Ready),
                TimeSpan.FromMilliseconds(100));
            Assert.True(completed.Started);
            Assert.True(completed.Completed);
            Assert.Equal(0, completed.ExitCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public async Task Guarded_completion_callback_runs_before_owned_workspace_cleanup()
    {
        if (!OperatingSystem.IsWindows()) return;
        string scratch = MakeScratch();
        try
        {
            string executable = CmdPath();
            int called = 0;
            var executor = new VideoGuardedToolExecutor(new VideoToolProcessSupervisor());
            VideoGuardedToolExecutionResult completed = await executor.RunAsync(
                executable, await HashAsync(executable), scratch,
                _ => ["/d", "/c", "exit /b 0"], PublicationBudget,
                _ => Task.FromResult(VideoExecutionPermit.Ready),
                TimeSpan.FromMilliseconds(100),
                onValidatedSuccessfulExit: (workspace, _) =>
                {
                    Assert.True(Directory.Exists(workspace));
                    Assert.True(File.Exists(Path.Combine(workspace,
                        VideoScratchOwnership.MarkerName)));
                    File.WriteAllText(Path.Combine(workspace, "output.mp4"), "synthetic");
                    Interlocked.Increment(ref called);
                    return Task.CompletedTask;
                });
            Assert.True(completed.Completed);
            Assert.Equal(1, called);
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public async Task Rejected_publication_callback_never_preserves_unowned_output()
    {
        if (!OperatingSystem.IsWindows()) return;
        string scratch = MakeScratch();
        try
        {
            string executable = CmdPath();
            string hash = await HashAsync(executable);
            int called = 0;
            var executor = new VideoGuardedToolExecutor(new VideoToolProcessSupervisor());
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.RunAsync(
                executable, hash, scratch,
                _ => ["/d", "/c", "exit /b 0"], PublicationBudget,
                _ => Task.FromResult(VideoExecutionPermit.Ready),
                TimeSpan.FromMilliseconds(100),
                onValidatedSuccessfulExit: (_, _) =>
                {
                    Interlocked.Increment(ref called);
                    throw new InvalidOperationException("Uncommitted synthetic derivative.");
                }));
            Assert.Equal(1, called);
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    private static string MakeScratch()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-preemption-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CmdPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    private static async Task<string> HashAsync(string executable)
    {
        await using FileStream file = File.OpenRead(executable);
        return Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant();
    }
}
