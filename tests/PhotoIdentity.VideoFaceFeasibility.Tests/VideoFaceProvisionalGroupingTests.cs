using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.VideoFaceFeasibility;
using Xunit;

namespace PhotoIdentity.VideoFaceFeasibility.Tests;

public sealed class VideoFaceProvisionalGroupingTests
{
    private static readonly ImageSize Native = new(1920, 1080);
    private static readonly NormalizedFaceLandmarks Landmarks = new(
        new NormalizedPoint(.22, .32),
        new NormalizedPoint(.28, .32),
        new NormalizedPoint(.25, .36),
        new NormalizedPoint(.22, .40),
        new NormalizedPoint(.28, .40));

    private static DetectedFaceCandidate Face(double confidence = .9,
        double width = .15) => new(
            new NormalizedBoundingBox(.2, .2, width, .2),
            Landmarks, confidence);

    private static EmbeddingVector Vector(int index, int dimensions = 128)
    {
        float[] values = new float[dimensions];
        values[index] = 1f;
        return new EmbeddingVector(values);
    }

    [Fact]
    public void Repeated_face_is_one_provisional_group_with_diverse_best_five_crops()
    {
        var grouper = new VideoFaceProvisionalGrouper();
        EmbeddingVector a = Vector(0);
        grouper.Observe(1, 1000, 1, a, Face(.52), Native);
        grouper.Observe(2, 2000, 1, a, Face(.98), Native);
        grouper.Observe(6, 6000, 1, a, Face(.9), Native);
        grouper.Observe(11, 11000, 1, a, Face(.95), Native);
        grouper.Observe(17, 17000, 1, a, Face(.99), Native);
        grouper.Observe(23, 23000, 1, a, Face(.85), Native);
        grouper.Observe(29, 29000, 1, a, Face(.96), Native);

        VideoFaceProvisionalGroupReview review = grouper.CreateReview();
        Assert.Equal(7, review.ObservationsConsidered);
        Assert.Equal(7, review.ObservationsAssigned);
        Assert.Equal(0, review.ObservationsUnassigned);
        VideoFaceProvisionalGroup group = Assert.Single(review.Groups);
        Assert.Equal("G001", group.Alias);
        Assert.Equal(7, group.Observations);
        Assert.Equal(5, group.RepresentativeCrops.Count);
        Assert.DoesNotContain(group.RepresentativeCrops, c => c.FrameNumber is 1 or 23);
        Assert.Equal(new[] { 17, 2, 29, 11, 6 },
            group.RepresentativeCrops.Select(c => c.FrameNumber).ToArray());
        Assert.All(group.RepresentativeCrops, crop =>
        {
            Assert.InRange(crop.X, 0, 1);
            Assert.InRange(crop.DetectorConfidence, 0.5, 1);
        });
        // The report serializes only source-frame coordinates, NOT vectors.
        string json = JsonSerializer.Serialize(review);
        Assert.DoesNotContain("Embedding", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Vector", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Disparate_face_embeddings_do_not_merge_based_on_catalogue_alias()
    {
        var grouper = new VideoFaceProvisionalGrouper();
        grouper.Observe(1, 1000, 1, Vector(0), Face(), Native);
        grouper.Observe(2, 2000, 1, Vector(1), Face(), Native);
        grouper.Observe(3, 3000, 1, Vector(0), Face(), Native);

        var review = grouper.CreateReview();
        Assert.Equal(2, review.Groups.Count);
        Assert.Equal(2, review.Groups[0].Observations);
        Assert.Equal(1, review.Groups[1].Observations);
    }

    [Fact]
    public void Two_faces_in_same_frame_never_assigned_to_one_group()
    {
        var grouper = new VideoFaceProvisionalGrouper();
        EmbeddingVector a = Vector(0);
        grouper.Observe(1, 1000, 1, a, Face(), Native);
        grouper.Observe(1, 1000, 2, a, Face(), Native);

        var result = grouper.CreateReview();
        Assert.Equal(2, result.Groups.Count);
        Assert.All(result.Groups, group => Assert.Equal(1, group.Observations));
    }

    [Fact]
    public void Group_limit_is_counted_and_unassigned_is_not_silently_lost()
    {
        var grouper = new VideoFaceProvisionalGrouper();
        for (int i = 0; i < VideoFaceProvisionalGrouper.MaximumGroups + 1; i++)
        {
            grouper.Observe(i + 1, 1000 + i * 1000L, 1,
                Vector(i), Face(), Native);
        }
        var result = grouper.CreateReview();
        Assert.Equal(VideoFaceProvisionalGrouper.MaximumGroups, result.Groups.Count);
        Assert.Equal(VideoFaceProvisionalGrouper.MaximumGroups + 1,
            result.ObservationsConsidered);
        Assert.Equal(VideoFaceProvisionalGrouper.MaximumGroups,
            result.ObservationsAssigned);
        Assert.Equal(1, result.ObservationsUnassigned);
    }

    [Fact]
    public void Provisional_groups_get_three_private_person_candidates_without_vectors()
    {
        var grouper = new VideoFaceProvisionalGrouper();
        grouper.Observe(1, 1000, 1, Vector(0), Face(), Native);
        grouper.Observe(5, 5000, 1, Vector(0), Face(), Native);
        grouper.Observe(7, 7000, 1, Vector(1), Face(), Native);
        var matcher = new VideoFaceCataloguePersonMatcher(
        [
            new("P01", new string('a', 64), Vector(0)),
            new("P02", new string('b', 64), Vector(1)),
            new("P03", new string('c', 64), Vector(2)),
        ]);

        var suggestions = grouper.RankGroups(matcher);
        Assert.Equal(2, suggestions.Count);
        Assert.Equal("G001", suggestions[0].GroupAlias);
        Assert.Equal(2, suggestions[0].GroupObservations);
        Assert.Equal(2, suggestions[0].RepresentativeCount);
        Assert.Equal("P01", suggestions[0].TopCandidates[0].Alias);
        Assert.Equal("G002", suggestions[1].GroupAlias);
        Assert.Equal(1, suggestions[1].RepresentativeCount);
        Assert.Equal("P02", suggestions[1].TopCandidates[0].Alias);

        string json = JsonSerializer.Serialize(suggestions);
        Assert.DoesNotContain("Embedding", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Vector", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PersonId", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Near_duplicate_with_lower_quality_does_not_replace_evidence()
    {
        var grouper = new VideoFaceProvisionalGrouper();
        var a = Vector(0);
        grouper.Observe(1, 1000, 1, a, Face(.97), Native);
        grouper.Observe(2, 2000, 1, a, Face(.7), Native);
        Assert.Equal(2, grouper.CreateReview().Groups[0].Observations);
        var crop = Assert.Single(grouper.CreateReview().Groups[0].RepresentativeCrops);
        Assert.Equal(1, crop.FrameNumber);
    }
}
