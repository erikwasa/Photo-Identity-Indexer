using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Recognition;

/// <summary>
/// Persists exact pipeline provenance and immutable reconciliation plans before face mutation.
/// </summary>
public interface IDetectorReconciliationPlanRepository
{
    Task<CatalogueDetectorPipelineRegistration> RegisterPipelineAsync(
        ProcessingRunId processingRunId,
        DetectorPipelineDefinition definition,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default);

    Task<CatalogueDetectorReconciliationPlan> SavePlanAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        Sha256Digest pipelineHash,
        IReadOnlyList<CandidateFaceDetectionAnchor> candidateFaces,
        FaceDetectionReconciliationPlan plan,
        DateTimeOffset plannedAtUtc,
        CancellationToken cancellationToken = default);

    Task<CatalogueDetectorReconciliationPlan?> GetPlanAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        CancellationToken cancellationToken = default);
}
