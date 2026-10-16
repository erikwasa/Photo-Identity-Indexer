using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Catalogue;

/// <summary>
/// Immutable, revision-bound technical inspection of an explicitly selected local video.
/// Null media fields mean that FFprobe did not establish the value; they are not inferred
/// from file modification time or from the photo processing pipeline.
/// </summary>
public sealed record VideoRevisionInspection(
    AssetRevisionId RevisionId,
    string InspectionStatus,
    string? UnsupportedReasonCode,
    string? ContainerName,
    string? VideoCodec,
    string? VideoProfile,
    string? AudioCodec,
    string? PixelFormat,
    string? ColorTransfer,
    int? CodedWidth,
    int? CodedHeight,
    int? DisplayRotationDegrees,
    decimal? DurationSeconds,
    string? AverageFrameRate,
    DateTimeOffset? CaptureAtUtc,
    string? CaptureTimeProvenance,
    string? CameraMake,
    string? CameraModel,
    double? GpsLatitude,
    double? GpsLongitude,
    DateTimeOffset InspectedAtUtc,
    string ToolSha256,
    string InspectionProfileId);

/// <summary>References a bounded durable JPEG poster; no original video bytes are stored here.</summary>
public sealed record VideoStillPoster(
    AssetRevisionId RevisionId,
    string PosterProfileId,
    string ToolSha256,
    string PosterSha256,
    string PosterRelativePath,
    long EncodedBytes,
    int Width,
    int Height,
    DateTimeOffset GeneratedAtUtc);
