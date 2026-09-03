using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

public static class ReviewIdentityMatchRegenerationStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Stale = "stale";
    public const string Failed = "failed";

    public static bool IsActive(string status) =>
        string.Equals(status, Pending, StringComparison.Ordinal) ||
        string.Equals(status, Running, StringComparison.Ordinal);
}

public static class ReviewIdentityMatchRegenerationTargetStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Error = "error";
}

public sealed record ReviewIdentityMatchRegenerationRun(
    Guid Id,
    ModelId ModelId,
    Sha256Digest ModelHash,
    int PolicyVersion,
    string Status,
    ReviewIdentityMatchEvidenceVersion EvidenceVersion,
    int TargetCount,
    int ProcessedTargetCount,
    int SuggestedTargetCount,
    int SuggestionCount,
    int AutomaticallyAssignedCount,
    int ErrorCount,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? Error)
{
    public bool IsActive => ReviewIdentityMatchRegenerationStatuses.IsActive(Status);
}

public sealed record ReviewIdentityMatchRegenerationTarget(
    Guid RunId,
    FaceOccurrenceId FaceOccurrenceId,
    int Ordinal,
    string Status,
    int SuggestionCount,
    string? Error);

/// <summary>
/// Durable control-state store for identity suggestion regeneration. A run snapshots the
/// exact-model evidence version and eligible target faces so interrupted work can resume safely.
/// </summary>
public interface IIdentityMatchRegenerationRepository
{
    Task<ReviewIdentityMatchRegenerationRun> StartAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int policyVersion,
        string requestedBy,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ReviewIdentityMatchRegenerationRun?> GetLatestAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default);

    Task<ReviewIdentityMatchRegenerationRun?> GetNextActiveAsync(
        CancellationToken cancellationToken = default);

    Task<ReviewIdentityMatchRegenerationTarget?> ClaimNextTargetAsync(
        Guid runId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task CompleteTargetAsync(
        Guid runId,
        FaceOccurrenceId faceOccurrenceId,
        int suggestionCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task FailTargetAsync(
        Guid runId,
        FaceOccurrenceId faceOccurrenceId,
        string error,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task CompleteRunAsync(
        Guid runId,
        int automaticallyAssignedCount,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task MarkFailedAsync(
        Guid runId,
        string error,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> EvidenceStillMatchesAsync(
        ReviewIdentityMatchRegenerationRun run,
        CancellationToken cancellationToken = default);
}
