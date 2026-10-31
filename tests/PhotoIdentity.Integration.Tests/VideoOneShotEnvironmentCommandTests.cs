using System.Text.Json;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Postgres;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoOneShotEnvironmentCommandTests
{
    private static readonly string Hash = new('a', 64);

    private static string[] Options() =>
    [
        "--postgres-connection-env", "PHOTOIDENTITY_WI0188_TEST_ENV",
        "--database", "photoidentity_catalogue",
        "--cache-root", Path.GetFullPath("private-video-cache"),
        "--ffmpeg", Path.GetFullPath("physical-ffmpeg.exe"),
        "--ffmpeg-sha256", Hash,
        "--ffprobe", Path.GetFullPath("physical-ffprobe.exe"),
        "--ffprobe-sha256", Hash
    ];

    [Fact]
    public void No_real_video_media_or_writes_can_be_requested_through_preflight_parser()
    {
        VideoOneShotEnvironmentOptions parsed =
            VideoOneShotEnvironmentOptions.Parse(Options());
        Assert.Equal("photoidentity_catalogue", parsed.DatabaseName);
        foreach (string[] forbidden in new[]
        {
            new[] { "--apply" },
            new[] { "--acknowledge-isolated-v37-rehearsal" },
            new[] { "--job-id", Guid.NewGuid().ToString() },
            new[] { "--revision-id", Guid.NewGuid().ToString() },
            new[] { "--source-path", @"C:\\Archive\\private.mov" },
            new[] { "--ffmpeg", Path.GetFullPath("another-ffmpeg.exe") }
        })
        {
            Assert.Throws<ArgumentException>(() =>
                VideoOneShotEnvironmentOptions.Parse([.. Options(), .. forbidden]));
        }
        Assert.Throws<ArgumentException>(() =>
            VideoOneShotEnvironmentOptions.Parse([]));
        Assert.Throws<ArgumentException>(() =>
            VideoOneShotEnvironmentOptions.Parse(
                [.. Options().Skip(0).Take(1), "NOT/VALID", .. Options().Skip(2)]));
    }

    [Fact]
    public void Admission_gate_refuses_any_active_control_or_photo_priority()
    {
        var stopped = new VideoProcessingControlState(
            Enabled: false, Paused: true, Generation: 0, LastAction: "initial");
        Assert.Null(VideoOneShotEnvironmentCommandRunner.EvaluateControl(
            stopped, photoWorkActive: false, runningVideo: false));
        Assert.Equal("photo-work-active",
            VideoOneShotEnvironmentCommandRunner.EvaluateControl(
                stopped, photoWorkActive: true, runningVideo: false));
        Assert.Equal("running-video-lease",
            VideoOneShotEnvironmentCommandRunner.EvaluateControl(
                stopped, photoWorkActive: false, runningVideo: true));
        Assert.Equal("video-control-not-stopped",
            VideoOneShotEnvironmentCommandRunner.EvaluateControl(
                stopped with { Enabled = true }, false, false));
        Assert.Equal("video-control-not-stopped",
            VideoOneShotEnvironmentCommandRunner.EvaluateControl(
                stopped with { Paused = false }, false, false));
        Assert.Equal("video-control-not-stopped",
            VideoOneShotEnvironmentCommandRunner.EvaluateControl(
                stopped with { SelectedJobId = Guid.NewGuid() }, false, false));
        Assert.Equal("video-control-not-stopped",
            VideoOneShotEnvironmentCommandRunner.EvaluateControl(
                stopped with { AuthorizationExpiresAtUtc = DateTimeOffset.UtcNow },
                false, false));
    }

    [Fact]
    public void Output_is_explicitly_preflight_only_and_never_claims_full_isolation()
    {
        using StringWriter report = new();
        VideoOneShotEnvironmentCommandRunner.Write(report, true, "preflight-only");
        string json = report.ToString();
        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement root = parsed.RootElement;
        Assert.Equal("wi0188-one-shot-environment-preflight-v1",
            root.GetProperty("protocol").GetString());
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.True(root.GetProperty("dryRun").GetBoolean());
        // The preflight reports the live schema contract, not a fixed v37 fixture.
        Assert.Equal(PostgresCatalogueDatabase.CurrentSchemaVersion,
            root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.GetProperty("jobsEnqueued").GetBoolean());
        Assert.False(root.GetProperty("originalsOpened").GetBoolean());
        Assert.False(root.GetProperty("oneDriveHydrationRequested").GetBoolean());
        Assert.False(root.GetProperty("videoWorkersActivated").GetBoolean());
        Assert.False(root.GetProperty("derivativesPublished").GetBoolean());
        Assert.False(root.GetProperty("hardDiskIoQuotaVerified").GetBoolean());
        Assert.False(root.GetProperty("networkSandboxVerified").GetBoolean());
        Assert.False(root.GetProperty("realVideoExecutionAuthorized").GetBoolean());
        foreach (string forbidden in new[]
        {
            "sourcePath", "sourceKey", "filename", "password",
            "connectionString", "token", "captureDate", "gps"
        })
            Assert.DoesNotContain(forbidden, json,
                StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() =>
            VideoOneShotEnvironmentCommandRunner.Write(
                report, false, @"C:\\Private\\Family.mov"));
    }

    [Fact]
    public async Task Absent_PostgreSQL_credentials_fail_before_database_access()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string variable = "PHOTOIDENTITY_WI0188_ENV_PREFLIGHT_MISSING";
        string? original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            string[] args = Options();
            args[1] = variable;
            var options = VideoOneShotEnvironmentOptions.Parse(args);
            using StringWriter report = new();
            await Assert.ThrowsAsync<ArgumentException>(() =>
                VideoOneShotEnvironmentCommandRunner.RunAsync(options, report));
            Assert.Equal(string.Empty, report.ToString());
        }
        finally { Environment.SetEnvironmentVariable(variable, original); }
    }
}
