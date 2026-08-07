using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Durable registration of one exact detector pipeline against a processing run.
/// </summary>
public sealed record CatalogueDetectorPipelineRegistration(
    ProcessingRunId ProcessingRunId,
    Sha256Digest PipelineHash,
    string CanonicalDefinition,
    DateTimeOffset RecordedAtUtc);

/// <summary>
/// Persisted candidate-side reconciliation evidence for one immutable asset revision.
/// </summary>
public sealed record CatalogueDetectorReconciliationCandidate(
    int CandidateIndex,
    FaceDetectionReconciliationDisposition Disposition,
    FaceOccurrenceId? ProposedFaceOccurrenceId,
    IReadOnlyList<FaceOccurrenceId> PossibleFaceOccurrenceIds,
    NormalizedBoundingBox BoundingBox,
    NormalizedFaceLandmarks Landmarks,
    FaceOccurrenceId? AppliedFaceOccurrenceId,
    DateTimeOffset? AppliedAtUtc);

/// <summary>
/// Durable detector reconciliation plan. Existing occurrences without a candidate are retained as evidence.
/// </summary>
public sealed record CatalogueDetectorReconciliationPlan(
    ProcessingRunId ProcessingRunId,
    AssetRevisionId AssetRevisionId,
    Sha256Digest PipelineHash,
    DateTimeOffset PlannedAtUtc,
    IReadOnlyList<CatalogueDetectorReconciliationCandidate> Candidates,
    IReadOnlyList<FaceOccurrenceId> ExistingOccurrencesWithoutCandidate);

/// <summary>
/// Complete inspection payload for one detector candidate before a stable face occurrence is selected.
/// </summary>
public sealed record CatalogueDetectorCandidateInspection
{
    public CatalogueDetectorCandidateInspection(
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
        ModelId embedderModelId,
        Sha256Digest embedderModelHash,
        EmbeddingVector embedding,
        DateTimeOffset observedAtUtc)
    {
        if (!double.IsFinite(confidence) || confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between zero and one.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(cropStoragePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cropWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cropHeight);
        ArgumentNullException.ThrowIfNull(embedding);

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
        EmbedderModelId = embedderModelId;
        EmbedderModelHash = embedderModelHash;
        Embedding = embedding;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
    }

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
    public ModelId EmbedderModelId { get; }
    public Sha256Digest EmbedderModelHash { get; }
    public EmbeddingVector Embedding { get; }
    public DateTimeOffset ObservedAtUtc { get; }
}
