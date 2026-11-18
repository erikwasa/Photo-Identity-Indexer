using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceAdaptiveVisualCueTests
{
    private static ImageFrame GrayscaleFrame(byte[] luma)
    {
        Assert.Equal(64, luma.Length);
        var pixels = new byte[64 * 3];
        for (int i = 0; i < luma.Length; i++)
        {
            pixels[3 * i] = luma[i];
            pixels[3 * i + 1] = luma[i];
            pixels[3 * i + 2] = luma[i];
        }

        return new ImageFrame(new ImageSize(8, 8), PixelFormat.Bgr24, 24, pixels);
    }

    [Fact]
    public void Scene_and_motion_densify_both_sides_even_when_detector_found_no_faces()
    {
        var plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(42_000);
        Assert.True(plan.CoarseFrameCount >= 5);
        var dark = new byte[64];
        var moving = new byte[64];
        for (int i = 0; i < 8; i++) moving[i] = 255;
        var bright = Enumerable.Repeat((byte)255, 64).ToArray();
        var fingerprints = plan.FrameTimestampsMs.Select((_, index) =>
            VideoFaceAdaptiveVisualCueDeriver.Capture(GrayscaleFrame(
                index < 2 ? dark : index < 4 ? moving : bright))).ToArray();

        var cues = VideoFaceAdaptiveVisualCueDeriver.Derive(plan, fingerprints);
        Assert.Equal(4, cues.Count);
        Assert.Equal(new[]
        {
            new VideoFaceSamplingCue(plan.FrameTimestampsMs[1], VideoFaceSamplingCueKind.Motion),
            new VideoFaceSamplingCue(plan.FrameTimestampsMs[2], VideoFaceSamplingCueKind.Motion),
            new VideoFaceSamplingCue(plan.FrameTimestampsMs[3], VideoFaceSamplingCueKind.SceneChange),
            new VideoFaceSamplingCue(plan.FrameTimestampsMs[4], VideoFaceSamplingCueKind.SceneChange),
        }, cues);

        var refined = VideoFaceAdaptiveSamplingPlanner.Refine(plan, cues);
        Assert.True(refined.FrameTimestampsMs.Count > plan.CoarseFrameCount);
        Assert.All(plan.FrameTimestampsMs, time =>
            Assert.Contains(time, refined.FrameTimestampsMs));
        Assert.False(refined.RefinementBudgetReached);
    }

    [Fact]
    public void Unchanged_or_tiny_pixel_variations_do_not_spend_refinement_budget()
    {
        var plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(600_000);
        byte[] uniform = new byte[64];
        byte[] tinyChange = new byte[64];
        tinyChange[0] = 255;
        tinyChange[1] = 255;
        tinyChange[2] = 255;
        var signatures = plan.FrameTimestampsMs.Select((_, i) =>
            i == 2 ? tinyChange : uniform).ToArray();
        Assert.Empty(VideoFaceAdaptiveVisualCueDeriver.Derive(plan, signatures));
        Assert.Empty(VideoFaceAdaptiveVisualCueDeriver.Derive(plan,
            plan.FrameTimestampsMs.Select(_ => uniform).ToArray()));
        Assert.Empty(VideoFaceAdaptiveSamplingPlanner.Refine(plan, []).FrameTimestampsMs
            .Except(plan.FrameTimestampsMs));
    }

    [Fact]
    public void Bgr_rgb_gray_and_bgra_inputs_yield_same_luma_signature()
    {
        byte[] bgr = new byte[8 * 8 * 3];
        byte[] rgb = new byte[8 * 8 * 3];
        byte[] bgra = new byte[8 * 8 * 4];
        byte[] gray = new byte[8 * 8];
        for (int i = 0; i < gray.Length; i++)
        {
            gray[i] = 90;
            bgr[i * 3] = 90;
            bgr[i * 3 + 1] = 90;
            bgr[i * 3 + 2] = 90;
            rgb[i * 3] = 90;
            rgb[i * 3 + 1] = 90;
            rgb[i * 3 + 2] = 90;
            bgra[i * 4] = 90;
            bgra[i * 4 + 1] = 90;
            bgra[i * 4 + 2] = 90;
            bgra[i * 4 + 3] = 255;
        }

        byte[] Capture(PixelFormat format, int stride, byte[] data) =>
            VideoFaceAdaptiveVisualCueDeriver.Capture(
                new ImageFrame(new ImageSize(8, 8), format, stride, data));
        Assert.Equal(Enumerable.Repeat((byte)90, 64), Capture(PixelFormat.Gray8, 8, gray));
        Assert.Equal(Capture(PixelFormat.Bgr24, 24, bgr), Capture(PixelFormat.Rgb24, 24, rgb));
        Assert.Equal(Capture(PixelFormat.Bgr24, 24, bgr), Capture(PixelFormat.Bgra32, 32, bgra));
    }

    [Fact]
    public void Incomplete_or_rewritten_coarse_evidence_fails_closed()
    {
        var plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(70_000);
        var valid = plan.FrameTimestampsMs.Select(_ => new byte[64]).ToArray();
        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveVisualCueDeriver.Derive(plan, valid.Skip(1).ToArray()));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveVisualCueDeriver.Derive(plan,
                valid.Select((x, i) => i == 1 ? new byte[63] : x).ToArray()));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveVisualCueDeriver.Derive(
                plan with { FrameTimestampsMs = plan.FrameTimestampsMs.Reverse().ToArray() },
                valid));
        Assert.Throws<ArgumentException>(() =>
            VideoFaceAdaptiveVisualCueDeriver.Capture(
                new ImageFrame(new ImageSize(4, 4), PixelFormat.Gray8, 4, new byte[16])));
    }

    [Fact]
    public void Last_window_stays_bounded_and_never_uses_an_other_window_timestamp()
    {
        var plan = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(1_450_000, 1_200_000);
        Assert.False(plan.HasMoreWindows);
        var signatures = plan.FrameTimestampsMs.Select((_, i) =>
            Enumerable.Repeat((byte)(i % 2 == 0 ? 0 : 255), 64).ToArray()).ToArray();
        var cues = VideoFaceAdaptiveVisualCueDeriver.Derive(plan, signatures);
        Assert.All(cues, cue => Assert.Contains(cue.ObservedFrameTimestampMs, plan.FrameTimestampsMs));
        var refined = VideoFaceAdaptiveSamplingPlanner.Refine(plan, cues);
        Assert.InRange(refined.FrameTimestampsMs.Count,
            plan.CoarseFrameCount, VideoFaceAdaptiveSamplingPlanner.MaximumFramesPerWindow);
        Assert.All(refined.FrameTimestampsMs, t =>
            Assert.InRange(t, plan.WindowStartMs, plan.WindowEndMs - 1));
    }
}
