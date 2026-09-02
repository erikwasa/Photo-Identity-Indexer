using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Recognition;

/// <summary>
/// Provider-neutral atomic persistence request for one detected face, its aligned crop and embedding.
/// </summary>
public sealed record FaceInspectionWrite
{
    public FaceInspectionWrite(
        FaceOccurrenceId occurrenceId,
        AssetRevisionId assetRevisionId,
        int ordinal,
        DateTimeOffset observedAtUtc,
        ModelId detectorModelId,
        Sha256Digest detectorModelHash,
        double confidence,
        NormalizedBoundingBox boundingBox,
        NormalizedFaceLandmarks landmarks,
        FaceCropId cropId,
        AlignmentProtocolId cropProtocol,
        Sha256Digest cropContentHash,
        string cropStoragePath,
        int cropWidth,
        int cropHeight,
        ModelId embeddingModelId,
        Sha256Digest embeddingModelHash,
        EmbeddingVector embedding)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        if (!double.IsFinite(confidence) || confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(cropStoragePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cropWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cropHeight);
        ArgumentNullException.ThrowIfNull(embedding);

        OccurrenceId = occurrenceId;
        AssetRevisionId = assetRevisionId;
        Ordinal = ordinal;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
        DetectorModelId = detectorModelId;
        DetectorModelHash = detectorModelHash;
        Confidence = confidence;
        BoundingBox = boundingBox;
        Landmarks = landmarks;
        CropId = cropId;
        CropProtocol = cropProtocol;
        CropContentHash = cropContentHash;
        CropStoragePath = cropStoragePath.Trim();
        CropWidth = cropWidth;
        CropHeight = cropHeight;
        EmbeddingModelId = embeddingModelId;
        EmbeddingModelHash = embeddingModelHash;
        Embedding = embedding;
    }

    public FaceOccurrenceId OccurrenceId { get; }
    public AssetRevisionId AssetRevisionId { get; }
    public int Ordinal { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public ModelId DetectorModelId { get; }
    public Sha256Digest DetectorModelHash { get; }
    public double Confidence { get; }
    public NormalizedBoundingBox BoundingBox { get; }
    public NormalizedFaceLandmarks Landmarks { get; }
    public FaceCropId CropId { get; }
    public AlignmentProtocolId CropProtocol { get; }
    public Sha256Digest CropContentHash { get; }
    public string CropStoragePath { get; }
    public int CropWidth { get; }
    public int CropHeight { get; }
    public ModelId EmbeddingModelId { get; }
    public Sha256Digest EmbeddingModelHash { get; }
    public EmbeddingVector Embedding { get; }
}

public interface IFaceInspectionRepository
{
    Task SaveInspectionAsync(
        FaceInspectionWrite inspection,
        CancellationToken cancellationToken = default);
}
