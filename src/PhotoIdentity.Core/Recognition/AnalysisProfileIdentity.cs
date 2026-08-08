using System.Security.Cryptography;
using System.Text;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Recognition;

/// <summary>
/// Canonical identity for one detector/embedder analysis profile.
/// </summary>
public sealed record AnalysisProfileDefinition
{
    public AnalysisProfileDefinition(
        Sha256Digest detectorPipelineHash,
        ModelId detectorModelId,
        Sha256Digest detectorModelHash,
        ModelId embedderModelId,
        Sha256Digest embedderModelHash,
        AlignmentProtocolId alignmentProtocol)
    {
        DetectorPipelineHash = detectorPipelineHash;
        DetectorModelId = detectorModelId;
        DetectorModelHash = detectorModelHash;
        EmbedderModelId = embedderModelId;
        EmbedderModelHash = embedderModelHash;
        AlignmentProtocol = alignmentProtocol;
    }

    public Sha256Digest DetectorPipelineHash { get; }
    public ModelId DetectorModelId { get; }
    public Sha256Digest DetectorModelHash { get; }
    public ModelId EmbedderModelId { get; }
    public Sha256Digest EmbedderModelHash { get; }
    public AlignmentProtocolId AlignmentProtocol { get; }

    public Sha256Digest ComputeHash()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ToCanonicalText());
        return new Sha256Digest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public string ToCanonicalText() => string.Join('\n',
    [
        "analysis-profile-v1",
        $"detector-pipeline-sha256={DetectorPipelineHash}",
        $"detector-model-id={DetectorModelId}",
        $"detector-model-sha256={DetectorModelHash}",
        $"embedder-model-id={EmbedderModelId}",
        $"embedder-model-sha256={EmbedderModelHash}",
        $"alignment-protocol={AlignmentProtocol}",
    ]);
}
