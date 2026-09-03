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

/// <summary>
/// Read-only audit view over active human assignments with optional exact-model suggestion comparison.
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
}
