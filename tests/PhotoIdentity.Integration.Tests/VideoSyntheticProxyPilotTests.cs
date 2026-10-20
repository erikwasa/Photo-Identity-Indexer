using System.ComponentModel;
using System.Text.Json;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoSyntheticProxyPilotTests
{
    private static readonly string Hash = new('a', 64);

    private static string[] Arguments(string root) =>
    [
        "--scratch-root", root,
        "--ffmpeg", Path.Combine(root, "ffmpeg.exe"),
        "--ffmpeg-sha256", Hash,
        "--ffprobe", Path.Combine(root, "ffprobe.exe"),
        "--ffprobe-sha256", Hash
    ];

    [Fact]
    public void Safe_options_require_explicit_local_paths_and_pinned_binaries()
    {
        string root = Path.Combine(Path.GetTempPath(), "video-synthetic-test");
        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticProxyPilotOptions.Parse([]));
        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticProxyPilotOptions.Parse(
                [.. Arguments(root), "--apply", "--apply"]));
        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticProxyPilotOptions.Parse(
                [.. Arguments(root), "--scratch-root", root]));
        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticProxyPilotOptions.Parse(
                [.. Arguments(root), "--bad-option", "value"]));
        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticProxyPilotOptions.Parse(
                [.. Arguments(root).Skip(2), "--scratch-root", "relative-folder"]));
        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticProxyPilotOptions.Parse(
                [.. Arguments(root).Take(5), "not-a-sha256",
                    .. Arguments(root).Skip(6)]));
        VideoSyntheticProxyPilotOptions safe =
            VideoSyntheticProxyPilotOptions.Parse(Arguments(root));
        Assert.False(safe.Apply);
        Assert.Equal(root, safe.ScratchRoot);
        Assert.True(VideoSyntheticProxyPilotOptions.Parse(
            [.. Arguments(root), "--apply"]).Apply);
    }

    [Fact]
    public async Task Empty_marked_temp_root_allows_read_only_dry_run_without_tools()
    {
        string root = NewScratch();
        try
        {
            var options = VideoSyntheticProxyPilotOptions.Parse(Arguments(root));
            using StringWriter output = new();
            Assert.Equal(0, await VideoSyntheticProxyPilotCommandRunner
                .RunAsync(options, output));
            using JsonDocument report = JsonDocument.Parse(output.ToString());
            JsonElement fields = report.RootElement;
            Assert.Equal("not-run", fields.GetProperty("status").GetString());
            Assert.True(fields.GetProperty("dryRun").GetBoolean());
            Assert.False(fields.GetProperty("schemaUsed").GetBoolean());
            Assert.False(fields.GetProperty("originalsOpened").GetBoolean());
            Assert.False(fields.GetProperty("videoWorkersActivated").GetBoolean());
            Assert.Equal(0, fields.GetProperty("results").GetArrayLength());
            Assert.Single(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Fixture_root_refuses_unmarked_or_nonempty_directories()
    {
        string root = NewScratch();
        try
        {
            Assert.Equal(root, VideoSyntheticProxyPilotCommandRunner.ValidateRoot(root));
            string marker = Path.Combine(root,
                VideoSyntheticProxyPilotCommandRunner.RootMarker);
            File.WriteAllText(marker, "different-marker");
            Assert.Throws<InvalidOperationException>(() =>
                VideoSyntheticProxyPilotCommandRunner.ValidateRoot(root));
            File.WriteAllText(marker, VideoSyntheticProxyPilotCommandRunner.MarkerText);
            File.WriteAllText(Path.Combine(root, "existing-original.mov"), "private");
            Assert.Throws<InvalidOperationException>(() =>
                VideoSyntheticProxyPilotCommandRunner.ValidateRoot(root));
            Assert.True(File.Exists(Path.Combine(root, "existing-original.mov")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Arbitrary_media_directories_are_never_accepted_as_a_fixture_root()
    {
        string external = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "..", "video-archive-not-temp"));
        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticProxyPilotCommandRunner.ValidateRoot(external));
    }

    [Fact]
    public void Two_synthetic_generators_are_fixed_and_source_free()
    {
        string working = Path.Combine(Path.GetTempPath(), "synthetic-workspace");
        foreach ((int width, int height, bool audio) in new[]
                 { (1280, 720, true), (720, 1280, false) })
        {
            string[] argv = VideoSyntheticProxyPilotCommandRunner.GenerateArguments(
                working, width, height, audio).ToArray();
            Assert.Equal(Path.Combine(working, "output.mp4"), argv[^1]);
            Assert.Contains("testsrc2=size=" + width + "x" + height +
                ":rate=24:duration=3", argv);
            Assert.Contains("-nostdin", argv);
            Assert.Contains("-protocol_whitelist", argv);
            Assert.Contains("-threads", argv);
            Assert.Contains("1", argv);
            Assert.Contains("libx264", argv);
            Assert.Contains("+faststart", argv);
            Assert.Contains("bt709", argv);
            Assert.DoesNotContain(argv, x => x.Contains("http:", StringComparison.Ordinal) ||
                x.Contains("cuda", StringComparison.OrdinalIgnoreCase) ||
                x.Contains("nvenc", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(audio, argv.Contains("1:a:0"));
        }
        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticProxyPilotCommandRunner.GenerateArguments(
                working, 4000, 3000, true));
        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticProxyPilotCommandRunner.GenerateArguments(
                working, 1280, 720, false));
        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticProxyPilotCommandRunner.GenerateArguments(
                "relative-scratch", 1280, 720, true));
    }

    [Fact]
    public async Task Approved_installed_tools_can_roundtrip_two_synthetic_clips_without_a_catalogue()
    {
        if (!OperatingSystem.IsWindows()) return;
        // Optional developer machine/CI tool-installed evidence. This never
        // downloads software or opens user media; the normal build is fully
        // deterministic and skips when tool paths/hashes are not configured.
        string? ffmpeg = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_WI0188_TEST_FFMPEG");
        string? ffmpegHash = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_WI0188_TEST_FFMPEG_SHA256");
        string? ffprobe = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_WI0188_TEST_FFPROBE");
        string? ffprobeHash = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_WI0188_TEST_FFPROBE_SHA256");
        if (new[] { ffmpeg, ffmpegHash, ffprobe, ffprobeHash }
                .Any(string.IsNullOrWhiteSpace))
            return;

        string root = NewScratch();
        try
        {
            VideoSyntheticProxyPilotOptions options =
                VideoSyntheticProxyPilotOptions.Parse(
                [
                    "--scratch-root", root,
                    "--ffmpeg", ffmpeg!,
                    "--ffmpeg-sha256", ffmpegHash!,
                    "--ffprobe", ffprobe!,
                    "--ffprobe-sha256", ffprobeHash!,
                    "--apply"
                ]);
            using StringWriter output = new();
            Assert.Equal(0, await VideoSyntheticProxyPilotCommandRunner
                .RunAsync(options, output));
            using JsonDocument report = JsonDocument.Parse(output.ToString());
            JsonElement fields = report.RootElement;
            Assert.Equal("validated", fields.GetProperty("status").GetString());
            Assert.False(fields.GetProperty("dryRun").GetBoolean());
            JsonElement rows = fields.GetProperty("results");
            Assert.Equal(2, rows.GetArrayLength());
            Assert.Equal("landscape", rows[0].GetProperty("orientation").GetString());
            Assert.True(rows[0].GetProperty("hasAudio").GetBoolean());
            Assert.Equal("portrait", rows[1].GetProperty("orientation").GetString());
            Assert.False(rows[1].GetProperty("hasAudio").GetBoolean());
            Assert.All(rows.EnumerateArray(), item =>
            {
                Assert.True(item.GetProperty("encodedBytes").GetInt64() > 128);
                Assert.True(item.GetProperty("encodeMilliseconds").GetInt64() > 0);
            });
            Assert.Single(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Sanitized_failures_distinguish_subprocess_from_probe_without_messages()
    {
        Assert.Equal("native-process-setup",
            VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(
                new Win32Exception(5, @"C:\\secret\\original.mov")));
        Assert.Equal("timeout",
            VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(
                new TimeoutException("private local file location")));
        Assert.Equal("invalid-media-evidence",
            VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(
                new InvalidDataException("private video filename")));
        Assert.Equal("configuration-rejected",
            VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(
                new ArgumentException("private installed binary location")));
        Assert.Equal("filesystem-io",
            VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(
                new IOException("private scratch path")));
        Assert.Equal("execution-safety-rejected",
            VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(
                new InvalidOperationException("private user metadata")));
        Assert.Equal("unexpected-failure",
            VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(
                new Exception("private text")));

        var unsupported = new VideoProxyValidationException(
            "probe-tool-nonzero", 1);
        Assert.Equal("probe-tool-nonzero", unsupported.ReasonCode);
        Assert.Equal(1, unsupported.ToolExitCode);
        Assert.Equal("probe-tool-nonzero",
            VideoSyntheticProxyPilotCommandRunner.ClassifyFailure(unsupported));
        Assert.Throws<ArgumentException>(() =>
            new VideoProxyValidationException("C:\\not-a-safe-reason"));
    }

    [Fact]
    public async Task Missing_external_tool_produces_private_safe_first_stage_failure()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = NewScratch();
        try
        {
            // No subprocess can run: the options name deliberately absent
            // binaries and the existing unrelated archive is never accessed.
            VideoSyntheticProxyPilotOptions options =
                VideoSyntheticProxyPilotOptions.Parse(
                    [.. Arguments(root), "--apply"]);
            using StringWriter output = new();
            Assert.Equal(1, await VideoSyntheticProxyPilotCommandRunner
                .RunAsync(options, output));
            string reportJson = output.ToString();
            using JsonDocument report = JsonDocument.Parse(reportJson);
            JsonElement fields = report.RootElement;
            Assert.Equal("pilot-failed", fields.GetProperty("status").GetString());
            Assert.Equal("fixture-generation",
                fields.GetProperty("failureStage").GetString());
            Assert.Equal("landscape",
                fields.GetProperty("failureOrientation").GetString());
            Assert.Equal("configuration-rejected",
                fields.GetProperty("failureClass").GetString());
            Assert.Equal(JsonValueKind.Null,
                fields.GetProperty("toolExitCode").ValueKind);
            Assert.Equal(0, fields.GetProperty("results").GetArrayLength());
            Assert.False(fields.GetProperty("schemaUsed").GetBoolean());
            Assert.False(fields.GetProperty("originalsOpened").GetBoolean());
            Assert.False(fields.GetProperty("videoWorkersActivated").GetBoolean());
            Assert.DoesNotContain(root, reportJson, StringComparison.OrdinalIgnoreCase);
            Assert.Single(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string NewScratch()
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "photoidentity-synthetic-video-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, VideoSyntheticProxyPilotCommandRunner.RootMarker),
            VideoSyntheticProxyPilotCommandRunner.MarkerText);
        return dir;
    }
}
