using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoSearchRankerTests
{
    private static readonly string Hash = new('a', 64);
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static VideoSearchCandidate Clip(Guid id, params VideoSearchFrame[] frames) =>
        new(id, "interval-v2-n2", "clip-model", Hash, "image-v1",
            "caption-model", "prompt-v1", frames);

    private static VideoSearchFrame Frame(long ms, string caption, params float[] vector) =>
        new(ms, caption, vector);

    [Fact]
    public void One_video_gets_only_one_fusion_vote_per_evidence_stream()
    {
        var videos = new[]
        {
            Clip(First,
                Frame(1000, "Dog on the beach", 1, 0),
                Frame(2000, "Dog jumping at the beach", 1, 0)),
            Clip(Second, Frame(1000, "A car", 0, 1))
        };
        var found = VideoSearchRanker.Rank(videos, "dog beach", "combined", 10,
            [1, 0], "clip-model", Hash, "image-v1");
        Assert.Equal(2, found.Count);
        Assert.Equal(First, found[0].RevisionId);
        Assert.Equal(new[] { "semantic", "caption" }, found[0].Sources);
        Assert.Equal(1000L, found[0].TimestampMilliseconds);
        Assert.Equal("interval-v2-n2", found[0].SamplingPolicyVersion);
        Assert.Equal("caption-model", found[0].CaptionModelId);
        Assert.Equal(First, Assert.Single(found.Where(x => x.RevisionId == First)).RevisionId);
    }

    [Fact]
    public void Caption_mode_works_without_any_model_and_does_not_return_other_videos()
    {
        var found = VideoSearchRanker.Rank([
            Clip(First, Frame(0, "Swedish summer day", 1, 0)),
            Clip(Second, Frame(0, "Winter snow", 0, 1))
        ], "summer", "caption", 10);
        VideoSearchHit result = Assert.Single(found);
        Assert.Equal(First, result.RevisionId);
        Assert.Equal(new[] { "caption" }, result.Sources);
        Assert.Null(result.SemanticScore);
    }

    [Fact]
    public void Visual_search_requires_exact_matching_model_provenance()
    {
        var clips = new[] { Clip(First, Frame(0, "Unrelated", 1, 0)) };
        Assert.Empty(VideoSearchRanker.Rank(clips, "beach", "semantic", 10,
            [1, 0], "clip-model", new string('b', 64), "image-v1"));
        VideoSearchHit hit = Assert.Single(VideoSearchRanker.Rank(clips, "beach", "semantic",
            10, [1, 0], "clip-model", Hash, "image-v1"));
        Assert.Equal(new[] { "semantic" }, hit.Sources);
        Assert.Equal(0L, hit.TimestampMilliseconds);
    }

    [Fact]
    public void Combined_search_keeps_caption_fallback_when_clip_model_is_missing()
    {
        VideoSearchHit hit = Assert.Single(VideoSearchRanker.Rank([
            Clip(First, Frame(1000, "People walking along a road", 0, 1))
        ], "walking", "combined", 5));
        Assert.Null(hit.SemanticScore);
        Assert.NotNull(hit.CaptionScore);
        Assert.Equal(new[] { "caption" }, hit.Sources);
    }

    [Fact]
    public void Duplicate_revisions_and_unordered_timestamps_fail_closed()
    {
        var valid = Clip(First, Frame(100, "A dog", 1, 0));
        Assert.Throws<ArgumentException>(() => VideoSearchRanker.Rank(
            [valid, valid], "dog", "caption", 10));
        Assert.Throws<ArgumentException>(() => VideoSearchRanker.Rank(
            [Clip(Second, Frame(200, "dog", 1, 0),
                Frame(200, "dog", 1, 0))], "dog", "caption", 10));
    }
}
