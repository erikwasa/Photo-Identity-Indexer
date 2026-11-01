using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoFrameSceneSamplingIdentityTests
{
    [Fact]
    public void Exact_interval_frame_counts_have_distinct_job_keys_and_regenerate()
    {
        string legacyTwo = VideoFrameSceneSamplingIdentity.CreateInterval(2);
        string one = VideoFrameSceneSamplingIdentity.CreateInterval(1);
        string three = VideoFrameSceneSamplingIdentity.CreateInterval(3);
        string four = VideoFrameSceneSamplingIdentity.CreateInterval(4);
        Assert.Equal(VideoFrameSamplingPlanner.PolicyVersion, legacyTwo);
        Assert.Equal("video-frame-sampling-interval-v2-n1", one);
        Assert.Equal("video-frame-sampling-interval-v2-n3", three);
        Assert.Equal("video-frame-sampling-interval-v2-n4", four);
        Assert.Equal(4, new HashSet<string> { one, legacyTwo, three, four }.Count);
        foreach ((string policy, int count) in new[]
        {
            (one, 1), (legacyTwo, 2), (three, 3), (four, 4)
        })
        {
            Assert.True(VideoFrameSceneSamplingIdentity.Permits(policy, count));
            VideoFrameSamplingPlan restored =
                VideoFrameSceneSamplingIdentity.BuildPlan(12.3m, count, policy);
            Assert.Equal(policy, restored.PolicyVersion);
            Assert.Equal(count, restored.Samples.Count);
            Assert.All(restored.Samples, sample => Assert.Null(sample.SceneIndex));
        }
        Assert.False(VideoFrameSceneSamplingIdentity.Permits(four, 2));
        Assert.False(VideoFrameSceneSamplingIdentity.Permits(three, 4));
        Assert.False(VideoFrameSceneSamplingIdentity.Permits(
            "video-frame-sampling-interval-v2-n5", 5));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameSceneSamplingIdentity.BuildPlan(12.3m, 2, four));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFrameSceneSamplingIdentity.CreateInterval(5));

        var revision = PhotoIdentity.Core.Identifiers.AssetRevisionId.From(Guid.NewGuid());
        string Key(string policy) => new VideoDerivativeJobIdentity(
            revision, VideoDerivativeKind.FrameEvidence, policy,
            "wi0190-sdr-frame-jpeg-v1", new string('a', 64)).IdempotencyKey();
        Assert.NotEqual(Key(legacyTwo), Key(four));
        Assert.NotEqual(Key(three), Key(four));
        Assert.NotEqual(Key(one), Key(legacyTwo));
        // Old publications remain interpretable from the separate persisted
        // requested_frames count, even when the old policy had no suffix.
        Assert.Equal(1, VideoFrameSceneSamplingIdentity.BuildPlan(
            12.3m, 1, legacyTwo).Samples.Count);
    }

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
        // The existing VideoDerivativeJobIdentity ceiling is 80 characters.
        // Six maximal-width cut offsets fit without silently truncating
        // a private annotation or colliding with another job identity.
        string maximum = VideoFrameSceneSamplingIdentity.Create(
            2, [1000000L, 1400000L, 1800000L, 2200000L, 2600000L, 3000000L],
            3600000);
        Assert.Equal(80, maximum.Length);
        Assert.True(VideoFrameSceneSamplingIdentity.TryParse(
            maximum, out _, out long[] recovered));
        Assert.Equal(6, recovered.Length);

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
