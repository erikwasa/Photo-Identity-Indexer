using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Recognition;

/// <summary>
/// Provider-neutral persistence boundary for detector rollout state and decisions.
/// </summary>
public interface IDetectorRolloutReviewRepository
{
    Task<CatalogueDetectorCandidateInspection> SaveInspectionAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CatalogueDetectorCandidateInspection inspection,
        CancellationToken cancellationToken = default);

    Task<CatalogueDetectorCandidateInspection?> GetInspectionAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken = default);

    Task<CatalogueDetectorReconciliationResolution> RecordResolutionAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        DetectorReconciliationResolutionKind kind,
        FaceOccurrenceId? faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogueDetectorReconciliationResolution>> GetResolutionHistoryAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken = default);

    Task<CatalogueDetectorReconciliationReview?> GetReviewAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogueDetectorReconciliationReview>> GetPendingAmbiguousAsync(
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken = default);
}
