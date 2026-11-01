using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoFrameAcceptanceEvidenceTests
{
    private static VideoFrameAcceptanceObservation Observation(
        params string[] categories) => new(
            Guid.NewGuid(), categories,
            2, 12_300, 4_247,
            new VideoFrameAcceptanceSceneCoverage(2, 3, 2, 1),
            1000, 350, null, null, 2, 1);

    [Fact]
    public void Aggregates_coverage_payload_throughput_and_indicates_missing_measurements()
    {
        var first = Observation("short", "multi-scene");
        var second = Observation("long", "rotated", "low-light") with
        {
            DurationMs = 900_000,
            EvidencePayloadBytes = 8_000,
            AnnotatedCoverage = null,
            ExecutionWallMs = null,
            CpuTimeMs = null,
            GpuTimeMs = 600,
            AzureCostMinorUnits = 45,
            CaptionsJudged = null,
            CaptionsAccurate = null
        };
        var result = VideoFrameAcceptanceEvidence.Summarize([first, second]);
        Assert.Equal(2, result.Clips);
        Assert.Equal(4, result.Frames);
        Assert.Equal(12_247, result.EvidencePayloadBytes);
        Assert.Equal(6123, result.MeanEvidencePayloadBytesPerClip);
        Assert.Equal(12_300, result.MinDurationMs);
        Assert.Equal(900_000, result.MaxDurationMs);
        Assert.Empty(result.MissingCategories);
        Assert.Equal(1, result.CategoryClipCounts["low-light"]);
        Assert.Equal(1, result.AnnotatedClips);
        Assert.Equal(3, result.AnnotatedScenes);
        Assert.Equal(2, result.CoveredScenes);
        Assert.Equal(1, result.MissedScenes);
        Assert.Equal(66.67, result.WeightedSceneCoveragePercent);
        Assert.Equal(1, result.ExecutionTimedClips);
        Assert.Equal(1000, result.ObservedExecutionWallMs);
        Assert.Equal(120, result.ObservedFramesPerMinute);
        Assert.Equal(1, result.CpuMeasuredClips);
        Assert.Equal(350, result.ObservedCpuTimeMs);
        Assert.Equal(1, result.GpuMeasuredClips);
        Assert.Equal(600, result.ObservedGpuTimeMs);
        Assert.Equal(1, result.AzureCostMeasuredClips);
        Assert.Equal(45, result.ObservedAzureCostMinorUnits);
        Assert.Equal(1, result.CaptionJudgedClips);
        Assert.Equal(2, result.CaptionsJudged);
        Assert.Equal(1, result.CaptionsAccurate);
        Assert.Equal(50, result.HumanCaptionAccuracyPercent);
        Assert.True(result.HumanEvidenceSelfReported);
        Assert.False(result.FullAcceptanceEstablished);
    }

    [Fact]
    public void Missing_manual_annotations_and_costs_do_not_turn_into_zero_cost_or_full_coverage()
    {
        var single = Observation("short") with
        {
            AnnotatedCoverage = null,
            ExecutionWallMs = null, CpuTimeMs = null, GpuTimeMs = null,
            AzureCostMinorUnits = null, CaptionsJudged = null, CaptionsAccurate = null
        };
        var outcome = VideoFrameAcceptanceEvidence.Summarize([single]);
        Assert.Equal(0, outcome.AnnotatedClips);
        Assert.Null(outcome.WeightedSceneCoveragePercent);
        Assert.Null(outcome.ObservedFramesPerMinute);
        Assert.Null(outcome.HumanCaptionAccuracyPercent);
        Assert.Equal(0, outcome.CpuMeasuredClips);
        Assert.Equal(0, outcome.GpuMeasuredClips);
        Assert.Equal(0, outcome.AzureCostMeasuredClips);
        Assert.Contains("multi-scene", outcome.MissingCategories);
        Assert.Contains("low-light", outcome.MissingCategories);
    }

    [Fact]
    public void Rejects_duplicate_identity_missing_labels_and_inconsistent_caption_evidence()
    {
        var first = Observation("short");
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceEvidence.Summarize([]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceEvidence.Summarize([first, first]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceEvidence.Summarize([first with { Categories = [] }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceEvidence.Summarize(
                [first with { Categories = ["short", "short"] }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceEvidence.Summarize(
                [first with { Categories = ["not-a-class"] }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceEvidence.Summarize(
                [first with { AnnotatedCoverage =
                    new VideoFrameAcceptanceSceneCoverage(2, 3, 3, 1) }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceEvidence.Summarize(
                [first with { CaptionsJudged = 3, CaptionsAccurate = 1 }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceEvidence.Summarize(
                [first with { CaptionsJudged = null, CaptionsAccurate = 1 }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceEvidence.Summarize(
                [first with { CpuTimeMs = -1 }]));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameAcceptanceEvidence.Summarize(
                [first with { EvidencePayloadBytes = 0 }]));
    }
}
