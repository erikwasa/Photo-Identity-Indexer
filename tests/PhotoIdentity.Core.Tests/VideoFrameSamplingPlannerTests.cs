using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoFrameSamplingPlannerTests
{
    [Fact]
    public void Short_clip_uses_fewer_separated_samples_and_has_no_source_access()
    {
        VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
            new VideoFrameSamplingRequest(0.6m));
        Assert.Equal(VideoFrameSamplingPlanner.PolicyVersion, plan.PolicyVersion);
        Assert.Equal(VideoFrameSamplingMode.Interval, plan.Mode);
        Assert.Equal(600, plan.DurationMilliseconds);
        Assert.InRange(plan.Samples.Count, 1, 3);
        Assert.All(plan.Samples, frame =>
        {
            Assert.InRange(frame.TimestampMilliseconds, 0, 599);
            Assert.Null(frame.SceneIndex);
        });
        AssertSpacing(plan);
    }

    [Fact]
    public void Long_interval_plan_spreads_samples_and_respects_both_budgets()
    {
        VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
            new VideoFrameSamplingRequest(3_600m, MaximumFrames: 24,
                MaximumReservedDecodeMilliseconds: 6_000,
                ReservedDecodeMillisecondsPerFrame: 1_000));
        Assert.Equal(6, plan.Samples.Count);
        Assert.Equal(6_000, plan.ReservedDecodeMilliseconds);
        Assert.True(plan.Samples[0].TimestampMilliseconds > 0);
        Assert.True(plan.Samples[^1].TimestampMilliseconds > 3_000_000);
        AssertSpacing(plan);
    }

    [Fact]
    public void Scene_sampling_covers_each_scene_before_adding_second_frame()
    {
        var request = new VideoFrameSamplingRequest(30m,
            Mode: VideoFrameSamplingMode.SceneBoundaries,
            SceneBoundariesSeconds: [10m, 20m],
            MaximumFrames: 4, MaximumFramesPerScene: 2);
        VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(request);
        Assert.Equal(4, plan.Samples.Count);
        Assert.Equal(3, plan.Samples.Select(x => x.SceneIndex).Distinct().Count());
        Assert.Equal(2, plan.Samples.Count(x => x.SceneIndex == 0));
        AssertSpacing(plan);
        Assert.Equal(plan.Samples.ToArray(), VideoFrameSamplingPlanner.Plan(request).Samples.ToArray());
    }

    [Fact]
    public void Too_many_scenes_spread_across_full_duration_instead_of_truncating_early()
    {
        VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
            new VideoFrameSamplingRequest(200m,
                Mode: VideoFrameSamplingMode.SceneBoundaries,
                SceneBoundariesSeconds: Enumerable.Range(1, 19)
                    .Select(i => i * 10m).ToArray(),
                MaximumFrames: 4, MaximumFramesPerScene: 2));
        Assert.Equal(4, plan.Samples.Count);
        Assert.True(plan.Samples[^1].SceneIndex >= 16);
        Assert.True(plan.Samples[0].SceneIndex <= 3);
        AssertSpacing(plan);
    }

    [Fact]
    public void Tiny_scenes_cannot_create_duplicate_seek_targets()
    {
        VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
            new VideoFrameSamplingRequest(1m,
                Mode: VideoFrameSamplingMode.SceneBoundaries,
                SceneBoundariesSeconds: [0.1m, 0.2m, 0.3m, 0.4m, 0.5m],
                MaximumFrames: 12));
        AssertSpacing(plan);
        Assert.True(plan.Samples.Count < 12);
    }

    [Fact]
    public void Duration_frame_scene_and_reserved_decode_limits_fail_closed()
    {
        foreach (VideoFrameSamplingRequest request in new[]
        {
            new VideoFrameSamplingRequest(0m),
            new VideoFrameSamplingRequest(3_601m),
            new VideoFrameSamplingRequest(4m, MaximumFrames: 25),
            new VideoFrameSamplingRequest(4m, MaximumFrames: 0),
            new VideoFrameSamplingRequest(4m, MaximumFramesPerScene: 3),
            new VideoFrameSamplingRequest(4m, MaximumReservedDecodeMilliseconds: 48_001),
            new VideoFrameSamplingRequest(4m, ReservedDecodeMillisecondsPerFrame: 3_001),
            new VideoFrameSamplingRequest(4m, MaximumReservedDecodeMilliseconds: 250,
                ReservedDecodeMillisecondsPerFrame: 251),
            new VideoFrameSamplingRequest(4m, Mode: (VideoFrameSamplingMode)100)
        })
            Assert.Throws<ArgumentOutOfRangeException>(() => VideoFrameSamplingPlanner.Plan(request));
    }

    [Fact]
    public void Untrusted_scene_boundaries_are_rejected_before_any_decode()
    {
        foreach (decimal[] cuts in new[]
        {
            new[] { 0m }, new[] { 10m }, new[] { -1m },
            new[] { 7m, 6m }, new[] { 5m, 5m },
            new[] { 1.0001m, 1.0002m },
            Enumerable.Range(1, 129).Select(i => i * 0.05m).ToArray()
        })
        {
            Assert.Throws<ArgumentException>(() =>
                VideoFrameSamplingPlanner.Plan(
                    new VideoFrameSamplingRequest(10m,
                        Mode: VideoFrameSamplingMode.SceneBoundaries,
                        SceneBoundariesSeconds: cuts)));
        }
        Assert.Throws<ArgumentException>(() => VideoFrameSamplingPlanner.Plan(
            new VideoFrameSamplingRequest(10m, SceneBoundariesSeconds: [5m])));
    }

    private static void AssertSpacing(VideoFrameSamplingPlan plan)
    {
        Assert.True(plan.Samples.Count <= VideoFrameSamplingPlanner.HardMaximumFrames);
        Assert.True(plan.ReservedDecodeMilliseconds <= 48_000);
        for (int i = 1; i < plan.Samples.Count; i++)
            Assert.True(plan.Samples[i].TimestampMilliseconds -
                plan.Samples[i - 1].TimestampMilliseconds >=
                VideoFrameSamplingPlanner.MinimumSpacingMilliseconds);
    }
}
