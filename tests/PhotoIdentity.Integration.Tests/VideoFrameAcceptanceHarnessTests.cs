using System.Diagnostics;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// All processes are synthetic local cmd children created by this test.
/// No PostgreSQL, private images, FFmpeg, CLIP, Ollama or Azure access.
/// </summary>
public sealed class VideoFrameAcceptanceHarnessTests
{
    private static string[] SelectedArgs(string root) =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_WI0190_TEST_CONN",
        "--database", "photoidentity_test",
        "--proxy-job-id", "9d20dafa-282e-4240-914c-4cc5328185a4",
        "--cache-root", root, "--scratch-root", root,
        "--ffmpeg", Path.Combine(root, "ffmpeg.exe"),
        "--ffmpeg-sha256", new string('a', 64),
        "--clip-model", Path.Combine(root, "model.onnx"),
        "--clip-model-sha256", new string('b', 64),
        "--clip-vocab", Path.Combine(root, "vocab.json"),
        "--clip-merges", Path.Combine(root, "merges.txt"),
        "--ollama-sha256", new string('c', 64),
        "--max-frames", "4",
        "--apply", "--acknowledge-isolated-v38-rehearsal",
        "--acknowledge-frame-disclosure"
    ];

    [Fact]
    public void Harness_requires_both_selected_disclosure_acks_and_separate_termination_gate()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-evidence-fixture");
        string[] selected = SelectedArgs(root);
        var plan = VideoFrameAcceptanceHarnessOptions.Parse(
            ["--mode", "measure", "--", .. selected]);
        Assert.False(plan.Execute);
        Assert.Equal("measure", plan.Mode);
        Assert.Equal(4, plan.Selected.Frames);

        var interruptPreview = VideoFrameAcceptanceHarnessOptions.Parse([
            "--mode", "interrupt", "--terminate-after-seconds", "5",
            "--", .. selected]);
        Assert.False(interruptPreview.Execute);
        Assert.False(interruptPreview.AcknowledgeChildTermination);
        var authorized = VideoFrameAcceptanceHarnessOptions.Parse([
            "--mode", "interrupt", "--terminate-after-seconds", "5",
            "--execute", "--acknowledge-owned-child-termination",
            "--", .. selected]);
        Assert.True(authorized.Execute);
        Assert.True(authorized.AcknowledgeChildTermination);

        foreach (string[] forbidden in new[]
        {
            new[] { "--mode", "interrupt", "--terminate-after-seconds", "5",
                "--execute" },
            new[] { "--mode", "interrupt", "--terminate-after-seconds", "4" },
            new[] { "--mode", "interrupt", "--terminate-after-seconds", "601" },
            new[] { "--mode", "measure",
                "--acknowledge-owned-child-termination" },
            new[] { "--mode", "measure", "--terminate-after-seconds", "10" },
            new[] { "--mode", "bogus" },
            new[] { "--mode", "measure", "--mode", "interrupt" },
            new[] { "--execute", "--execute", "--mode", "measure" },
        })
            Assert.Throws<ArgumentException>(() =>
                VideoFrameAcceptanceHarnessOptions.Parse(
                    [.. forbidden, "--", .. selected]));

        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceHarnessOptions.Parse([
                "--mode", "measure", "--execute", "--",
                .. selected.Where(x => x != "--acknowledge-frame-disclosure")]));
    }

    [Fact]
    public async Task Preview_never_starts_a_child_or_opens_Postgres_or_model()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-frame-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,
            VideoFrameEvidenceScratchGuard.MarkerName),
            VideoFrameEvidenceScratchGuard.MarkerContents);
        try
        {
            var options = VideoFrameAcceptanceHarnessOptions.Parse([
                "--mode", "interrupt", "--terminate-after-seconds", "5",
                "--", .. SelectedArgs(root)]);
            using StringWriter output = new();
            Assert.Equal(0, await VideoFrameAcceptanceHarnessCommandRunner.RunAsync(
                options, output));
            string report = output.ToString();
            Assert.Contains("\"executed\":false", report);
            Assert.Contains("\"childStarted\":false", report);
            Assert.Contains("\"reasonCode\":\"preview-only\"", report);
            Assert.DoesNotContain(root, report);
            Assert.DoesNotContain(options.Selected.ProxyJobId.ToString(), report);
            Assert.Equal(1, Directory.GetFileSystemEntries(root).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Only_dotnet_or_PhotoIdentity_Cli_host_can_be_launched()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "pi-harness-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string dll = Path.Combine(root, "PhotoIdentity.Cli.dll");
            string dotnet = Path.Combine(root, "dotnet.exe");
            string unsafeHost = Path.Combine(root, "arbitrary.exe");
            File.WriteAllText(dll, "fixture");
            File.WriteAllText(dotnet, "fixture");
            File.WriteAllText(unsafeHost, "fixture");
            ProcessStartInfo start =
                VideoFrameAcceptanceHarnessCommandRunner.BuildOwnCliStart(
                    dotnet, dll, ["--max-frames", "4"]);
            Assert.False(start.UseShellExecute);
            Assert.True(start.RedirectStandardOutput);
            Assert.True(start.RedirectStandardError);
            Assert.Equal(dll, start.ArgumentList[0]);
            Assert.Equal("selected-one-shot", start.ArgumentList[3]);
            Assert.Equal("--max-frames", start.ArgumentList[4]);
            Assert.Throws<InvalidOperationException>(() =>
                VideoFrameAcceptanceHarnessCommandRunner.BuildOwnCliStart(
                    unsafeHost, dll, ["--max-frames", "4"]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Synthetic_child_output_is_discarded_and_CPU_is_partial()
    {
        if (!OperatingSystem.IsWindows()) return;
        string shell = Environment.GetEnvironmentVariable("ComSpec") ??
            Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var start = new ProcessStartInfo(shell)
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("echo PRIVATE_SYNTHETIC_FRAME_TEXT");
        VideoFrameAcceptanceProcessObservation observed =
            await VideoFrameAcceptanceHarnessCommandRunner.ObserveStartedChildAsync(
                start, "measure", 0);
        Assert.True(observed.ChildStarted);
        Assert.True(observed.ChildExited);
        Assert.Equal(0, observed.ChildExitCode);
        Assert.False(observed.TerminationRequested);
        Assert.Equal("child-completed", observed.ReasonCode);
        Assert.True(observed.WallTimeMs >= 0);
        Assert.DoesNotContain("PRIVATE_SYNTHETIC",
            System.Text.Json.JsonSerializer.Serialize(observed));
    }

    [Fact]
    public async Task Synthetic_long_running_child_is_owned_and_killed_only_on_explicit_deadline()
    {
        if (!OperatingSystem.IsWindows()) return;
        string shell = Environment.GetEnvironmentVariable("ComSpec") ??
            Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var start = new ProcessStartInfo(shell)
        {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("ping -n 30 127.0.0.1 >NUL");
        VideoFrameAcceptanceProcessObservation observed =
            await VideoFrameAcceptanceHarnessCommandRunner.ObserveStartedChildAsync(
                start, "interrupt", 5);
        Assert.True(observed.ChildStarted);
        Assert.True(observed.ChildExited);
        Assert.True(observed.TerminationRequested);
        Assert.Equal("owned-child-terminated", observed.ReasonCode);
        Assert.True(observed.WallTimeMs >= 4500);
    }

    private static readonly VideoFrameScratchRecoveryResult CleanScratch =
        new(0, 0, 0, 0, 0, 0, false, false);

    private static VideoFrameInterruptionSnapshot State(
        string status = "cancelled", bool lease = false,
        bool grant = false, bool control = false, bool selected = false,
        bool batch = false, long rows = 0) =>
        new(status, "FrameEvidence", lease, true, batch, rows,
            control, !control, selected, selected, grant);

    [Fact]
    public void Interruption_audit_requires_no_partial_batch_terminal_state_and_recovered_controls()
    {
        var complete = VideoFrameInterruptionAssessment.Assess(
            State(), CleanScratch);
        Assert.True(complete.RecoveryComplete);
        Assert.Equal("interruption-recovered", complete.ReasonCode);
        Assert.True(complete.NoPartialEvidence);
        Assert.True(complete.ControlDisarmed);

        Assert.Equal("published-or-partial-evidence",
            VideoFrameInterruptionAssessment.Assess(
                State(batch: true, rows: 3), CleanScratch).ReasonCode);
        Assert.Equal("grant-still-active",
            VideoFrameInterruptionAssessment.Assess(
                State(grant: true, control: true, selected: true), CleanScratch)
                .ReasonCode);
        Assert.Equal("lease-still-active",
            VideoFrameInterruptionAssessment.Assess(
                State(lease: true), CleanScratch).ReasonCode);
        Assert.Equal("control-not-disarmed",
            VideoFrameInterruptionAssessment.Assess(
                State(control: true, selected: true), CleanScratch).ReasonCode);
        Assert.Equal("job-not-terminal",
            VideoFrameInterruptionAssessment.Assess(
                State(status: "running"), CleanScratch).ReasonCode);
        Assert.Equal("scratch-not-clean",
            VideoFrameInterruptionAssessment.Assess(State(),
                CleanScratch with { OwnedWorkspaces = 1 }).ReasonCode);
        Assert.Equal("wrong-job-kind",
            VideoFrameInterruptionAssessment.Assess(
                State() with { DerivativeKind = "PlayableProxy" }, CleanScratch)
                .ReasonCode);
        Assert.Equal("job-not-found",
            VideoFrameInterruptionAssessment.Assess(null, CleanScratch)
                .ReasonCode);
    }

    [Fact]
    public void Interruption_audit_parser_refuses_unbounded_or_missing_job_input()
    {
        string[] args =
        [
            "--postgres-connection-env", "PHOTOIDENTITY_WI0190_CONN",
            "--database", "photoidentity_test",
            "--frame-job-id", Guid.NewGuid().ToString(),
            "--scratch-root", Path.GetFullPath(Path.Combine(
                Path.GetTempPath(), "photoidentity-frame-evidence-fixture"))
        ];
        var ok = VideoFrameInterruptionAuditOptions.Parse(args);
        Assert.NotEqual(Guid.Empty, ok.FrameJobId);
        Assert.Throws<ArgumentException>(() =>
            VideoFrameInterruptionAuditOptions.Parse(
                [.. args, "--database", "repeat"]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameInterruptionAuditOptions.Parse(
                args.Where(x => x != "--frame-job-id").ToArray()));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameInterruptionAuditOptions.Parse(
                [.. args[..4], "--frame-job-id", Guid.Empty.ToString(),
                    .. args[6..]]));
    }
}
