using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Stable face identity within one immutable asset revision.
/// </summary>
public sealed record CatalogueFaceOccurrence
{
    public CatalogueFaceOccurrence(
        FaceOccurrenceId id,
        AssetRevisionId assetRevisionId,
        int ordinal,
        DateTimeOffset createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);

        Id = id;
        AssetRevisionId = assetRevisionId;
        Ordinal = ordinal;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public FaceOccurrenceId Id { get; }
    public AssetRevisionId AssetRevisionId { get; }
    public int Ordinal { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}

/// <summary>
/// Detector output for a face occurrence and one exact detector model revision.
/// </summary>
public sealed record CatalogueFaceObservation
{
    public CatalogueFaceObservation(
        FaceOccurrenceId faceOccurrenceId,
        ModelId detectorModelId,
        Sha256Digest detectorModelHash,
        double confidence,
        NormalizedBoundingBox boundingBox,
        NormalizedFaceLandmarks landmarks,
        DateTimeOffset observedAtUtc)
    {
        if (!double.IsFinite(confidence) || confidence < 0 || confidence > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0 and 1.");
        }

        FaceOccurrenceId = faceOccurrenceId;
        DetectorModelId = detectorModelId;
        DetectorModelHash = detectorModelHash;
        Confidence = confidence;
        BoundingBox = boundingBox;
        Landmarks = landmarks;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
    }

    public FaceOccurrenceId FaceOccurrenceId { get; }
    public ModelId DetectorModelId { get; }
    public Sha256Digest DetectorModelHash { get; }
    public double Confidence { get; }
    public NormalizedBoundingBox BoundingBox { get; }
    public NormalizedFaceLandmarks Landmarks { get; }
    public DateTimeOffset ObservedAtUtc { get; }
}

/// <summary>
/// Persisted aligned crop produced for a face occurrence.
/// </summary>
public sealed record CatalogueFaceCrop
{
    public CatalogueFaceCrop(
        FaceCropId id,
        FaceOccurrenceId faceOccurrenceId,
        AlignmentProtocolId protocol,
        Sha256Digest contentHash,
        string storagePath,
        int width,
        int height,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Id = id;
        FaceOccurrenceId = faceOccurrenceId;
        Protocol = protocol;
        ContentHash = contentHash;
        StoragePath = storagePath.Trim();
        Width = width;
        Height = height;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public FaceCropId Id { get; }
    public FaceOccurrenceId FaceOccurrenceId { get; }
    public AlignmentProtocolId Protocol { get; }
    public Sha256Digest ContentHash { get; }
    public string StoragePath { get; }
    public int Width { get; }
    public int Height { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}

/// <summary>
/// Embedding produced from one exact crop and model revision.
/// </summary>
public sealed record CatalogueFaceEmbedding
{
    public CatalogueFaceEmbedding(
        FaceCropId faceCropId,
        ModelId modelId,
        Sha256Digest modelHash,
        EmbeddingVector vector,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(vector);

        FaceCropId = faceCropId;
        ModelId = modelId;
        ModelHash = modelHash;
        Vector = vector;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    public FaceCropId FaceCropId { get; }
    public ModelId ModelId { get; }
    public Sha256Digest ModelHash { get; }
    public EmbeddingVector Vector { get; }
    public DateTimeOffset CreatedAtUtc { get; }
}

/// <summary>
/// One complete detector, crop and embedding result persisted atomically.
/// </summary>
public sealed record CatalogueFaceInspection(
    CatalogueFaceOccurrence Occurrence,
    CatalogueFaceObservation Observation,
    CatalogueFaceCrop Crop,
    CatalogueFaceEmbedding Embedding);
