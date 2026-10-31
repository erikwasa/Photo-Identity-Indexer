using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoFrameRetrievalScoringTests
{
    [Fact]
    public void Identical_frames_are_deduplicated_and_scoring_matches_query()
    {
        IReadOnlyList<IReadOnlyList<float>> frames =
        [
            new float[] { 1, 0 }, new float[] { 1, 0 },
            new float[] { 0, 1 }
        ];
        var scores = VideoFrameRetrievalScoring.Compare(frames, [1f, 0f], 2);
        Assert.Equal(3, scores.Frames);
        Assert.Equal(2, scores.DistinctFrames);
        Assert.Equal(1, scores.MaxCosine, 6);
        Assert.Equal(1, scores.TopKMeanCosine, 6);
        Assert.InRange(scores.PooledCosine, 0.70, 0.72);
    }

    [Fact]
    public void Opposite_frames_have_zero_pooled_direction()
    {
        IReadOnlyList<IReadOnlyList<float>> frames =
        [
            new float[] { 1, 0 }, new float[] { -1, 0 }
        ];
        var scores = VideoFrameRetrievalScoring.Compare(frames, [1f, 0f], 2);
        Assert.Equal(0, scores.PooledCosine, 6);
        Assert.Equal(0, scores.TopKMeanCosine, 6);
        Assert.Equal(1, scores.MaxCosine, 6);
    }

    [Fact]
    public void Rejects_nan_dimension_mismatch_zero_vectors_and_invalid_limits()
    {
        Assert.Throws<ArgumentException>(() => VideoFrameRetrievalScoring.Compare(
            [new float[] { 1f, 0f }], [0f, 0f]));
        Assert.Throws<ArgumentException>(() => VideoFrameRetrievalScoring.Compare(
            [new float[] { 1f, float.NaN }], [1f, 0f]));
        Assert.Throws<ArgumentException>(() => VideoFrameRetrievalScoring.Compare(
            [new float[] { 1f, 0f, 0f }], [1f, 0f]));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoFrameRetrievalScoring.Compare(
            [new float[] { 1f, 0f }], [1f, 0f], 25));
    }
}
