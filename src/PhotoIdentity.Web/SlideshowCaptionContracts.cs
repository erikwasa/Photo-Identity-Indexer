namespace PhotoIdentity.Web;

public sealed record SlideshowCaptionRequest(string Language);

public sealed record SlideshowCaptionResponse(
    string RevisionId,
    string Language,
    string Status,
    string? Caption,
    bool Cached);
