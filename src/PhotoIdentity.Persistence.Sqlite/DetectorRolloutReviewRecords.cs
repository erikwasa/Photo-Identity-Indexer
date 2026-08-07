using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public enum DetectorReconciliationResolutionKind
{
    ExistingOccurrence,
    NewOccurrence,
    Deferred,
}

/// <summary>
/// Append-only human decision for an ambiguous detector-reconciliation candidate.
/// This resolves face-occurrence identity only; it never assigns a person.
/// </summary>
public sealed record CatalogueDetectorReconciliationResolution(
    long Id,
    ProcessingRunId ProcessingRunId,
    AssetRevisionId AssetRevisionId,
    int CandidateIndex,
    DetectorReconciliationResolutionKind Kind,
    FaceOccurrenceId? FaceOccurrenceId,
    string Actor,
    string? Note,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Review state for one persisted rollout candidate. The inspection payload is durable so
/// a reviewed decision can be applied without re-running detector/alignment/embedding inference.
/// </summary>
public sealed record CatalogueDetectorReconciliationReview(
    CatalogueDetectorReconciliationCandidate Candidate,
    CatalogueDetectorCandidateInspection? Inspection,
    CatalogueDetectorReconciliationResolution? LatestResolution);
