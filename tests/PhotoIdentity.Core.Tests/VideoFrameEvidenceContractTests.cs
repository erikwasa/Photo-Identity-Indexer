using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoFrameEvidenceContractTests
{
    private static readonly VideoFrameSamplingPlan Plan = VideoFrameSamplingPlanner.Plan(
        new VideoFrameSamplingRequest(4m, MaximumFrames: 3));
    private static readonly VideoFrameEvidenceProvenance Provenance = new(
        AssetRevisionId.New(),
        VideoFrameSamplingPlanner.PolicyVersion,
        "wi0190-sdr-frame-jpeg-v1", "openai/clip-vit-base-patch32",
        new string('a', 64),
        "clip-rgb-opencv-cubic-shortest-edge-center-crop-224-v1");

    private static VideoFrameEvidenceBatch Good() => new(Provenance,
        Plan.Samples.Select(s => new VideoFrameEvidenceItem(
            s.TimestampMilliseconds, s.SceneIndex, "Visible objects.",
            new float[] { 0.6f, 0.8f })).ToArray());

    private static VideoFrameEvidenceValidation Verify(
        VideoFrameEvidenceBatch batch, bool current = true,
        bool excluded = false, bool complete = true) =>
        VideoFrameEvidenceContract.Validate(Plan, batch, Provenance,
            current, excluded, complete);

    [Fact]
    public void Exact_full_batch_is_only_validated_not_persisted()
    {
        VideoFrameEvidenceValidation result = Verify(Good());
        Assert.True(result.Accepted);
        Assert.Equal("validated-not-published", result.ReasonCode);
        Assert.Equal(3, result.FrameCount);
    }

    [Theory]
    [InlineData("revision-unavailable", false, false, true)]
    [InlineData("revision-unavailable", true, true, true)]
    [InlineData("incomplete-batch", true, false, false)]
    public void Privacy_revision_and_partial_completion_refuse(
        string reason, bool current, bool excluded, bool complete)
    {
        Assert.Equal(reason, Verify(Good(), current, excluded, complete).ReasonCode);
    }

    [Fact]
    public void Partial_missing_or_wrong_timestamp_refuses()
    {
        VideoFrameEvidenceBatch full = Good();
        Assert.Equal("frame-count-mismatch",
            Verify(full with { Items = full.Items.Take(2).ToArray() }).ReasonCode);
        var reordered = full.Items.Reverse().ToArray();
        Assert.Equal("timestamp-mismatch",
            Verify(full with { Items = reordered }).ReasonCode);
        var altered = full.Items.ToArray();
        altered[1] = altered[1] with { TimestampMilliseconds = altered[1].TimestampMilliseconds + 1 };
        Assert.Equal("timestamp-mismatch",
            Verify(full with { Items = altered }).ReasonCode);
    }

    [Fact]
    public void Model_and_sampling_provenance_must_match_current_pilot_contract()
    {
        VideoFrameEvidenceBatch full = Good();
        Assert.Equal("provenance-invalid",
            Verify(full with { Provenance = Provenance with { ModelSha256 = new string('A', 64) } }).ReasonCode);
        Assert.Equal("provenance-invalid",
            Verify(full with { Provenance = Provenance with { SamplingPolicyVersion = "old-policy" } }).ReasonCode);
        Assert.Equal("provenance-invalid",
            Verify(full with { Provenance = Provenance with { RevisionId = default } }).ReasonCode);
        Assert.Equal("provenance-invalid",
            Verify(full with { Provenance = Provenance with { ModelId = "../unsafe" } }).ReasonCode);
    }

    [Fact]
    public void Empty_or_non_finite_or_mismatched_evidence_refuses()
    {
        VideoFrameEvidenceBatch full = Good();
        var copy = full.Items.ToArray();
        copy[0] = copy[0] with { Caption = null, Embedding = null };
        Assert.Equal("frame-evidence-empty",
            Verify(full with { Items = copy }).ReasonCode);
        copy = full.Items.ToArray();
        copy[0] = copy[0] with { Caption = " private\nname" };
        Assert.Equal("caption-invalid",
            Verify(full with { Items = copy }).ReasonCode);
        copy = full.Items.ToArray();
        copy[0] = copy[0] with { Embedding = [float.NaN, 0.8f] };
        Assert.Equal("embedding-invalid",
            Verify(full with { Items = copy }).ReasonCode);
        copy = full.Items.ToArray();
        copy[0] = copy[0] with { Embedding = [1.0f] };
        Assert.Equal("embedding-dimensions-mismatch",
            Verify(full with { Items = copy }).ReasonCode);
    }

    [Fact]
    public void Rejected_batches_do_not_claim_any_published_frame_count()
    {
        VideoFrameEvidenceBatch full = Good();
        var noEmbedding = full.Items.Select(item => item with { Embedding = null }).ToArray();
        VideoFrameEvidenceValidation refused = Verify(full with { Items = noEmbedding });
        Assert.False(refused.Accepted);
        Assert.Equal("missing-evidence-kind", refused.ReasonCode);
        Assert.Equal(0, refused.FrameCount);
    }
}
