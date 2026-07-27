using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueReviewSuggestionAction(
    long Id,
    string Kind,
    string Actor,
    string? Note,
    DateTimeOffset CreatedAtUtc,
    long? ReviewActionId);

public sealed record CatalogueReviewIdentitySuggestion(
    long Id,
    CatalogueReviewPerson Person,
    ModelId ModelId,
    Sha256Digest ModelHash,
    int Rank,
    double Score,
    double? ScoreMargin,
    string Status,
    DateTimeOffset GeneratedAtUtc,
    CatalogueReviewSuggestionAction? LatestAction);
