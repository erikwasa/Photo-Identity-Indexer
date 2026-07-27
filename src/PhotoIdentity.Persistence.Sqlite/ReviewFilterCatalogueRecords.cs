using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueReviewProcessingRun(
    ProcessingRunId Id,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int FaceCount);

public sealed record CatalogueReviewModelRevision(
    ModelId ModelId,
    Sha256Digest ModelHash,
    DateTimeOffset GeneratedAtUtc,
    int FaceCount);

public sealed record CatalogueReviewFilterOptions(
    IReadOnlyList<CatalogueReviewProcessingRun> ProcessingRuns,
    IReadOnlyList<CatalogueReviewModelRevision> ModelRevisions);
