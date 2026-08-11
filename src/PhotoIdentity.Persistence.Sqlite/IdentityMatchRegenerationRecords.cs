using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public static class IdentityMatchRegenerationStatuses
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

public sealed record IdentityMatchEvidenceVersion(
    long ReviewActionId,
    long SuggestionReviewActionId,
    long PersonMergeActionId,
    long EmbeddingId)
{
    public override string ToString() =>
        $"review:{ReviewActionId};suggestion:{SuggestionReviewActionId};merge:{PersonMergeActionId};embedding:{EmbeddingId}";
}

public sealed record CatalogueIdentityMatchRegenerationRun(
    Guid Id,
    ModelId ModelId,
    Sha256Digest ModelHash,
    int PolicyVersion,
    string Status,
    IdentityMatchEvidenceVersion EvidenceVersion,
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
    public bool IsActive => IdentityMatchRegenerationStatuses.IsActive(Status);
}

public sealed record CatalogueIdentityMatchRegenerationTarget(
    Guid RunId,
    FaceOccurrenceId FaceOccurrenceId,
    int Ordinal,
    string Status,
    int SuggestionCount,
    string? Error);
