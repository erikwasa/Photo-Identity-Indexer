using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoProxyPosterCommandTests
{
    private static string[] SafeOptions =>
    [
        "--enable-video",
        "--cache-root", "C:\\safe-cache",
        "--derivatives", "C:\\safe-posters",
        "--ffmpeg", "C:\\tools\\ffmpeg.exe",
        "--ffmpeg-sha256", new string('a', 64),
        "--ffprobe", "C:\\tools\\ffprobe.exe",
        "--ffprobe-sha256", new string('b', 64),
        "--postgres-connection-env", "TEST_POSTGRES_CONN",
        "--database", "test_db"
    ];

    [Fact]
    public void Defaults_to_dry_run_with_one_video_and_bounded_timeout()
    {
        VideoProxyPosterOptions options = VideoProxyPosterOptions.Parse(SafeOptions);
        Assert.False(options.Apply);
        Assert.Equal(1, options.MaximumVideos);
        Assert.Equal(30, options.TimeoutSeconds);
        Assert.Equal(new string('a', 64), options.FfmpegSha256);
    }

    [Fact]
    public void Explicit_apply_requires_opt_in_and_pinned_tool_digests()
    {
        Assert.True(VideoProxyPosterOptions.Parse(
            [.. SafeOptions, "--apply", "--max-videos", "3"]).Apply);
        Assert.Throws<ArgumentException>(() => VideoProxyPosterOptions.Parse(
            SafeOptions.Where(x => x != "--enable-video").ToArray()));
        Assert.Throws<ArgumentException>(() => VideoProxyPosterOptions.Parse(
            [.. SafeOptions, "--max-videos", "4"]));
        Assert.Throws<ArgumentException>(() => VideoProxyPosterOptions.Parse(
            [.. SafeOptions, "--timeout-seconds", "91"]));
        Assert.Throws<ArgumentException>(() => VideoProxyPosterOptions.Parse(
            [.. SafeOptions, "--apply", "--apply"]));
        Assert.Throws<ArgumentException>(() => VideoProxyPosterOptions.Parse(
            [.. SafeOptions, "--arbitrary-original", "C:\\private"]));
        string[] bad = (string[])SafeOptions.Clone();
        bad[Array.IndexOf(bad, "--ffprobe-sha256") + 1] = "untrusted";
        Assert.Throws<ArgumentException>(() => VideoProxyPosterOptions.Parse(bad));
    }

    [Fact]
    public void Renderer_reads_only_trusted_staged_local_mp4_and_outputs_one_jpeg()
    {
        IReadOnlyList<string> args = VideoProxyPosterCommandRunner.BuildRenderArguments(
            "C:\\derived\\verified.mp4", "C:\\derived\\poster.jpg", 20m);
        Assert.Equal("4.000", args[args.IndexOf("-ss") + 1]);
        Assert.Equal("C:\\derived\\verified.mp4", args[args.IndexOf("-i") + 1]);
        Assert.Equal("C:\\derived\\poster.jpg", args[^1]);
        Assert.Contains("file", args);
        Assert.Contains("-map_metadata", args);
        Assert.Contains("-map_chapters", args);
        Assert.Contains("-frames:v", args);
        Assert.Contains("1", args);
        Assert.Contains(args, a => a.Contains("thumbnail=n=8", StringComparison.Ordinal));
        Assert.DoesNotContain("-filter_complex", args);
        Assert.DoesNotContain("http", args);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoProxyPosterCommandRunner.BuildRenderArguments("a.mp4", "b.jpg", 0));
    }

    [Fact]
    public void Output_receipt_path_is_immutable_and_versioned()
    {
        AssetRevisionId revision = AssetRevisionId.From(Guid.NewGuid());
        string hash = new('c', 64);
        Assert.Equal($"video-posters/wi0211-proxy-still-v1/{revision.Value:D}/{hash}.jpg",
            VideoProxyPosterCommandRunner.RelativePosterPath(revision, hash));
        Assert.Throws<ArgumentException>(() =>
            VideoProxyPosterCommandRunner.RelativePosterPath(revision, "../../private"));
    }
}
