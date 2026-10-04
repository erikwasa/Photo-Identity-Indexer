using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

public static class PersonAuditSorts
{
    public const string AssignedDescending = "assigned-desc";
    public const string AssignedAscending = "assigned-asc";
    public const string DisagreementFirst = "disagreement-first";
    public const string ConfidenceAscending = "confidence-asc";
}

public static class AssignmentAuditSources
{
    public const string All = "all";
    public const string Automatic = "automatic";
    public const string Manual = "manual";
}

public sealed record PersonAuditTopSuggestion(
    long Id,
    ReviewPerson Person,
    ModelId ModelId,
    Sha256Digest ModelHash,
    int Rank,
    double Score,
    double? ScoreMargin,
    string Status,
    DateTimeOffset GeneratedAtUtc);

public sealed record PersonAuditFace(
    FaceOccurrenceId Id,
    int Ordinal,
    DateTimeOffset FaceCreatedAtUtc,
    DateTimeOffset AssignedAtUtc,
    string PhotoName,
    string MediaType,
    int? PhotoWidth,
    int? PhotoHeight,
    Sha256Digest RevisionHash,
    string? CropStoragePath,
    double? Confidence,
    long AssignmentActionId,
    ReviewPerson AssignedPerson,
    PersonAuditTopSuggestion? TopSuggestion,
    bool SuggestionDisagrees);

public sealed record PersonAuditPage(
    ReviewPerson Person,
    IReadOnlyList<PersonAuditFace> Items,
    int Offset,
    int Limit,
    int Total,
    int DisagreementCount,
    string Sort);

public sealed record AssignmentAuditFace(
    FaceOccurrenceId Id,
    int Ordinal,
    DateTimeOffset FaceCreatedAtUtc,
    DateTimeOffset AssignedAtUtc,
    string PhotoName,
    string MediaType,
    int? PhotoWidth,
    int? PhotoHeight,
    Sha256Digest RevisionHash,
    string? CropStoragePath,
    double? Confidence,
    long AssignmentActionId,
    string AssignmentActor,
    ReviewPerson AssignedPerson,
    PersonAuditTopSuggestion? AcceptedSuggestion,
    PersonAuditTopSuggestion? CurrentTopSuggestion,
    bool CurrentSuggestionDisagrees);

public sealed record AssignmentAuditPage(
    IReadOnlyList<AssignmentAuditFace> Items,
    int Offset,
    int Limit,
    int Total,
    string Source,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);

/// <summary>
/// Read-only audit view over active assignments with assignment-linked provenance and optional exact-model suggestion comparison.
/// </summary>
public interface IPersonAuditRepository
{
    Task<PersonAuditPage?> GetFacesAsync(
        PersonId personId,
        ModelId? modelId = null,
        Sha256Digest? modelHash = null,
        int offset = 0,
        int limit = 40,
        bool disagreementsOnly = false,
        string sort = PersonAuditSorts.AssignedDescending,
        CancellationToken cancellationToken = default);

    Task<AssignmentAuditPage> GetAssignmentsAsync(
        string source = AssignmentAuditSources.Automatic,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        ModelId? modelId = null,
        Sha256Digest? modelHash = null,
        int offset = 0,
        int limit = 120,
        CancellationToken cancellationToken = default);
}
