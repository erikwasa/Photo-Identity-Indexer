using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoScratchCleanupCommandTests
{
    [Fact]
    public void Requires_explicit_absolute_root_and_safe_ranges()
    {
        Assert.Throws<ArgumentException>(() =>
            VideoScratchCleanupOptions.Parse([]));
        Assert.Throws<ArgumentException>(() =>
            VideoScratchCleanupOptions.Parse(["--scratch-root", "relative"]));
        Assert.Throws<ArgumentException>(() =>
            VideoScratchCleanupOptions.Parse(["--scratch-root", Path.GetTempPath(),
                "--unknown", "x"]));
        Assert.Throws<ArgumentException>(() =>
            VideoScratchCleanupOptions.Parse(["--scratch-root", Path.GetTempPath(),
                "--min-age-hours", "1"]));
        Assert.Throws<ArgumentException>(() =>
            VideoScratchCleanupOptions.Parse(["--scratch-root", Path.GetTempPath(),
                "--max-directories", "999"]));

        VideoScratchCleanupOptions safe = VideoScratchCleanupOptions.Parse(
            ["--scratch-root", Path.GetTempPath()]);
        Assert.False(safe.Apply);
        Assert.True(safe.MinimumAge >= TimeSpan.FromHours(24));

        VideoScratchCleanupOptions applied = VideoScratchCleanupOptions.Parse(
            ["--scratch-root", Path.GetTempPath(), "--apply"]);
        Assert.True(applied.Apply);
        Assert.Throws<ArgumentException>(() =>
            VideoScratchCleanupOptions.Parse(
                ["--scratch-root", Path.GetTempPath(), "--apply", "--apply"]));
    }
}
