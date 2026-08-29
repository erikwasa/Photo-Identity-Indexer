namespace PhotoIdentity.Web.Contracts;

public sealed record SlideshowOriginalPreparationRequest(
    string[] RevisionIds);

public sealed record SlideshowOriginalPreparationResponse(
    string SessionId,
    string State,
    int Ready,
    int Total,
    long RequiredAdditionalBytes,
    long AvailableManagedCapacity,
    string? Message,
    bool CanContinueWithAvailable);
