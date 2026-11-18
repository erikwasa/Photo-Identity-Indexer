using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceAdaptiveSamplingPlannerTests
{
    [Theory]
    [InlineData(200L)]
    [InlineData(5_000L)]
    [InlineData(42_000L)]
    [InlineData(600_000L)]
    public void Coarse_plan_covers_whole_window_with_bounded_gaps(long duration)
    {
        VideoFaceSamplingPlan plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(duration);

        Assert.Equal(0, plan.WindowStartMs);
        Assert.Equal(duration, plan.WindowEndMs);
        Assert.False(plan.HasMoreWindows);
        Assert.False(plan.RefinementBudgetReached);
        Assert.Equal(plan.CoarseFrameCount, plan.FrameTimestampsMs.Count);
        Assert.InRange(plan.CoarseFrameCount, 1,
            VideoFaceAdaptiveSamplingPlanner.MaximumFramesPerWindow);
        Assert.All(plan.FrameTimestampsMs, t => Assert.InRange(t, 0, duration - 1));
        for (int i = 1; i < plan.FrameTimestampsMs.Count; i++)
        {
            Assert.InRange(
                plan.FrameTimestampsMs[i] - plan.FrameTimestampsMs[i - 1],
                1, VideoFaceAdaptiveSamplingPlanner.MaximumCoarseGapMs);
        }
    }

    [Fact]
    public void Long_video_requires_explicit_continuation_through_final_window()
    {
        const long duration = 1_450_000;
        var first = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(duration);
        var second = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(
            duration, first.WindowEndMs);
        var third = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(
            duration, second.WindowEndMs);

        Assert.Equal(600_000, first.WindowEndMs);
        Assert.Equal(1_200_000, second.WindowEndMs);
        Assert.Equal(duration, third.WindowEndMs);
        Assert.True(first.HasMoreWindows);
        Assert.True(second.HasMoreWindows);
        Assert.False(third.HasMoreWindows);
        Assert.Equal(first.WindowEndMs, second.WindowStartMs);
        Assert.Equal(second.WindowEndMs, third.WindowStartMs);
        Assert.All(new[] { first, second, third }, plan =>
            Assert.InRange(plan.FrameTimestampsMs.Count, 1,
                VideoFaceAdaptiveSamplingPlanner.MaximumFramesPerWindow));
    }

    [Fact]
    public void Refined_frames_are_bounded_stable_and_preserve_all_coarse_frames()
    {
        var coarse = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(60_000);
        long a = coarse.FrameTimestampsMs[2];
        long b = coarse.FrameTimestampsMs[5];
        var cues = new[]
        {
            new VideoFaceSamplingCue(b, VideoFaceSamplingCueKind.SceneChange),
            new VideoFaceSamplingCue(a, VideoFaceSamplingCueKind.PossibleNewOrPoorlyObservedFace),
            new VideoFaceSamplingCue(b, VideoFaceSamplingCueKind.SceneChange),
            new VideoFaceSamplingCue(a, VideoFaceSamplingCueKind.Motion),
        };

        var forward = VideoFaceAdaptiveSamplingPlanner.Refine(coarse, cues);
        var reverse = VideoFaceAdaptiveSamplingPlanner.Refine(coarse,
            cues.Reverse().ToArray());

        Assert.Equal(forward.FrameTimestampsMs.ToArray(),
            reverse.FrameTimestampsMs.ToArray());
        Assert.True(forward.FrameTimestampsMs.Count > coarse.FrameTimestampsMs.Count);
        Assert.False(forward.RefinementBudgetReached);
        Assert.All(coarse.FrameTimestampsMs,
            time => Assert.Contains(time, forward.FrameTimestampsMs));
        Assert.Equal(forward.FrameTimestampsMs.Count,
            forward.FrameTimestampsMs.Distinct().Count());
        Assert.Equal(forward.FrameTimestampsMs.OrderBy(t => t).ToArray(),
            forward.FrameTimestampsMs.ToArray());
    }

    [Fact]
    public void No_evidence_does_not_spend_additional_frames()
    {
        var coarse = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(90_000);
        var refined = VideoFaceAdaptiveSamplingPlanner.Refine(coarse, []);

        Assert.Equal(coarse.FrameTimestampsMs.ToArray(),
            refined.FrameTimestampsMs.ToArray());
        Assert.False(refined.RefinementBudgetReached);
    }

    [Fact]
    public void Busy_content_cannot_exceed_budget_and_reports_truncation()
    {
        var coarse = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(600_000);
        var cues = coarse.FrameTimestampsMs.Take(25)
            .Select(t => new VideoFaceSamplingCue(
                t, VideoFaceSamplingCueKind.PossibleEntranceOrExit))
            .ToArray();

        var refined = VideoFaceAdaptiveSamplingPlanner.Refine(coarse, cues);

        Assert.Equal(VideoFaceAdaptiveSamplingPlanner.MaximumFramesPerWindow,
            refined.FrameTimestampsMs.Count);
        Assert.True(refined.RefinementBudgetReached);
        Assert.Equal(coarse.CoarseFrameCount, refined.CoarseFrameCount);
        Assert.All(coarse.FrameTimestampsMs,
            t => Assert.Contains(t, refined.FrameTimestampsMs));
    }

    [Fact]
    public void Rejects_unplanned_or_invalid_content_signals_without_video_access()
    {
        var coarse = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(30_000);
        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveSamplingPlanner.Refine(coarse,
            [
                new VideoFaceSamplingCue(12_345, VideoFaceSamplingCueKind.UncertainFace),
            ]));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveSamplingPlanner.Refine(coarse,
            [
                new VideoFaceSamplingCue(coarse.FrameTimestampsMs[0],
                    (VideoFaceSamplingCueKind)99),
            ]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFaceAdaptiveSamplingPlanner.PlanCoverage(199));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFaceAdaptiveSamplingPlanner.PlanCoverage(3_600_001));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFaceAdaptiveSamplingPlanner.PlanCoverage(10_000, 10_000));
    }
}
