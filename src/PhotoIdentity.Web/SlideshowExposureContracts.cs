namespace PhotoIdentity.Web.Contracts;

public sealed record SlideshowExposureRequest(
    Guid SessionId,
    Guid CollectionId,
    bool Creative,
    string RevisionId);

public sealed record SlideshowExposureResponse(
    string RevisionId,
    bool Recorded,
    int ShowCount,
    DateTimeOffset? LastShownAtUtc);
