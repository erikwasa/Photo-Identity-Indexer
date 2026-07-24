using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Tests;

public sealed class EmbeddingVectorTests
{
    [Fact]
    public void ConstructorCopiesInput()
    {
        float[] values = [1, 2, 3];
        EmbeddingVector vector = new(values);

        values[0] = 99;

        Assert.Equal(1f, vector.Values[0]);
    }

    [Fact]
    public void CosineSimilarityHandlesEqualAndOrthogonalVectors()
    {
        EmbeddingVector first = new([1, 0]);
        EmbeddingVector sameDirection = new([2, 0]);
        EmbeddingVector orthogonal = new([0, 1]);

        Assert.Equal(1, first.CosineSimilarity(sameDirection), 12);
        Assert.Equal(0, first.CosineSimilarity(orthogonal), 12);
    }

    [Fact]
    public void InvalidVectorsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new EmbeddingVector([]));
        Assert.Throws<ArgumentException>(() => new EmbeddingVector([0, 0]));
        Assert.Throws<ArgumentException>(() => new EmbeddingVector([float.NaN, 1]));
    }

    [Fact]
    public void SimilarityRequiresMatchingDimensions()
    {
        EmbeddingVector first = new([1, 0]);
        EmbeddingVector second = new([1, 0, 0]);

        Assert.Throws<ArgumentException>(() => first.CosineSimilarity(second));
    }
}
