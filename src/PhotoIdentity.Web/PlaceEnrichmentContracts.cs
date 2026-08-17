namespace PhotoIdentity.Web;

public sealed record PhotoPlaceEnrichmentStatusResponse(
    string Provider,
    bool Configured,
    string ContractKey,
    string ServiceHost,
    string Language,
    int MinimumRequestIntervalMilliseconds);

public sealed record PhotoPlaceEnrichmentIssueResponse(
    string RevisionId,
    string Outcome,
    string? ProviderCode,
    string Message);

public sealed record PhotoPlaceEnrichmentReportResponse(
    int Candidates,
    int ProviderRequests,
    int CachedResults,
    int Assigned,
    int UnchangedAutomatic,
    int SkippedManual,
    int SkippedConflict,
    int NoResult,
    int Deferred,
    int Failed,
    bool StoppedEarly,
    string? StopReasonCode = null,
    string? StopReasonMessage = null,
    IReadOnlyList<PhotoPlaceEnrichmentIssueResponse>? Issues = null);

public sealed record PhotoPlaceEnrichmentErrorResponse(string Error);
