using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public static class CataloguePersonAuditSorts
{
    public const string AssignedDescending = "assigned-desc";
    public const string AssignedAscending = "assigned-asc";
    public const string DisagreementFirst = "disagreement-first";
    public const string ConfidenceAscending = "confidence-asc";
}

public sealed record CataloguePersonAuditFace(
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
    CatalogueReviewPerson AssignedPerson,
    CatalogueSuggestionGalleryTopSuggestion? TopSuggestion,
    bool SuggestionDisagrees);

public sealed record CataloguePersonAuditPage(
    CatalogueReviewPerson Person,
    IReadOnlyList<CataloguePersonAuditFace> Items,
    int Offset,
    int Limit,
    int Total,
    int DisagreementCount,
    string Sort);
