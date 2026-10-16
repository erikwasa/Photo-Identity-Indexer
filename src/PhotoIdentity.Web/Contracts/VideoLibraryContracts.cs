namespace PhotoIdentity.Web.Contracts;

/// <summary>No source locator, local path, camera identity or GPS leaves the server.</summary>
public sealed record VideoLibraryItemResponse(
    string RevisionId,
    string? InspectionStatus,
    decimal? DurationSeconds,
    int? Width,
    int? Height,
    int? RotationDegrees,
    DateTimeOffset? CaptureAtUtc,
    bool HasPoster);

public sealed record VideoLibraryResponse(
    IReadOnlyList<VideoLibraryItemResponse> Items,
    int Offset,
    int Limit,
    bool HasMore);
