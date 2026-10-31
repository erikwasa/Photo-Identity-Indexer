using PhotoIdentity.Cli;
using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoFrameExtractionProfileTests
{
    [Fact]
    public void Frame_command_is_fixed_local_sdr_and_one_output_with_accurate_seek()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "frame-profile"));
        string source = Path.Combine(root, "generated.mp4");
        string workspace = Path.Combine(root, "tool");
        IReadOnlyList<string> args = VideoFrameExtractionProfile.BuildArguments(
            source, workspace, new VideoFrameSample(1500, null), 3000);
        Assert.Contains("-autorotate", args);
        Assert.Contains("-protocol_whitelist", args);
        Assert.Contains("file", args);
        Assert.Contains("1.500", args);
        Assert.Contains(VideoFrameExtractionProfile.VideoFilter, args);
        Assert.Equal(Path.Combine(workspace, "output.jpg"), args[^1]);
        Assert.Equal(1, args.Count(arg => arg == "-i"));
        Assert.True(args.ToList().IndexOf("-i") < args.ToList().IndexOf("-ss"));
        Assert.Contains("-an", args);
        Assert.Contains("-sn", args);
        Assert.Contains("-dn", args);
        Assert.DoesNotContain("http", string.Join(" ", args));
    }

    [Fact]
    public void Out_of_window_and_untrusted_paths_refuse_before_tool_execution()
    {
        string root = Path.GetFullPath(Path.GetTempPath());
        string valid = Path.Combine(root, "fixture.mp4");
        foreach (long timestamp in new long[] { -1, 3000, 4000 })
            Assert.Throws<ArgumentException>(() =>
                VideoFrameExtractionProfile.BuildArguments(
                    valid, root, new VideoFrameSample(timestamp, null), 3000));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameExtractionProfile.BuildArguments(
                "relative.mov", root, new VideoFrameSample(500, null), 3000));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameExtractionProfile.BuildArguments(
                valid, "relative", new VideoFrameSample(500, null), 3000));
    }
}
