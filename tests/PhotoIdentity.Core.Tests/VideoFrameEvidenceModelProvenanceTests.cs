using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoFrameEvidenceModelProvenanceTests
{
    [Fact]
    public void Caption_and_clip_provenance_are_both_required_when_caption_model_is_present()
    {
        VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
            new VideoFrameSamplingRequest(3m, MaximumFrames: 1));
        var expected = new VideoFrameEvidenceProvenance(
            AssetRevisionId.New(), plan.PolicyVersion,
            "wi0190-sdr-frame-jpeg-v1", "openai/clip-vit-base-patch32",
            new string('a', 64), "clip-rgb-opencv-cubic-shortest-edge-center-crop-224-v1")
        {
            CaptionModelId = "qwen2.5vl:3b",
            CaptionModelSha256 = new string('b', 64),
            CaptionPromptVersion = "wi-0128-neutral-visible-caption-v1"
        };
        VideoFrameEvidenceBatch Good(VideoFrameEvidenceProvenance source) => new(
            source, [new VideoFrameEvidenceItem(
                plan.Samples[0].TimestampMilliseconds, null, "An object.",
                new[] { 0.6f, 0.8f })]);
        string Result(VideoFrameEvidenceProvenance source) =>
            VideoFrameEvidenceContract.Validate(
                plan, Good(source), expected, true, false, true).ReasonCode;

        Assert.Equal("validated-not-published", Result(expected));
        Assert.Equal("provenance-invalid", Result(expected with {
            CaptionModelSha256 = new string('c', 64) }));
        Assert.Equal("provenance-invalid", Result(expected with {
            CaptionPromptVersion = "different-prompt" }));
        Assert.Equal("provenance-invalid", Result(expected with {
            CaptionModelId = "../unsafe" }));
        Assert.Equal("provenance-invalid", Result(expected with {
            CaptionModelSha256 = null }));
        Assert.Equal("provenance-invalid", Result(expected with {
            ModelSha256 = new string('d', 64) }));
    }
}
