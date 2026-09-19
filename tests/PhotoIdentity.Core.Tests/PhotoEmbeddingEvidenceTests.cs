using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class PhotoEmbeddingEvidenceTests
{
    [Fact]
    public void Evidence_requires_versioned_normalized_finite_vector()
    {
        AssetRevisionId revision = AssetRevisionId.From(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));

        PhotoEmbeddingEvidence evidence = new(
            revision,
            "clip-test",
            new string('a', 64),
            "preprocess-v1",
            [0.6f, 0.8f]);

        Assert.Equal(revision, evidence.RevisionId);
        Assert.Equal(2, evidence.Dimensions);
        Assert.Equal(8, evidence.StorageBytes);
        Assert.Equal(PhotoEmbeddingEvidence.Float32L2NormalizedEncoding, evidence.VectorEncoding);
        Assert.Equal(1d, Math.Sqrt(evidence.Values.Sum(value => value * value)), 3);
    }

    [Fact]
    public void Evidence_rejects_non_normalized_or_invalid_provenance()
    {
        AssetRevisionId revision = AssetRevisionId.From(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));

        Assert.Throws<ArgumentException>(() => new PhotoEmbeddingEvidence(
            revision,
            "clip-test",
            "not-a-hash",
            "preprocess-v1",
            [0.6f, 0.8f]));

        Assert.Throws<ArgumentException>(() => new PhotoEmbeddingEvidence(
            revision,
            "clip-test",
            new string('b', 64),
            "preprocess-v1",
            [3f, 4f]));
    }

    [Fact]
    public void Cosine_similarity_is_exact_and_dimension_checked()
    {
        Assert.Equal(1d, PhotoEmbeddingSimilarity.Cosine([1f, 0f], [1f, 0f]), 6);
        Assert.Equal(0d, PhotoEmbeddingSimilarity.Cosine([1f, 0f], [0f, 1f]), 6);
        Assert.Throws<ArgumentException>(() =>
            PhotoEmbeddingSimilarity.Cosine([1f], [1f, 0f]));
    }
}
