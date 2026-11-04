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
    bool HasPoster,
    bool HasPlayableProxy);

public sealed record VideoLibraryResponse(
    IReadOnlyList<VideoLibraryItemResponse> Items,
    int Offset,
    int Limit,
    bool HasMore);

/// <summary>Aggregate read-only counts only, never private source identifiers.</summary>
public sealed record VideoLibraryCoverageResponse(
    long TotalVideos, long PlayableVideos, long VideosWithPosters,
    long PlayableWithPosters, long PlayableMissingPosters,
    long UninspectedOrUnsupported);
