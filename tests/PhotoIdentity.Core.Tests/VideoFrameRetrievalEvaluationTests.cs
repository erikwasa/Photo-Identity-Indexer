using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoFrameRetrievalEvaluationTests
{
    private static readonly Guid First = Guid.Parse(
        "00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse(
        "00000000-0000-0000-0000-000000000002");
    private static readonly Guid Query = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");

    private static VideoFrameRetrievalEvaluationManifest Sample() =>
        new(VideoFrameRetrievalEvaluation.ManifestProtocol, new string('a', 64),
            "clip-image-preprocessing-v1", "clip-text-preprocessing-v1",
        [
            new(First, [[1f, 0f], [-1f, 0f]]),
            new(Second, [[0.8f, 0.6f], [0.8f, 0.6f]])
        ],
        [
            new(Query, [1f, 0f], [Second])
        ]);

    [Fact]
    public void Offline_judgments_expose_real_ranking_tradeoffs_without_any_ids()
    {
        VideoFrameRetrievalEvaluationResult top1 =
            VideoFrameRetrievalEvaluation.Evaluate(Sample(), 2, 1);
        Assert.Equal(2, top1.Clips);
        Assert.Equal(1, top1.Queries);
        Assert.Equal(1, top1.RelevantJudgments);
        Assert.Equal(4, top1.FrameVectors);
        Assert.Equal(2, top1.EmbeddingDimensions);
        Assert.Equal(1, top1.RankingK);
        Assert.Equal(0, top1.Maximum.HitRateAtK);
        Assert.Equal(0, top1.Maximum.MeanReciprocalRankAtK);
        Assert.Equal(0, top1.Maximum.MeanRecallAtK);
        Assert.Equal(1, top1.TopKMean.HitRateAtK);
        Assert.Equal(1, top1.TopKMean.MeanReciprocalRankAtK);
        Assert.Equal(1, top1.DeduplicatedPooled.MeanNdcgAtK);
        Assert.Equal(1, top1.DeduplicatedPooled.MeanAveragePrecisionAtK);

        VideoFrameRetrievalEvaluationResult top2 =
            VideoFrameRetrievalEvaluation.Evaluate(Sample(), 2, 2);
        Assert.Equal(1, top2.Maximum.HitRateAtK);
        Assert.Equal(0.5, top2.Maximum.MeanReciprocalRankAtK, 6);
        Assert.Equal(1, top2.Maximum.MeanRecallAtK);
        Assert.Equal(0.5, top2.Maximum.MeanAveragePrecisionAtK, 6);
        Assert.InRange(top2.Maximum.MeanNdcgAtK, 0.63092, 0.63094);
        Assert.Equal(1, top2.TopKMean.MeanReciprocalRankAtK);
    }

    [Fact]
    public void Evaluation_is_independent_of_corpus_order_and_ties_are_stable()
    {
        var original = Sample();
        var reversed = original with { Clips = original.Clips.Reverse().ToArray() };
        Assert.Equal(
            VideoFrameRetrievalEvaluation.Evaluate(original, 2, 1),
            VideoFrameRetrievalEvaluation.Evaluate(reversed, 2, 1));

        var tied = original with
        {
            Clips =
            [
                new(First, [[1f, 0f]]),
                new(Second, [[1f, 0f]])
            ]
        };
        var outcome = VideoFrameRetrievalEvaluation.Evaluate(tied, 1, 1);
        Assert.Equal(0, outcome.Maximum.HitRateAtK);
        Assert.Equal(outcome.Maximum, outcome.DeduplicatedPooled);
    }

    [Fact]
    public void Rejects_inconsistent_provenance_and_unjudged_or_duplicate_items()
    {
        var data = Sample();
        Assert.Throws<ArgumentException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(
                data with { Protocol = "not-a-protocol" }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(
                data with { ClipModelSha256 = "not-a-digest" }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(
                data with { TextPreprocessingVersion = "private/path" }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(data with
            {
                Clips = [data.Clips[0], data.Clips[0]]
            }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(data with
            {
                Queries = [data.Queries[0], data.Queries[0]]
            }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(data with
            {
                Queries = [data.Queries[0] with { RelevantClipIds = [Guid.NewGuid()] }]
            }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(data with
            {
                Queries = [data.Queries[0] with { RelevantClipIds = [Second, Second] }]
            }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(data with
            {
                Queries = [data.Queries[0] with { TextEmbedding = [0f, 0f] }]
            }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(data with
            {
                Queries = [data.Queries[0] with { TextEmbedding = [1f, 0f, 0f] }]
            }));
        Assert.Throws<ArgumentException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(data with
            {
                Clips = [data.Clips[0], data.Clips[1] with
                {
                    FrameEmbeddings = [[float.NaN, 0f]]
                }]
            }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(data, rankingK: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoFrameRetrievalEvaluation.Evaluate(data, frameTopK: 25));
    }

    [Fact]
    public void All_relevant_judgments_are_required_to_exist_in_the_bounded_corpus()
    {
        var data = Sample();
        var both = data with
        {
            Queries = [data.Queries[0] with { RelevantClipIds = [First, Second] }]
        };
        var result = VideoFrameRetrievalEvaluation.Evaluate(both, 2, 1);
        Assert.Equal(2, result.RelevantJudgments);
        Assert.Equal(0.5, result.Maximum.MeanRecallAtK);
        Assert.Equal(1, result.Maximum.HitRateAtK);
        Assert.Equal(1, result.TopKMean.HitRateAtK);
        Assert.Equal(1, result.DeduplicatedPooled.HitRateAtK);
    }
}
