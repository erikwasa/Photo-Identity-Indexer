using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoPosterCleanupCommandTests
{
    [Fact]
    public void Cleanup_requires_exact_source_database_and_video_opt_in()
    {
        string[] args =
        [
            "--source", "C:\\Archive", "--derivatives", "D:\\DerivativeRoot",
            "--postgres-connection-env", "PRIVATE_POSTGRES", "--database", "photoidentity"
        ];
        Assert.Throws<ArgumentException>(() => VideoPosterCleanupOptions.Parse(args));
        VideoPosterCleanupOptions dry = VideoPosterCleanupOptions.Parse(["--enable-video", .. args]);
        Assert.False(dry.Apply);
        Assert.Equal(25, dry.MaximumFiles);
        Assert.Equal(24, dry.MinimumAgeHours);
        VideoPosterCleanupOptions write = VideoPosterCleanupOptions.Parse(
            ["--enable-video", .. args, "--apply", "--max-files", "3", "--min-age-hours", "48"]);
        Assert.True(write.Apply);
        Assert.Equal(3, write.MaximumFiles);
        Assert.Equal(48, write.MinimumAgeHours);
        Assert.Throws<ArgumentException>(() => VideoPosterCleanupOptions.Parse(
            ["--enable-video", .. args, "--max-files", "26"]));
        Assert.Throws<ArgumentException>(() => VideoPosterCleanupOptions.Parse(
            ["--enable-video", .. args, "--min-age-hours", "1"]));
        Assert.Throws<ArgumentException>(() => VideoPosterCleanupOptions.Parse(
            ["--enable-video", .. args, "--apply", "--apply"]));
        Assert.Throws<ArgumentException>(() => VideoPosterCleanupOptions.Parse(
            ["--enable-video", .. args, "--unknown", "unsafe"]));
        Assert.Throws<ArgumentException>(() => VideoPosterCleanupOptions.Parse(
            ["--enable-video", .. args, "--database", "other"]));
    }

    [Fact]
    public void Only_canonical_poster_profile_paths_may_be_deleted()
    {
        AssetRevisionId id = AssetRevisionId.New();
        string tool = new('a', 64);
        string good = VideoPosterCommandRunner.RelativePosterPath(id, tool);
        Assert.True(VideoPosterCleanupCommandRunner.TryParseRelativePath(good,
            out AssetRevisionId actual, out string actualTool));
        Assert.Equal(id, actual);
        Assert.Equal(tool, actualTool);
        string[] invalid =
        [
            good.ToUpperInvariant(),
            good.Replace(".jpg", ".jpeg", StringComparison.Ordinal),
            good.Replace("/" + id.Value.ToString("D") + "/", "/../", StringComparison.Ordinal),
            good.Replace("/" + tool, "/../" + tool, StringComparison.Ordinal),
            good.Replace("/" + tool, "\\\"" + tool, StringComparison.Ordinal),
            good.Replace("video-posters/", "other/", StringComparison.Ordinal),
            good.Replace("wi0151-sdr-still-v1", "unreviewed-poster-profile", StringComparison.Ordinal),
            "video-posters/wi0151-sdr-still-v1/" + id.Value.ToString("D") + "/" + new string('b', 64) + ".jpg/extra",
            "../" + good,
            good.Replace("/", "\\", StringComparison.Ordinal),
        ];
        foreach (string path in invalid)
            Assert.False(VideoPosterCleanupCommandRunner.TryParseRelativePath(
                path, out _, out _), path);
    }

    [Fact]
    public void Grace_period_prevents_just_published_orphan_cleanup()
    {
        DateTimeOffset now = new(2026, 10, 16, 16, 0, 0, TimeSpan.Zero);
        Assert.False(VideoPosterCleanupCommandRunner.IsOldEnough(now.AddHours(-23), now, 24));
        Assert.True(VideoPosterCleanupCommandRunner.IsOldEnough(now.AddHours(-24), now, 24));
        Assert.True(VideoPosterCleanupCommandRunner.IsOldEnough(now.AddDays(-3), now, 24));
    }
}
