using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Sqlite;

public static class CatalogueBulkReviewActionKinds
{
    public const string Assign = "assign";
    public const string Reject = "reject";
}

public sealed record CatalogueBulkReviewPreview(
    string Action,
    int RequestedCount,
    int AffectedCount,
    int SkippedCount,
    string PreviewToken,
    CatalogueReviewPerson? Person);

public sealed record CatalogueBulkReviewResult(
    string Action,
    int RequestedCount,
    int AffectedCount,
    CatalogueReviewPerson? Person,
    DateTimeOffset CreatedAtUtc);
