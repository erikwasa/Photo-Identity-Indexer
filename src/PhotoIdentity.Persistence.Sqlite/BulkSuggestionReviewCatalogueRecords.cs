using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueBulkSuggestionPreview(
    int RequestedCount,
    int AffectedCount,
    int SkippedCount,
    string PreviewToken,
    CatalogueReviewPerson Person,
    ModelId ModelId,
    Sha256Digest ModelHash);

public sealed record CatalogueBulkSuggestionResult(
    int RequestedCount,
    int AffectedCount,
    CatalogueReviewPerson Person,
    ModelId ModelId,
    Sha256Digest ModelHash,
    DateTimeOffset CreatedAtUtc);
