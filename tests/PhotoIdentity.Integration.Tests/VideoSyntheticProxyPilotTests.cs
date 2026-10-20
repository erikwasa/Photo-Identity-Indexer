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
