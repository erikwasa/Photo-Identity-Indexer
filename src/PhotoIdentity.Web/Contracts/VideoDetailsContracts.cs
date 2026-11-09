namespace PhotoIdentity.Web.Contracts;

/// <summary>
/// Only revision-bound catalogue facts and manual annotations reach the
/// browser. No source paths, embeddings, face crops or model suggestions.
/// </summary>
public sealed record VideoDetailsPersonResponse(
    string Id, string DisplayName, string Actor, DateTimeOffset AssignedAtUtc);

public sealed record VideoDetailsPlaceResponse(
    string Value, string Source, string Actor, DateTimeOffset AssignedAtUtc);

public sealed record VideoDetailsResponse(
    string RevisionId,
    string FileName,
    string? InspectionStatus,
    DateTimeOffset? ExtractedCaptureAtUtc,
    string? CaptureTimeProvenance,
    DateTimeOffset? EffectiveCaptureAtUtc,
    string CaptureSource,
    string? CameraMake,
    string? CameraModel,
    double? Latitude,
    double? Longitude,
    int? Width,
    int? Height,
    int? RotationDegrees,
    decimal? DurationSeconds,
    string? AverageFrameRate,
    VideoDetailsPlaceResponse? Place,
    bool ManualPlaceOverride,
    IReadOnlyList<VideoDetailsPersonResponse> People,
    string? LastGpsResolvedPlace,
    string? VideoCodec,
    string? AudioCodec,
    string? PixelFormat,
    string? ColorTransfer,
    long? VideoBitrateBps,
    int? AudioSampleRateHz,
    int? AudioChannels,
    string? AudioChannelLayout);

public sealed record VideoDetailsPersonRequest(string PersonId);
public sealed record VideoDetailsPlaceRequest(string Value);
public sealed record VideoDetailsCaptureTimeRequest(string Value);
public sealed record VideoDetailsPersonPickerEntry(string Id, string DisplayName);
public sealed record VideoDetailsErrorResponse(string Error);
