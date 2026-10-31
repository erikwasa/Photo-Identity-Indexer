using System.Text.Json;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoSyntheticFramePilotTests
{
    private static string[] Valid() =>
    [
        "--scratch-root", Path.Combine(Path.GetTempPath(), "frame-synthetic-pilot"),
        "--ffmpeg", Path.Combine(Path.GetTempPath(), "ffmpeg.exe"),
        "--ffmpeg-sha256", new string('a', 64)
    ];

    [Fact]
    public void Pilot_is_dry_run_by_default_with_no_arbitrary_media_path_option()
    {
        var safe = VideoSyntheticFramePilotOptions.Parse(Valid());
        Assert.False(safe.Apply);
        Assert.Equal(new string('a', 64), safe.FfmpegSha256);
        Assert.True(VideoSyntheticFramePilotOptions.Parse([.. Valid(), "--apply"]).Apply);

        foreach (string[] additional in new[]
        {
            new[] { "--input", "family.mov" },
            new[] { "--video", "family.mp4" },
            new[] { "--database", "private" },
            new[] { "--profile", "720p" },
            new[] { "--apply", "--apply" },
            new[] { "--ffmpeg-sha256", new string('b', 64) }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoSyntheticFramePilotOptions.Parse([.. Valid(), .. additional]));

        Assert.Throws<ArgumentException>(() =>
            VideoSyntheticFramePilotOptions.Parse([]));
    }

    [Fact]
    public void Dedicated_scratch_marker_is_required_even_for_preview()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "wi0190-no-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var opts = VideoSyntheticFramePilotOptions.Parse([
                "--scratch-root", directory,
                "--ffmpeg", Path.Combine(directory, "ffmpeg.exe"),
                "--ffmpeg-sha256", new string('a', 64)
            ]);
            using StringWriter writer = new();
            Assert.ThrowsAny<Exception>(() =>
                VideoSyntheticFramePilotCommandRunner.RunAsync(opts, writer)
                    .GetAwaiter().GetResult());
            Assert.Equal(string.Empty, writer.ToString());
        }
        finally { Directory.Delete(directory); }
    }
}
