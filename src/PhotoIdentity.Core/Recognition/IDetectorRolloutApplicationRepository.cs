using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Recognition;

/// <summary>
/// Provider-neutral persistence boundary for detector rollout state and decisions.
/// </summary>
public interface IDetectorRolloutApplicationRepository
{
    Task<IReadOnlyList<ExistingFaceDetectionAnchor>> GetExistingAnchorsAsync(
        AssetRevisionId assetRevisionId,
        Sha256Digest currentPipelineHash,
        CancellationToken cancellationToken = default);

    Task<CatalogueDetectorRolloutOccurrenceAnchor?> GetOccurrenceAnchorAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default);

    Task<Sha256Digest> GetPipelineHashAsync(
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken = default);

    Task<CatalogueDetectorRolloutSummary> GetSummaryAsync(
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogueDetectorRolloutPendingReview>> GetPendingReviewsAsync(
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken = default);

    Task<CatalogueDetectorRolloutApplyResult> ApplyResolvedAsync(
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken = default);

    Task<FaceOccurrenceId> ApplyReviewedCandidateAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken = default);
}
