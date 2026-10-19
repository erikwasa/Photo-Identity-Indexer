using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoScratchJanitorTests
{
    [Fact]
    public async Task Never_deletes_unowned_or_recent_scratch_and_defaults_to_dry_run()
    {
        string scratchRoot = TempRoot();
        try
        {
            string recent = VideoScratchOwnership.Create(scratchRoot, "video-tool");
            string old = VideoScratchOwnership.Create(scratchRoot, "video-input");
            string unrelated = Path.Combine(scratchRoot, "keep-source");
            Directory.CreateDirectory(unrelated);
            File.WriteAllText(Path.Combine(unrelated, "original.mp4"), "original");
            File.SetLastWriteTimeUtc(
                Path.Combine(old, VideoScratchOwnership.MarkerName),
                DateTime.UtcNow.AddDays(-2));

            VideoScratchSweepResult preview = await VideoScratchJanitor.SweepAsync(
                scratchRoot, TimeSpan.FromHours(24));
            Assert.True(preview.DryRun);
            Assert.Equal(3, preview.Considered);
            Assert.Equal(1, preview.Eligible);
            Assert.Equal(0, preview.Deleted);
            Assert.True(Directory.Exists(old));

            VideoScratchSweepResult applied = await VideoScratchJanitor.SweepAsync(
                scratchRoot, TimeSpan.FromHours(24), apply: true);
            Assert.Equal(1, applied.Deleted);
            Assert.False(Directory.Exists(old));
            Assert.True(Directory.Exists(recent));
            Assert.True(File.Exists(Path.Combine(unrelated, "original.mp4")));
        }
        finally { Directory.Delete(scratchRoot, recursive: true); }
    }

    [Fact]
    public async Task Invalid_roots_and_recent_cleanup_are_rejected()
    {
        string scratchRoot = TempRoot();
        try
        {
            Assert.Throws<ArgumentException>(() =>
                VideoScratchOwnership.Create(scratchRoot, "photo-source"));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                VideoScratchJanitor.SweepAsync(
                    scratchRoot, TimeSpan.FromMinutes(30)));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                VideoScratchJanitor.SweepAsync(
                    scratchRoot, TimeSpan.FromHours(24), maximumDirectories: 257));
        }
        finally { Directory.Delete(scratchRoot, recursive: true); }
    }

    private static string TempRoot()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-janitor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
