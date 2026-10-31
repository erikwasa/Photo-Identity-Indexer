using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoFrameSceneSamplingIdentityTests
{
    [Fact]
    public void Annotated_cuts_are_regenerable_and_choose_different_scenes()
    {
        string identity = VideoFrameSceneSamplingIdentity.Create(
            2, [3000L, 6000L, 9000L], 12300);
        Assert.Equal("video-frame-sampling-scene-v1-n2-3000_6000_9000", identity);
        Assert.True(VideoFrameSceneSamplingIdentity.TryParse(identity,
            out int frames, out long[] decoded));
        Assert.Equal(2, frames);
        Assert.Equal(new long[] { 3000, 6000, 9000 }, decoded);
        VideoFrameSamplingPlan actual = VideoFrameSceneSamplingIdentity.BuildPlan(
            12.3m, 2, identity);
        Assert.Equal(VideoFrameSamplingMode.SceneBoundaries, actual.Mode);
        Assert.Equal(identity, actual.PolicyVersion);
        Assert.Equal(2, actual.Samples.Count);
        Assert.NotNull(actual.Samples[0].SceneIndex);
        Assert.NotNull(actual.Samples[1].SceneIndex);
        Assert.NotEqual(actual.Samples[0].SceneIndex, actual.Samples[1].SceneIndex);
        Assert.True(actual.Samples[0].TimestampMilliseconds <
                    actual.Samples[1].TimestampMilliseconds);
        Assert.True(VideoFrameSceneSamplingIdentity.Permits(identity, 2));
        Assert.False(VideoFrameSceneSamplingIdentity.Permits(identity, 1));

        VideoFrameSamplingPlan interval = VideoFrameSceneSamplingIdentity.BuildPlan(
            12.3m, 2, VideoFrameSamplingPlanner.PolicyVersion);
        Assert.Equal(VideoFrameSamplingMode.Interval, interval.Mode);
        Assert.All(interval.Samples, sample => Assert.Null(sample.SceneIndex));
        Assert.NotEqual(interval.PolicyVersion, actual.PolicyVersion);
    }

    [Fact]
    public void Different_cuts_and_frame_budget_have_independent_identities()
    {
        string cutsA = VideoFrameSceneSamplingIdentity.Create(2, [3000L, 6000L], 12000);
        string cutsB = VideoFrameSceneSamplingIdentity.Create(2, [3001L, 6000L], 12000);
        string oneFrame = VideoFrameSceneSamplingIdentity.Create(1, [3000L, 6000L], 12000);
        Assert.NotEqual(cutsA, cutsB);
        Assert.NotEqual(cutsA, oneFrame);
        Assert.NotEqual(VideoFrameSamplingPlanner.PolicyVersion, cutsA);
        Assert.True(VideoFrameSceneSamplingIdentity.TryParse(cutsB, out _, out long[] cuts));
        Assert.Equal(new long[] { 3001, 6000 }, cuts);
    }

    [Fact]
    public void Rejects_invalid_noncanonical_or_unsupported_scene_policy()
    {
        foreach (string? invalid in new string?[]
        {
            null, "", "video-frame-sampling-scene-v1-n2",
            "video-frame-sampling-scene-v1-n2-",
            "video-frame-sampling-scene-v1-n0-1000",
            "video-frame-sampling-scene-v1-n5-1000",
            "video-frame-sampling-scene-v1-n2-0",
            "video-frame-sampling-scene-v1-n2-0100",
            "video-frame-sampling-scene-v1-n2-1000_1000",
            "video-frame-sampling-scene-v1-n2-2000_1000",
            "video-frame-sampling-scene-v1-n2-1000_-2000",
            "video-frame-sampling-scene-v1-n2-1000,2000",
            "video-frame-sampling-scene-v1-n2-3600000",
            "video-frame-sampling-scene-v1-n2-1000_",
            "video-frame-sampling-scene-v1-n2-1000_+2000",
            "video-frame-sampling-scene-v1-n2-1000_" + new string('1', 100),
            "video-frame-sampling-scene-v1-n2-1_2_3_4_5_6_7_8_9"
        })
            Assert.False(VideoFrameSceneSamplingIdentity.TryParse(
                invalid, out _, out _));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFrameSceneSamplingIdentity.Create(2, [], 12000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFrameSceneSamplingIdentity.Create(2,
                [1000L, 2000L, 3000L, 4000L, 5000L, 6000L, 7000L, 8000L, 9000L],
                12000));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneSamplingIdentity.Create(2, [5000L, 5000L], 12000));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneSamplingIdentity.Create(2, [12000L], 12000));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneSamplingIdentity.BuildPlan(
                12m, 1,
                VideoFrameSceneSamplingIdentity.Create(2, [6000L], 12000)));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneSamplingIdentity.BuildPlan(
                4m, 2,
                VideoFrameSceneSamplingIdentity.Create(2, [5000L], 12000)));
    }

    [Fact]
    public void Very_short_scene_annotations_refuse_nonunique_seek_positions()
    {
        string identity = VideoFrameSceneSamplingIdentity.Create(
            2, [100L], 300);
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneSamplingIdentity.BuildPlan(.3m, 2, identity));
    }
}
