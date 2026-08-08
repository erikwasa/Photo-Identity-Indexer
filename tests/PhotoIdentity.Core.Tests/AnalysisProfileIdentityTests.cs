using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Tests;

public sealed class AnalysisProfileIdentityTests
{
    [Fact]
    public void Hash_changes_when_material_pipeline_or_embedder_identity_changes()
    {
        AnalysisProfileDefinition baseline = Create('a', 'b', 'c');
        AnalysisProfileDefinition changedPipeline = Create('d', 'b', 'c');
        AnalysisProfileDefinition changedEmbedder = Create('a', 'e', 'c');
        AnalysisProfileDefinition changedAlignment = Create('a', 'b', 'f');

        Assert.Equal(baseline.ComputeHash(), Create('a', 'b', 'c').ComputeHash());
        Assert.NotEqual(baseline.ComputeHash(), changedPipeline.ComputeHash());
        Assert.NotEqual(baseline.ComputeHash(), changedEmbedder.ComputeHash());
        Assert.NotEqual(baseline.ComputeHash(), changedAlignment.ComputeHash());
    }

    private static AnalysisProfileDefinition Create(
        char pipelineHash,
        char embedderHash,
        char alignmentSuffix) => new(
        new Sha256Digest(new string(pipelineHash, 64)),
        new ModelId("centerface-2019-fp32"),
        new Sha256Digest(new string('1', 64)),
        new ModelId("sface-2021dec-fp32"),
        new Sha256Digest(new string(embedderHash, 64)),
        new AlignmentProtocolId($"sface-five-point-v1-{alignmentSuffix}"));
}
