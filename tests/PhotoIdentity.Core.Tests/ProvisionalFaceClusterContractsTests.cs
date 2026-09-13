using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Recognition;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class ProvisionalFaceClusterContractsTests
{
    [Fact]
    public void Policy_requires_version_algorithm_and_conservative_bounds()
    {
        ProvisionalFaceClusterPolicy valid = new(
            "wi-0113-v1",
            "mutual-neighbor-graph",
            MinimumClusterSize: 3,
            MinimumSamples: 2,
            DistanceThreshold: 0.25,
            MutualNeighborCount: 5,
            MinimumSharedNeighbors: 1);

        Assert.Same(valid, valid.Validate());
        Assert.Throws<ArgumentException>(() => (valid with { Version = "" }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { Algorithm = " " }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (valid with { MinimumClusterSize = 1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (valid with { MinimumSamples = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (valid with { DistanceThreshold = 2.01 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (valid with { MutualNeighborCount = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (valid with { MinimumSharedNeighbors = -1 }).Validate());
    }

    [Fact]
    public void Provenance_keeps_exact_model_and_policy_revision_explicit()
    {
        ModelId modelId = new("sface-test");
        Sha256Digest modelHash = new(new string('a', 64));
        DateTimeOffset generatedAt = new(2026, 9, 13, 18, 0, 0, TimeSpan.Zero);

        ProvisionalFaceClusterProvenance provenance = new(
            modelId,
            modelHash,
            "wi-0113-v1",
            "dbscan",
            generatedAt);

        Assert.Equal(modelId, provenance.EmbeddingModelId);
        Assert.Equal(modelHash, provenance.EmbeddingModelHash);
        Assert.Equal("wi-0113-v1", provenance.PolicyVersion);
        Assert.Equal("dbscan", provenance.Algorithm);
        Assert.Contains("never", ProvisionalFaceClusterSemantics.CanonicalDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("canonical", ProvisionalFaceClusterSemantics.CanonicalDefinition, StringComparison.OrdinalIgnoreCase);
    }
}
