namespace PhotoIdentity.Web.Contracts;

public sealed record BulkSuggestionPreviewRequest(
    IReadOnlyList<long> SuggestionIds,
    string ModelId,
    string ModelHash);

public sealed record BulkSuggestionCommitRequest(
    IReadOnlyList<long> SuggestionIds,
    string ModelId,
    string ModelHash,
    int ExpectedAffectedCount,
    string PreviewToken,
    bool Confirm,
    string Actor,
    string? Note = null);

public sealed record BulkSuggestionPreviewResponse(
    int RequestedCount,
    int AffectedCount,
    int SkippedCount,
    string PreviewToken,
    ReviewPersonResponse Person,
    string ModelId,
    string ModelHash);

public sealed record BulkSuggestionCommitResponse(
    int RequestedCount,
    int AffectedCount,
    ReviewPersonResponse Person,
    string ModelId,
    string ModelHash,
    DateTimeOffset CreatedAtUtc);
