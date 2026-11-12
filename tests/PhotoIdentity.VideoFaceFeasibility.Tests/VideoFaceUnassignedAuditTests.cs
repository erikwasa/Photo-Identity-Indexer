using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceUnassignedAuditTests
{
    private static readonly ImageSize Native = new(1920, 1080);

    private static readonly NormalizedFaceLandmarks Landmarks = new(
        new NormalizedPoint(.22, .32),
        new NormalizedPoint(.28, .32),
        new NormalizedPoint(.25, .36),
        new NormalizedPoint(.22, .40),
        new NormalizedPoint(.28, .40));

    private static DetectedFaceCandidate Face(double confidence = .9) => new(
        new NormalizedBoundingBox(.2, .2, .12, .2), Landmarks, confidence);

    private static EmbeddingVector Vector(int index)
    {
        float[] values = new float[128];
        values[index] = 1;
        return new EmbeddingVector(values);
    }

    private static VideoFaceGroupCrop Crop(
        int frame, long timestamp, int face, double quality = .8) =>
        new(frame, timestamp, face, .2, .2, .1, .2, .9, quality);

    [Fact]
    public void Temporal_strata_bound_many_unassigned_candidates_without_claiming_group_membership()
    {
        var sampler = new VideoFaceUnassignedAuditSampler(0, 600_000);
        const int total = 3_505;
        for (int i = 0; i < total; i++)
        {
            long time = 600_000L * i / (total - 1);
            sampler.Observe(Crop(i / 3 + 1, time, i % 3 + 1, .2 + (i % 7) * .1));
        }

        var result = sampler.CreateReview(total);
        Assert.Equal(total, result.TotalUnassignedObservations);
        Assert.Equal(96, result.SampledObservations);
        Assert.Equal(3_409, result.UnrepresentedObservations);
        Assert.Equal(12, result.StrataRepresented);
        Assert.Equal(96, result.MaximumReviewSamples);
        Assert.All(result.SampledCrops.GroupBy(c => c.FrameNumber),
            group => Assert.InRange(group.Count(), 1, 2));
        Assert.Equal(result.SampledCrops.Count, result.SampledCrops
            .Select(c => (c.FrameNumber, c.FaceNumber)).Distinct().Count());
        Assert.True(result.SampledCrops.SequenceEqual(
            result.SampledCrops.OrderBy(c => c.TimestampMs)
                .ThenBy(c => c.FrameNumber).ThenBy(c => c.FaceNumber)));
    }

    [Fact]
    public void Highest_quality_candidate_can_replace_only_within_its_temporal_stratum()
    {
        var sampler = new VideoFaceUnassignedAuditSampler(0, 119_999);
        for (int i = 0; i < 8; i++)
            sampler.Observe(Crop(i + 1, 1000 + i, 1, .3));

        sampler.Observe(Crop(10, 1008, 1, .95));
        sampler.Observe(Crop(11, 1009, 1, .1));
        sampler.Observe(Crop(12, 80_000, 1, .4));
        var result = sampler.CreateReview(11);

        Assert.Equal(9, result.SampledObservations);
        Assert.Equal(2, result.StrataRepresented);
        Assert.Contains(result.SampledCrops, c => c.FrameNumber == 10);
        Assert.DoesNotContain(result.SampledCrops, c => c.FrameNumber == 11);
        Assert.Contains(result.SampledCrops, c => c.FrameNumber == 12);
    }

    [Fact]
    public void Saturated_group_cap_leaves_group_counts_and_assignments_unchanged()
    {
        var legacy = new VideoFaceProvisionalGrouper();
        var audited = new VideoFaceProvisionalGrouper(
            captureUnassignedAudit: true, firstTimestampMs: 0, lastTimestampMs: 600_000);

        for (int i = 0; i < VideoFaceProvisionalGrouper.MaximumGroups; i++)
        {
            int frame = i + 1;
            long time = i * 1000L;
            var vector = Vector(i);
            legacy.Observe(frame, time, 1, vector, Face(), Native);
            audited.Observe(frame, time, 1, vector, Face(), Native);
        }

        for (int i = 0; i < 100; i++)
        {
            int frame = 65 + i;
            long time = 65_000 + i * 5_000L;
            var vector = Vector(127);
            legacy.Observe(frame, time, 1, vector, Face(.6 + (i % 4) * .1), Native);
            audited.Observe(frame, time, 1, vector, Face(.6 + (i % 4) * .1), Native);
        }

        var original = legacy.CreateReview();
        var reviewed = audited.CreateReview();
        Assert.Equal(164, original.ObservationsConsidered);
        Assert.Equal(64, original.ObservationsAssigned);
        Assert.Equal(100, original.ObservationsUnassigned);
        Assert.Equal(original.ObservationsAssigned, reviewed.ObservationsAssigned);
        Assert.Equal(original.ObservationsUnassigned, reviewed.ObservationsUnassigned);
        Assert.Equal(JsonSerializer.Serialize(original.Groups),
            JsonSerializer.Serialize(reviewed.Groups));
        Assert.Null(original.UnassignedAudit);
        Assert.NotNull(reviewed.UnassignedAudit);
        Assert.Equal(100, reviewed.UnassignedAudit.TotalUnassignedObservations);
        Assert.InRange(reviewed.UnassignedAudit.SampledObservations, 1, 96);
        Assert.All(reviewed.UnassignedAudit.SampledCrops,
            crop => Assert.InRange(crop.FrameNumber, 65, 164));
        Assert.DoesNotContain("UnassignedAudit", JsonSerializer.Serialize(original));
        Assert.DoesNotContain("Embedding", JsonSerializer.Serialize(reviewed));
        Assert.DoesNotContain("Vector", JsonSerializer.Serialize(reviewed));
        Assert.DoesNotContain("PersonId", JsonSerializer.Serialize(reviewed));
    }

    [Fact]
    public void Existing_groups_without_overflow_produce_empty_audit_if_opted_in()
    {
        var grouper = new VideoFaceProvisionalGrouper(
            captureUnassignedAudit: true, firstTimestampMs: 0, lastTimestampMs: 10_000);
        grouper.Observe(1, 1000, 1, Vector(0), Face(), Native);
        var review = grouper.CreateReview();
        Assert.Equal(1, review.ObservationsAssigned);
        Assert.Equal(0, review.ObservationsUnassigned);
        Assert.NotNull(review.UnassignedAudit);
        Assert.Empty(review.UnassignedAudit.SampledCrops);
        Assert.Equal(0, review.UnassignedAudit.StrataRepresented);
    }

    [Fact]
    public void Invalid_and_outside_window_crop_is_rejected()
    {
        var sampler = new VideoFaceUnassignedAuditSampler(500, 1500);
        Assert.Throws<ArgumentException>(() => sampler.Observe(Crop(1, 499, 1)));
        Assert.Throws<ArgumentException>(() => sampler.Observe(Crop(1, 1501, 1)));
        Assert.Throws<ArgumentException>(() => sampler.Observe(Crop(1, 1000, 1, double.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VideoFaceUnassignedAuditSampler(2000, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VideoFaceUnassignedAuditSampler(0, 600_001));
    }

    [Fact]
    public async Task Unassigned_audit_requires_explicit_provisional_grouping()
    {
        int exit = await VideoFaceCatalogueRunner.RunAsync(
        [
            "--unassigned-audit",
            "--catalogue-cases", "C:\\private\\cases.json",
            "--private-report", "C:\\private\\report.json",
        ]);
        Assert.NotEqual(0, exit);
    }
}
