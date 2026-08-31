namespace PhotoIdentity.Web.Contracts;

public sealed record SlideshowOriginalPreparationRequest(
    string[] RevisionIds);

public sealed record SlideshowOriginalPreparationResponse(
    string SessionId,
    string State,
    int Ready,
    int Total,
    int Downloading,
    int Queued,
    int WaitingForRelease,
    int HydrationRequests,
    string Phase,
    DateTimeOffset LastProgressAtUtc,
    long NoProgressSeconds,
    bool NoProgressWarning,
    bool CanRetry,
    long RequiredAdditionalBytes,
    long AvailableManagedCapacity,
    string? Message,
    bool CanContinueWithAvailable);
