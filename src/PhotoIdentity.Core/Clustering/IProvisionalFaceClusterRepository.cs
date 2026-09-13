using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Clustering;

public static class ProvisionalFaceClusterRunStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Superseded = "superseded";
    public const string Stale = "stale";
    public const string Failed = "failed";

    public static bool IsActive(string status) =>
        string.Equals(status, Pending, StringComparison.Ordinal) ||
        string.Equals(status, Running, StringComparison.Ordinal);
}

public static class ProvisionalFaceClusterPolicies
{
    public const int MaximumFacesPerRun = 20000;

    public static ProvisionalFaceClusterPolicy InitialDbscan { get; } = new(
        Version: "m25-dbscan-v1",
        Algorithm: "dbscan-cosine",
        MinimumClusterSize: 3,
        MinimumSamples: 3,
        DistanceThreshold: 0.30).Validate();
}

public sealed record ProvisionalFaceClusterEvidenceVersion(
    long ReviewActionId,
    long EmbeddingId);

public sealed record ProvisionalFaceClusterRun(
    Guid Id,
    ModelId ModelId,
    Sha256Digest ModelHash,
    ProvisionalFaceClusterPolicy Policy,
    bool IncludeUnknown,
    string Status,
    ProvisionalFaceClusterEvidenceVersion EvidenceVersion,
    int TargetCount,
    int ProcessedTargetCount,
    int ClusterCount,
    int NoiseCount,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? Error)
{
    public bool IsActive => ProvisionalFaceClusterRunStatuses.IsActive(Status);
}

public sealed record ProvisionalFaceClusterInputFace(
    FaceOccurrenceId FaceOccurrenceId,
    AssetRevisionId AssetRevisionId,
    string ReviewState,
    EmbeddingVector Embedding);

public sealed record ProvisionalFaceClusterComputedMembership(
    FaceOccurrenceId FaceOccurrenceId,
    string? DerivedClusterKey,
    ProvisionalFaceClusterMemberRole Role);

public sealed record ProvisionalFaceClusterComputation(
    int EvaluatedFaceCount,
    int ClusterCount,
    int NoiseCount,
    IReadOnlyList<ProvisionalFaceClusterComputedMembership> Memberships);

public sealed record ProvisionalFaceClusterGroupSummary(
    Guid RunId,
    string DerivedClusterKey,
    int MemberCount,
    int CoreCount,
    int BorderCount);

/// <summary>
/// Durable PostgreSQL-backed control state for provisional clustering. Runs are exact-model and
/// policy scoped. Completing a replacement run atomically changes the current derived evidence
/// for that scope; canonical review state is never written by this contract.
/// </summary>
public interface IProvisionalFaceClusterRepository
{
    Task<ProvisionalFaceClusterRun> StartAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        ProvisionalFaceClusterPolicy policy,
        bool includeUnknown,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ProvisionalFaceClusterRun?> GetLatestAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        CancellationToken cancellationToken = default);

    Task<ProvisionalFaceClusterRun?> GetNextActiveAsync(
        CancellationToken cancellationToken = default);

    Task<ProvisionalFaceClusterRun?> TryStartNextRefreshAsync(
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProvisionalFaceClusterInputFace>> ReadInputSnapshotAsync(
        ProvisionalFaceClusterRun run,
        int maximumFaces = ProvisionalFaceClusterPolicies.MaximumFacesPerRun,
        CancellationToken cancellationToken = default);

    Task ReportProgressAsync(
        Guid runId,
        int processedTargetCount,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists all derived memberships and atomically makes the run current when its captured
    /// evidence version is still current. Returns false when the run was marked stale instead.
    /// </summary>
    Task<bool> CompleteAsync(
        ProvisionalFaceClusterRun run,
        ProvisionalFaceClusterComputation computation,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        Guid runId,
        string error,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProvisionalFaceClusterGroupSummary>> ListCurrentGroupsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        string policyVersion,
        bool includeUnknown,
        int maximumGroups = 500,
        CancellationToken cancellationToken = default);

    Task<bool> EvidenceStillMatchesAsync(
        ProvisionalFaceClusterRun run,
        CancellationToken cancellationToken = default);
}
