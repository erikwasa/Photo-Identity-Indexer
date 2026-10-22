using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class PhotoSimilarityBurstReviewGrouperTests
{
    [Fact]
    public void October_example_five_visual_groups_across_five_minutes_become_one_review_set()
    {
        DateTime origin = new(2024, 10, 5, 15, 57, 0);
        // Pairs are exact duplicates (except the four-photo H4 group),
        // but base hashes across groups are very different.
        PhotoVisualFingerprint[] photos =
        [
            Photo(1, origin, 0, 0),
            Photo(2, origin, 4, 0),
            Photo(3, origin, 60, ulong.MaxValue),
            Photo(4, origin, 64, ulong.MaxValue),
            Photo(5, origin, 95, 0xaaaaaaaaaaaaaaaa),
            Photo(6, origin, 99, 0xaaaaaaaaaaaaaaaa),
            Photo(7, origin, 245, 0x5555555555555555),
            Photo(8, origin, 248, 0x5555555555555555),
            Photo(9, origin, 251, 0x5555555555555555),
            Photo(10, origin, 254, 0x5555555555555551),
            Photo(11, origin, 300, 0xf0f0f0f0f0f0f0f0),
            Photo(12, origin, 304, 0xf0f0f0f0f0f0f0f0),
        ];

        PhotoVisualRedundancyResult detected = Detect(photos);
        Assert.Equal([2, 2, 2, 4, 2], detected.Groups.Select(g => g.Members.Count).ToArray());

        PhotoVisualRedundancyResult review = PhotoSimilarityBurstReviewGrouper.Combine(detected, photos);
        PhotoVisualRedundancyGroup burst = Assert.Single(review.Groups);
        Assert.Equal(12, burst.Members.Count);
        Assert.Equal(5, burst.VisualSubgroupCount);
        Assert.Equal(origin, burst.StartedAtLocal);
        Assert.Equal(origin.AddSeconds(304), burst.EndedAtLocal);
        Assert.Equal(64, burst.MaximumPairDistance);
        Assert.Equal(12, burst.Members.Select(m => m.RevisionId).Distinct().Count());
        Assert.Equal(PhotoSimilarityBurstReviewGrouper.PolicyVersion, review.PolicyVersion);
    }

    [Fact]
    public void December_example_three_groups_in_same_minute_become_one_review_set()
    {
        DateTime origin = new(2024, 12, 31, 17, 24, 0);
        ulong alt = 0xaaaaaaaaaaaaaaaa;
        PhotoVisualFingerprint[] photos =
        [
            Photo(1, origin, 0, 0),
            Photo(2, origin, 1, 0x1ff), // H9
            Photo(3, origin, 8, ulong.MaxValue),
            Photo(4, origin, 9, ulong.MaxValue ^ 0xfff), // H12
            Photo(5, origin, 16, alt),
            Photo(6, origin, 17, alt ^ 0x7f),
            Photo(7, origin, 18, alt ^ 0x7fff), // H15 from first; H8 from second
        ];

        PhotoVisualRedundancyResult detected = Detect(photos);
        Assert.Equal([2, 2, 3], detected.Groups.Select(g => g.Members.Count).ToArray());
        Assert.Equal([9, 12, 15], detected.Groups.Select(g => g.MaximumPairDistance).ToArray());

        PhotoVisualRedundancyGroup review = Assert.Single(
            PhotoSimilarityBurstReviewGrouper.Combine(detected, photos).Groups);
        Assert.Equal(7, review.Members.Count);
        Assert.Equal(3, review.VisualSubgroupCount);
    }

    [Fact]
    public void Gaps_longer_than_four_minutes_are_not_combined_even_in_one_moment()
    {
        DateTime start = new(2024, 1, 1, 10, 0, 0);
        PhotoVisualFingerprint[] photos =
        [
            Photo(1, start, 0, 0),
            Photo(2, start, 1, 0),
            Photo(3, start, 301, ulong.MaxValue),
            Photo(4, start, 302, ulong.MaxValue),
        ];
        PhotoVisualRedundancyResult review = Combine(photos);
        Assert.Equal(2, review.Groups.Count);
        Assert.All(review.Groups, group => Assert.Equal(2, group.Members.Count));
    }

    [Fact]
    public void Total_review_span_is_bounded_to_ten_minutes_even_with_short_gaps()
    {
        DateTime start = new(2024, 1, 1, 10, 0, 0);
        PhotoVisualFingerprint[] photos =
        [
            Photo(1, start, 0, 0),
            Photo(2, start, 1, 0),
            Photo(3, start, 210, ulong.MaxValue),
            Photo(4, start, 211, ulong.MaxValue),
            Photo(5, start, 420, 0xaaaaaaaaaaaaaaaa),
            Photo(6, start, 421, 0xaaaaaaaaaaaaaaaa),
            Photo(7, start, 630, 0x5555555555555555),
            Photo(8, start, 631, 0x5555555555555555),
        ];
        PhotoVisualRedundancyResult review = Combine(photos);
        Assert.Equal([6, 2], review.Groups.Select(g => g.Members.Count).ToArray());
    }

    [Fact]
    public void Review_sets_cannot_cross_derived_moment_boundary()
    {
        DateTime start = new(2024, 1, 1, 10, 0, 0);
        PhotoVisualFingerprint[] photos =
        [
            Photo(1, start, 0, 0),
            Photo(2, start, 1, 0),
            Photo(3, start, 121, ulong.MaxValue),
            Photo(4, start, 122, ulong.MaxValue),
        ];
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            photos.Select(p => new PhotoMomentCandidate(p.RevisionId, p.TakenAtLocal)),
            PhotoMomentGapPolicy.CreateTimeGapEvaluation(1));
        PhotoVisualRedundancyResult detected = PhotoVisualRedundancyGrouper.Group(
            photos, moments, PhotoVisualRedundancyPolicy.OperatorSimilarityReviewV2);
        PhotoVisualRedundancyResult review = PhotoSimilarityBurstReviewGrouper.Combine(detected, photos);
        Assert.Equal(2, review.Groups.Count);
        Assert.All(review.Groups, group => Assert.Equal(1, group.VisualSubgroupCount));
    }

    [Fact]
    public void Review_sets_are_deterministic_and_keep_exact_revision_membership()
    {
        DateTime start = new(2024, 1, 1, 10, 0, 0);
        PhotoVisualFingerprint[] photos =
        [
            Photo(1, start, 0, 0),
            Photo(2, start, 2, 0),
            Photo(3, start, 40, ulong.MaxValue),
            Photo(4, start, 42, ulong.MaxValue),
        ];
        PhotoVisualRedundancyResult baseline = Combine(photos);
        PhotoVisualRedundancyResult reordered = Combine(photos.Reverse().ToArray());
        Assert.Equal(baseline.Groups.Select(g => (g.Id, g.PolicyVersion,
            Members: string.Join(",", g.Members.Select(m => m.RevisionId)))),
            reordered.Groups.Select(g => (g.Id, g.PolicyVersion,
                Members: string.Join(",", g.Members.Select(m => m.RevisionId)))));
        Assert.Equal(4, Assert.Single(reordered.Groups).Members.Count);
    }

    [Fact]
    public void Caps_review_sets_at_forty_photos_without_losing_any_groups()
    {
        DateTime origin = new(2024, 1, 1, 10, 0, 0);
        // The coalescer takes known detected groups, so it does not need
        // to redetect these synthetic two-photo groups.
        PhotoVisualRedundancyGroup[] pairs = Enumerable.Range(0, 21).Select(i =>
            new PhotoVisualRedundancyGroup(
                $"pair-{i}", "moment-0001", "underlying-visual-policy",
                Id(i * 2 + 1), origin.AddSeconds(i * 4), origin.AddSeconds(i * 4 + 1),
                0,
                [
                    new PhotoVisualRedundancyMember(Id(i * 2 + 1), origin.AddSeconds(i * 4), 0),
                    new PhotoVisualRedundancyMember(Id(i * 2 + 2), origin.AddSeconds(i * 4 + 1), 0),
                ])).ToArray();
        PhotoVisualFingerprint[] photos = Enumerable.Range(0, 42)
            .Select(i => new PhotoVisualFingerprint(Id(i + 1), origin.AddSeconds((i / 2) * 4 + i % 2),
                new PhotoPerceptualHash64(0))).ToArray();
        PhotoVisualRedundancyResult detected = new(
            "underlying-visual-policy", PhotoVisualRedundancyPolicy.AlgorithmVersion,
            12, TimeSpan.FromSeconds(45), 42, pairs);
        PhotoVisualRedundancyResult review = PhotoSimilarityBurstReviewGrouper.Combine(detected, photos);
        Assert.Equal([40, 2], review.Groups.Select(g => g.Members.Count).ToArray());
        Assert.Equal(42, review.Groups.SelectMany(g => g.Members).Select(m => m.RevisionId).Distinct().Count());
    }

    [Fact]
    public void A_single_oversized_visual_group_is_split_into_bounded_review_sets()
    {
        DateTime start = new(2024, 1, 1, 10, 0, 0);
        PhotoVisualFingerprint[] photos = Enumerable.Range(0, 42)
            .Select(i => Photo(i + 1, start, i, 0)).ToArray();
        Assert.Single(Detect(photos).Groups);
        PhotoVisualRedundancyResult result = Combine(photos);
        Assert.Equal([40, 2], result.Groups.Select(g => g.Members.Count).ToArray());
        Assert.Equal(42, result.Groups.SelectMany(g => g.Members).Select(m => m.RevisionId).Distinct().Count());
    }

    private static PhotoVisualRedundancyResult Combine(PhotoVisualFingerprint[] photos) =>
        PhotoSimilarityBurstReviewGrouper.Combine(Detect(photos), photos);

    private static PhotoVisualRedundancyResult Detect(PhotoVisualFingerprint[] photos)
    {
        PhotoMomentClusteringResult moments = PhotoMomentClusterer.Cluster(
            photos.Select(p => new PhotoMomentCandidate(p.RevisionId, p.TakenAtLocal)),
            PhotoMomentGapPolicy.Evaluation30Minutes);
        return PhotoVisualRedundancyGrouper.Group(
            photos, moments, PhotoVisualRedundancyPolicy.OperatorSimilarityReviewV2);
    }

    private static PhotoVisualFingerprint Photo(int id, DateTime start, int seconds, ulong hash) =>
        new(Id(id), start.AddSeconds(seconds), new PhotoPerceptualHash64(hash));

    private static AssetRevisionId Id(int number) => AssetRevisionId.From(
        Guid.Parse($"00000000-0000-0000-0000-{number:D12}"));
}
