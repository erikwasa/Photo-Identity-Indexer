using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// Legacy fixture-shaped identity records backed exclusively by PostgreSQL test state.
/// They keep mature integration assertions readable while WI-0148 removes the SQLite assembly.
/// </summary>
public sealed record CatalogueHumanLabel(
    long Id,
    PersonId PersonId,
    FaceOccurrenceId FaceOccurrenceId,
    string LabelKind,
    string AssignedBy,
    DateTimeOffset AssignedAtUtc,
    string? Note);

public enum IdentityMatchTargetScope
{
    UnreviewedOnly,
    UnreviewedAndUnknown,
}

public sealed record IdentityMatchSummary(
    int TargetCount,
    int SuggestedTargetCount,
    int SuggestionCount);

public sealed record CatalogueRankedIdentitySuggestion(
    long SuggestionId,
    FaceOccurrenceId FaceOccurrenceId,
    PersonId SuggestedPersonId,
    ModelId ModelId,
    Sha256Digest ModelHash,
    int Rank,
    double Score,
    double? ScoreMargin,
    string Status,
    DateTimeOffset GeneratedAtUtc);

public sealed record PhotoMetadataBackfillCandidate(
    AssetRevisionId RevisionId,
    Sha256Digest ContentHash,
    long SizeBytes,
    string RootLocator,
    string SourceKey,
    string? MediaType);
