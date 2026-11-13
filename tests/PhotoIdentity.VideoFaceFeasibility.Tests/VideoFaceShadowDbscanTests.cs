using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceShadowDbscanTests
{
    private static readonly ImageSize Size = new(1920, 1080);
    private static readonly NormalizedFaceLandmarks Landmarks = new(
        new NormalizedPoint(.25, .32),
        new NormalizedPoint(.35, .32),
        new NormalizedPoint(.30, .35),
        new NormalizedPoint(.27, .39),
        new NormalizedPoint(.33, .39));

    private static DetectedFaceCandidate Face(double confidence = .9) => new(
        new NormalizedBoundingBox(.2, .2, .18, .25), Landmarks, confidence);

    private static EmbeddingVector Vector(int i, int dimensions = 128)
    {
        float[] elements = new float[dimensions];
        elements[i] = 1f;
        return new EmbeddingVector(elements);
    }

    private static VideoFaceCataloguePersonMatcher Matcher() => new(
    [
        new("P0001", new string('a', 64), Vector(0)),
        new("P0002", new string('b', 64), Vector(1)),
        new("P0003", new string('c', 64), Vector(2)),
    ]);

    [Fact]
    public async Task Replays_M25_DBSCAN_without_merging_simultaneous_faces_or_dropping_noise()
    {
        VideoFaceShadowDbscan shadow = new();
        // Even identical vectors cannot merge these two simultaneous people.
        shadow.Observe(1, 1000, 1, Vector(0), Face(), Size);
        shadow.Observe(1, 1000, 2, Vector(0), Face(), Size);
        shadow.Observe(5, 5000, 1, Vector(0), Face(), Size);
        shadow.Observe(5, 5000, 2, Vector(0), Face(), Size);
        // A clearly different, sole frame is valid evidence even though it
        // cannot form a two-member density cluster.
        shadow.Observe(9, 9000, 1, Vector(1), Face(.6), Size);

        VideoFaceShadowDbscanReview report = await shadow.EvaluateAsync(Matcher());
        Assert.Equal(5, report.ObservationsEvaluated);
        Assert.Equal(2, report.ClusterCount);
        Assert.Equal(4, report.ClusterMembers);
        Assert.Equal(1, report.NoiseObservations);
        Assert.Single(report.NoiseAudit!.SampledCrops);
        Assert.Equal(1, report.NoiseAudit.TotalUnassignedObservations);
        Assert.Equal(9, report.NoiseAudit.SampledCrops[0].FrameNumber);
        Assert.Equal(2, report.NotSameFrameConstraints);
        Assert.Equal(0, report.SameFrameCollisions);
        Assert.True(report.AllObservationsAccountedFor);
        Assert.Equal(2, report.Groups.Count);
        Assert.All(report.Groups, group =>
        {
            Assert.Equal(2, group.Observations);
            Assert.Equal(2, group.RepresentativeCrops.Count);
            Assert.Equal("P0001", group.TopCandidates[0].Alias);
        });

        string json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("Embedding", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Vector", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PersonId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FaceOccurrenceId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AssetRevisionId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Many_valid_clusters_are_bounded_for_review_but_all_members_are_counted()
    {
        VideoFaceShadowDbscan shadow = new();
        const int clusters = 65;
        for (int i = 0; i < clusters; i++)
            shadow.Observe(i + 1, (i + 1) * 1000L, 1, Vector(i), Face(), Size);

        for (int i = 0; i < clusters; i++)
            shadow.Observe(i + clusters + 1, (i + clusters + 1) * 1000L, 1,
                Vector(i), Face(), Size);

        VideoFaceShadowDbscanReview report = await shadow.EvaluateAsync(Matcher());
        Assert.Equal(130, report.ObservationsEvaluated);
        Assert.Equal(65, report.ClusterCount);
        Assert.Equal(130, report.ClusterMembers);
        Assert.Equal(0, report.NoiseObservations);
        Assert.Equal(64, report.GroupsRepresentedForReview);
        Assert.Equal(1, report.GroupsNotRepresentedForReview);
        Assert.Equal(2, report.MembersInUnrepresentedGroups);
        Assert.Equal(0, report.SameFrameCollisions);
        Assert.True(report.AllObservationsAccountedFor);
        Assert.All(report.Groups, group =>
        {
            Assert.InRange(group.RepresentativeCrops.Count, 1, 5);
            Assert.Equal(2, group.Observations);
        });
        Assert.Equal(64, report.Groups.Select(g => g.Alias).Distinct().Count());
        Assert.All(report.Groups, g => Assert.StartsWith("D", g.Alias));
    }

    [Fact]
    public async Task Singleton_faces_remain_explicit_noise_instead_of_being_deleted()
    {
        var shadow = new VideoFaceShadowDbscan();
        shadow.Observe(1, 1000, 1, Vector(0), Face(), Size);
        shadow.Observe(2, 2000, 1, Vector(1), Face(), Size);
        shadow.Observe(3, 3000, 1, Vector(2), Face(), Size);

        VideoFaceShadowDbscanReview report = await shadow.EvaluateAsync(Matcher());
        Assert.Equal(3, report.NoiseObservations);
        Assert.Equal(3, report.NoiseAudit!.SampledObservations);
        Assert.Empty(report.Groups);
        Assert.Equal(0, report.ClusterCount);
        Assert.Equal(0, report.ClusterMembers);
        Assert.True(report.AllObservationsAccountedFor);
    }

    [Fact]
    public async Task Empty_shadow_experiment_is_valid_and_has_no_clustering_side_effect()
    {
        var report = await new VideoFaceShadowDbscan().EvaluateAsync(Matcher());
        Assert.Equal(0, report.ObservationsEvaluated);
        Assert.Equal(0, report.NoiseObservations);
        Assert.Empty(report.Groups);
        Assert.Null(report.NoiseAudit);
        Assert.True(report.AllObservationsAccountedFor);
    }

    [Fact]
    public async Task Different_input_order_is_rejected_and_no_candidate_is_silently_discarded()
    {
        var shadow = new VideoFaceShadowDbscan();
        shadow.Observe(1, 1000, 1, Vector(0), Face(), Size);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            shadow.Observe(1, 1000, 1, Vector(1), Face(), Size));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            shadow.Observe(181, 181000, 1, Vector(1), Face(), Size));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            shadow.Observe(1, 1000, 33, Vector(1), Face(), Size));
        Assert.Equal(1, shadow.Count);
        VideoFaceShadowDbscanReview report = await shadow.EvaluateAsync(Matcher());
        Assert.Equal(1, report.NoiseObservations);
        Assert.Equal(1, report.ObservationsEvaluated);
    }

    [Fact]
    public async Task Identical_synthetic_input_yields_deterministic_review_aliases_and_members()
    {
        static VideoFaceShadowDbscan New()
        {
            var result = new VideoFaceShadowDbscan();
            result.Observe(1, 1000, 1, Vector(0), Face(.6), Size);
            result.Observe(2, 2000, 1, Vector(1), Face(.9), Size);
            result.Observe(4, 4000, 1, Vector(0), Face(.95), Size);
            result.Observe(5, 5000, 1, Vector(1), Face(.8), Size);
            return result;
        }
        VideoFaceShadowDbscanReview first = await New().EvaluateAsync(Matcher());
        VideoFaceShadowDbscanReview second = await New().EvaluateAsync(Matcher());
        Assert.Equal(first.ClusterCount, second.ClusterCount);
        Assert.Equal(first.NoiseObservations, second.NoiseObservations);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }
}
