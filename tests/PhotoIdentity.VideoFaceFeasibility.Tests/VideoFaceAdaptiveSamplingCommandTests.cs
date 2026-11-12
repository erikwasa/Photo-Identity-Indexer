using System.Text.Json;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceAdaptiveSamplingCommandTests
{
    [Fact]
    public void Source_free_plan_spans_the_current_window_without_a_people_shortcut()
    {
        var first = VideoFaceAdaptiveSamplingCommand.CreatePlan(1_350_000, 0);
        var next = VideoFaceAdaptiveSamplingCommand.CreatePlan(
            1_350_000, first.WindowEndMs);

        Assert.Equal(600_000, first.WindowEndMs);
        Assert.True(first.HasMoreWindows);
        Assert.Equal(600_000, next.WindowStartMs);
        Assert.All(first.FrameTimestampsMs, t =>
            Assert.InRange(t, first.WindowStartMs, first.WindowEndMs - 1));
        Assert.True(first.FrameTimestampsMs.Count < 180);
    }

    [Fact]
    public void Cues_refine_only_observed_samples_and_preserve_all_coarse_frames()
    {
        var coarse = VideoFaceAdaptiveSamplingCommand.CreatePlan(60_000, 0);
        long anchor = coarse.FrameTimestampsMs[3];
        var refined = VideoFaceAdaptiveSamplingCommand.CreatePlan(60_000, 0,
        [
            new(anchor, VideoFaceSamplingCueKind.PossibleNewOrPoorlyObservedFace),
        ]);

        Assert.True(refined.FrameTimestampsMs.Count > coarse.FrameTimestampsMs.Count);
        Assert.All(coarse.FrameTimestampsMs,
            t => Assert.Contains(t, refined.FrameTimestampsMs));
        Assert.Equal(coarse.CoarseFrameCount, refined.CoarseFrameCount);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"cues\":[{\"observedFrameTimestampMs\":100,\"kind\":\"Motion\"}]}",
        true)]
    [InlineData("{\"schemaVersion\":1,\"cues\":[]}", true)]
    [InlineData("{\"schemaVersion\":1,\"cues\":[{\"observedFrameTimestampMs\":100,\"kind\":4}]}",
        false)]
    [InlineData("{\"schemaVersion\":1,\"cues\":[{\"observedFrameTimestampMs\":100,\"kind\":\"SecretPerson\"}]}",
        false)]
    [InlineData("{\"schemaVersion\":1,\"cues\":[],\"personId\":\"private\"}", false)]
    [InlineData("{\"schemaVersion\":2,\"cues\":[]}", false)]
    public async Task Cue_file_accepts_only_bounded_source_free_schema(
        string input, bool accepted)
    {
        string folder = Path.Combine(Path.GetTempPath(),
            "wi0155-adaptive-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "cues.json");
        try
        {
            await File.WriteAllTextAsync(file, input);
            if (accepted)
            {
                var cues = await VideoFaceAdaptiveSamplingCommand.LoadCuesAsync(file);
                Assert.NotNull(cues);
            }
            else
            {
                await Assert.ThrowsAnyAsync<Exception>(async () =>
                    await VideoFaceAdaptiveSamplingCommand.LoadCuesAsync(file));
            }
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    [Fact]
    public async Task Valid_cue_must_refer_to_a_planned_initial_sample()
    {
        string folder = Path.Combine(Path.GetTempPath(),
            "wi0155-adaptive-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "cues.json");
        try
        {
            await File.WriteAllTextAsync(file,
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    cues = new[]
                    {
                        new { observedFrameTimestampMs = 12_345, kind = "Motion" },
                    },
                }));
            var cues = await VideoFaceAdaptiveSamplingCommand.LoadCuesAsync(file);
            Assert.Throws<ArgumentException>(() =>
                VideoFaceAdaptiveSamplingCommand.CreatePlan(60_000, 0, cues));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void Invalid_short_or_overlong_video_request_fails_before_extraction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFaceAdaptiveSamplingCommand.CreatePlan(199, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFaceAdaptiveSamplingCommand.CreatePlan(3_600_001, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFaceAdaptiveSamplingCommand.CreatePlan(60_000, 60_000));
    }
}
