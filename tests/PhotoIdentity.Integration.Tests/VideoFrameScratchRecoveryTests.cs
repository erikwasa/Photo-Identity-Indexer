using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Physical scratch fixtures only. No video, source, database, model, ffmpeg
/// or cloud endpoint is involved. Applies recovery only in Windows CI.
/// </summary>
public sealed class VideoFrameScratchRecoveryTests
{
    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, VideoFrameEvidenceScratchGuard.MarkerName),
            VideoFrameEvidenceScratchGuard.MarkerContents);
        return root;
    }

    private static (string Path, FileStream Handle) CreateWorkspace(string root)
    {
        string path = Path.Combine(root, VideoFrameScratchRecovery.WorkspacePrefix +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var handle = VideoFrameScratchRecovery.CreateActiveWorkspace(path);
        return (path, handle);
    }

    private static void Age(string workspace) =>
        File.SetLastWriteTimeUtc(
            Path.Combine(workspace, VideoFrameScratchRecovery.WorkspaceMarker),
            DateTime.UtcNow.AddHours(-48));

    [Fact]
    public void Recovery_parser_requires_explicit_local_owned_root_and_two_apply_acks()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-evidence-synthetic");
        string[] args = ["--scratch-root", root];
        var dry = VideoFrameScratchRecoveryOptions.Parse(args);
        Assert.False(dry.Apply);
        Assert.Equal(24, dry.MinimumAgeHours);
        var apply = VideoFrameScratchRecoveryOptions.Parse([
            .. args, "--minimum-age-hours", "6", "--apply",
            "--acknowledge-private-scratch-deletion"]);
        Assert.True(apply.Apply);
        Assert.Equal(6, apply.MinimumAgeHours);
        Assert.Throws<ArgumentException>(() =>
            VideoFrameScratchRecoveryOptions.Parse([.. args, "--apply"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameScratchRecoveryOptions.Parse([
                .. args, "--acknowledge-private-scratch-deletion"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameScratchRecoveryOptions.Parse([
                .. args, "--minimum-age-hours", "5"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameScratchRecoveryOptions.Parse([
                .. args, "--minimum-age-hours", "721"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameScratchRecoveryOptions.Parse([
                .. args, "--apply", "--apply",
                "--acknowledge-private-scratch-deletion"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameScratchRecoveryOptions.Parse([.. args, "--source", "private.mp4"]));
    }

    [Fact]
    public void Unknown_or_unmarked_workspace_blocks_all_deletions_even_with_age_and_acknowledgment()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = CreateRoot();
        try
        {
            var (owned, handle) = CreateWorkspace(root);
            handle.Dispose();
            Age(owned);
            string unknown = Path.Combine(root, "operator-private-media.txt");
            File.WriteAllText(unknown, "must-not-touch");
            var assessment = VideoFrameScratchRecovery.InspectAndRecover(
                root, apply: true, DateTimeOffset.UtcNow, minimumAgeHours: 6);
            Assert.True(assessment.Blocked);
            Assert.Equal(1, assessment.RefusedWorkspaces);
            Assert.Equal(0, assessment.RemovedWorkspaces);
            Assert.True(Directory.Exists(owned));
            Assert.Equal("must-not-touch", File.ReadAllText(unknown));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Interrupted_workspace_can_be_recovered_only_after_age_and_only_when_inactive()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = CreateRoot();
        try
        {
            var (orphan, lease) = CreateWorkspace(root);
            File.WriteAllText(Path.Combine(orphan, "private-transient-frame.jpg"), "synthetic");
            Age(orphan);

            var active = VideoFrameScratchRecovery.InspectAndRecover(
                root, apply: false, DateTimeOffset.UtcNow, minimumAgeHours: 6);
            Assert.Equal(1, active.OwnedWorkspaces);
            Assert.Equal(1, active.ActiveWorkspaces);
            Assert.Equal(0, active.RecoverableWorkspaces);
            Assert.Equal(0, active.RemovedWorkspaces);

            // Process exits or crashes, releasing the OS-exclusive handle;
            // still no deletion without explicit apply.
            lease.Dispose();
            var plan = VideoFrameScratchRecovery.InspectAndRecover(
                root, apply: false, DateTimeOffset.UtcNow, minimumAgeHours: 6);
            Assert.False(plan.Blocked);
            Assert.Equal(1, plan.RecoverableWorkspaces);
            Assert.True(Directory.Exists(orphan));

            var clean = VideoFrameScratchRecovery.InspectAndRecover(
                root, apply: true, DateTimeOffset.UtcNow, minimumAgeHours: 6);
            Assert.Equal(1, clean.RemovedWorkspaces);
            Assert.False(clean.Blocked);
            Assert.False(Directory.Exists(orphan));
            Assert.True(File.Exists(Path.Combine(root,
                VideoFrameEvidenceScratchGuard.MarkerName)));
            Assert.Equal(root, VideoFrameEvidenceScratchGuard.Validate(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Young_and_invalid_or_linked_workspaces_never_become_recovery_candidates()
    {
        string root = CreateRoot();
        try
        {
            var (young, handle) = CreateWorkspace(root);
            handle.Dispose();
            var youngPlan = VideoFrameScratchRecovery.InspectAndRecover(
                root, apply: false, DateTimeOffset.UtcNow, minimumAgeHours: 6);
            Assert.Equal(1, youngPlan.YoungWorkspaces);
            Assert.Equal(0, youngPlan.RecoverableWorkspaces);
            Assert.True(Directory.Exists(young));

            // Even an aged, marker-owned workspace containing a foreign
            // reparse point is categorically refused.
            Age(young);
            string target = Path.Combine(Path.GetTempPath(),
                "pi-recovery-untouched-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(target);
            try
            {
                string link = Path.Combine(young, "forbidden-junction");
                try { Directory.CreateSymbolicLink(link, target); }
                catch (Exception e) when (e is UnauthorizedAccessException or
                    IOException or PlatformNotSupportedException) { return; }
                var invalid = VideoFrameScratchRecovery.InspectAndRecover(
                    root, apply: false, DateTimeOffset.UtcNow, minimumAgeHours: 6);
                Assert.True(invalid.Blocked);
                Assert.Equal(1, invalid.RefusedWorkspaces);
                Assert.Equal(0, invalid.RecoverableWorkspaces);
                Assert.True(Directory.Exists(target));
            }
            finally { Directory.Delete(target, true); }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void No_marker_or_an_unowned_non_temp_location_is_never_recovered()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
                VideoFrameScratchRecovery.InspectAndRecover(
                    root, false, DateTimeOffset.UtcNow));
        }
        finally { Directory.Delete(root, true); }

        Assert.Throws<InvalidOperationException>(() =>
            VideoFrameScratchRecovery.InspectAndRecover(
                Path.GetTempPath(), false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Numeric_only_report_never_returns_private_root_or_names()
    {
        string root = CreateRoot();
        try
        {
            var (workspace, handle) = CreateWorkspace(root);
            handle.Dispose();
            Age(workspace);
            using StringWriter output = new();
            Assert.Equal(0, VideoFrameScratchRecoveryCommandRunner.Run(
                new(root), output));
            Assert.Contains("\"recoverableWorkspaces\":1", output.ToString());
            Assert.DoesNotContain(root, output.ToString());
            Assert.DoesNotContain(workspace, output.ToString());
            Assert.Contains("\"dryRun\":true", output.ToString());
            Assert.True(Directory.Exists(workspace));
        }
        finally { Directory.Delete(root, true); }
    }
}
