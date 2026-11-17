using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceCenterFaceShadowTests
{
    private static readonly ImageSize Size = new(1280, 720);
    private static readonly NormalizedFaceLandmarks Points = new(
        new NormalizedPoint(.2, .2), new NormalizedPoint(.3, .2),
        new NormalizedPoint(.25, .27), new NormalizedPoint(.22, .34),
        new NormalizedPoint(.28, .34));

    private static DetectedFaceCandidate Detection(double x, double y, double confidence = .9) =>
        new(new NormalizedBoundingBox(x, y, .12, .20), Points, confidence);

    private static EmbeddingVector Vector(int index)
    {
        float[] values = new float[128];
        values[index] = 1;
        return new EmbeddingVector(values);
    }

    private static ModelDescriptor IndependentDescriptor() =>
        new(new ModelId("fake-centerface"),
            ModelRole.FaceDetection, ModelFormat.Onnx,
            new Sha256Digest(new string('d', 64)), new ImageSize(640, 640),
            "fake", "test", "test");

    private static VideoFaceCataloguePersonMatcher Matcher() =>
        new([
            new("P0001", new string('a', 64), Vector(0)),
            new("P0002", new string('b', 64), Vector(1)),
        ]);

    [Fact]
    public async Task Counterfactual_separates_correlated_background_from_confirmed_faces_without_losing_original_candidates()
    {
        var all = new VideoFaceShadowDbscan();
        var counterfactual = new VideoFaceCenterFaceShadow(1000, 4000, IndependentDescriptor());
        for (int frame = 1; frame <= 4; frame++)
        {
            var original = new[] { Detection(.10, .10), Detection(.65, .65) };
            var vectors = new[] { Vector(0), Vector(1) };
            var independent = new[] { Detection(.10, .10, .65) };
            counterfactual.ObserveFrame(frame, frame * 1000, Size,
                original, vectors, independent);
            for (int face = 0; face < original.Length; face++)
                all.Observe(frame, frame * 1000, face + 1,
                    vectors[face], original[face], Size);
        }

        var baseline = await all.EvaluateAsync(Matcher());
        var result = await counterfactual.EvaluateAsync(Matcher());

        Assert.Equal(8, baseline.ObservationsEvaluated);
        Assert.Equal(2, baseline.ClusterCount);
        Assert.Equal(8, baseline.ClusterMembers);
        Assert.Equal(8, result.RetainedYuNetObservations);
        Assert.Equal(4, result.CorroboratedAt030);
        Assert.Equal(4, result.CorroboratedAt050);
        Assert.Equal(4, result.NotCorroboratedAt030);
        Assert.Equal(4, result.NotCorroboratedAudit.TotalUnassignedObservations);
        Assert.Equal(4, result.NotCorroboratedAudit.SampledObservations);
        Assert.All(result.NotCorroboratedAudit.SampledCrops,
            crop => Assert.Equal(2, crop.FaceNumber));
        Assert.Equal(1, result.CorroboratedOnlyDbscan.ClusterCount);
        Assert.Equal(4, result.CorroboratedOnlyDbscan.ClusterMembers);
        Assert.Equal(0, result.CorroboratedOnlyDbscan.SameFrameCollisions);
        Assert.Equal("P0001", Assert.Single(
            result.CorroboratedOnlyDbscan.Groups).TopCandidates[0].Alias);
        Assert.Equal(4, result.FramesChecked);
        Assert.Equal(4, result.IndependentDetectionsAt030);
        Assert.Equal(0, result.IndependentOnlyAt030);
        Assert.Empty(result.IndependentOnlyAudit!.SampledCrops);
        Assert.True(result.AllCandidatesAccountedFor);

        string serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("Embedding", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PersonId", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FaceOccurrenceId", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AssetRevisionId", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Independent_misses_do_not_discard_genuine_singletons()
    {
        var result = new VideoFaceCenterFaceShadow(1000, 2000, IndependentDescriptor());
        result.ObserveFrame(1, 1000, Size,
            [Detection(.1, .1)],
            [Vector(0)],
            []);
        result.ObserveFrame(2, 2000, Size,
            [Detection(.1, .1)],
            [Vector(0)],
            [Detection(.1, .1, .40)]);
        var audit = await result.EvaluateAsync(Matcher());

        Assert.Equal(2, audit.RetainedYuNetObservations);
        Assert.Equal(1, audit.CorroboratedAt030);
        Assert.Equal(0, audit.CorroboratedAt050);
        Assert.Equal(1, audit.NotCorroboratedAt030);
        Assert.Single(audit.NotCorroboratedAudit.SampledCrops);
        Assert.Equal(1, audit.CorroboratedOnlyDbscan.NoiseObservations);
        Assert.True(audit.AllCandidatesAccountedFor);
    }

    [Fact]
    public async Task No_independent_matches_still_produces_accounted_reviewable_observations()
    {
        var result = new VideoFaceCenterFaceShadow(1000, 1000, IndependentDescriptor());
        result.ObserveFrame(1, 1000, Size,
            [Detection(.2, .2)], [Vector(0)], []);
        var review = await result.EvaluateAsync(Matcher());

        Assert.Equal(0, review.CorroboratedAt030);
        Assert.Equal(1, review.NotCorroboratedAt030);
        Assert.Equal(1, review.NotCorroboratedAudit.SampledObservations);
        Assert.Equal(0, review.CorroboratedOnlyDbscan.ObservationsEvaluated);
        Assert.True(review.AllCandidatesAccountedFor);
    }

    [Fact]
    public async Task Independent_only_face_candidates_are_preserved_without_claiming_identity()
    {
        var counterfactual = new VideoFaceCenterFaceShadow(
            1000, 1000, IndependentDescriptor());
        counterfactual.ObserveFrame(1, 1000, Size,
            [Detection(.10, .10)], [Vector(0)],
            [Detection(.10, .10, .8), Detection(.65, .65, .4)]);
        var result = await counterfactual.EvaluateAsync(Matcher());

        Assert.Equal(1, result.RetainedYuNetObservations);
        Assert.Equal(1, result.CorroboratedAt030);
        Assert.Equal(0, result.NotCorroboratedAt030);
        Assert.Equal(2, result.IndependentDetectionsAt030);
        Assert.Equal(1, result.IndependentOnlyAt030);
        var candidate = Assert.Single(result.IndependentOnlyAudit!.SampledCrops);
        Assert.Equal(2, candidate.FaceNumber);
        Assert.Equal(VideoFaceUnassignedAuditSampler.QualityExtremesPolicy,
            result.IndependentOnlyAudit.SelectionPolicy);
        Assert.Equal(1, result.CorroboratedOnlyDbscan.NoiseObservations);
        Assert.True(result.AllCandidatesAccountedFor);
        string json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("PersonId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Embedding", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Independent_only_audit_keeps_easy_and_hard_examples_in_bounded_sample()
    {
        var counterfactual = new VideoFaceCenterFaceShadow(
            1000, 2000, IndependentDescriptor());
        var candidates = Enumerable.Range(0, 20)
            .Select(i => Detection(.60, .60, i < 10 ? .9 : .31)).ToArray();
        // Source boxes are independent detections, not YuNet embeddings.
        // A single frame cannot consume the review stratum (two samples).
        counterfactual.ObserveFrame(1, 1000, Size, [], [],
            candidates.Take(20).ToArray());
        counterfactual.ObserveFrame(2, 2000, Size, [], [], []);
        var result = await counterfactual.EvaluateAsync(Matcher());
        Assert.Equal(20, result.IndependentOnlyAt030);
        Assert.Equal(20, result.IndependentDetectionsAt030);
        Assert.Equal(2, result.IndependentOnlyAudit!.SampledObservations);
        Assert.True(result.AllCandidatesAccountedFor);
    }

    [Fact]
    public void Refuses_wrong_frame_order_and_inconsistent_embedding_count()
    {
        var result = new VideoFaceCenterFaceShadow(1000, 2000, IndependentDescriptor());
        Assert.Throws<ArgumentException>(() =>
            result.ObserveFrame(2, 2000, Size, [], [], []));
        Assert.Throws<ArgumentException>(() =>
            result.ObserveFrame(1, 1000, Size,
                [Detection(.2, .2)], [], []));
        Assert.Throws<ArgumentException>(() => result.ObserveFrame(1, 1000, Size,
            [], [], Enumerable.Repeat(Detection(.2, .2), 33).ToArray()));
        result.ObserveFrame(1, 1000, Size, [], [], []);
        Assert.Throws<ArgumentException>(() =>
            result.ObserveFrame(1, 1000, Size, [], [], []));
    }
}
