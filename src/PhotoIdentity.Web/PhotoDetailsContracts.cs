namespace PhotoIdentity.Web.Contracts;

public sealed record PhotoDetailsPersonResponse(
    string Id,
    string DisplayName,
    int ConfirmedFaceCount,
    bool ManualPresence);

public sealed record PhotoMetadataTagResponse(
    string Directory,
    string Name,
    string Value);

public sealed record PhotoMetadataResponse(
    DateTime? TakenAtLocal,
    int? UtcOffsetMinutes,
    double? Latitude,
    double? Longitude,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    string? Orientation,
    string? ExposureTime,
    string? Aperture,
    string? Iso,
    string? FocalLength,
    string? FocalLength35Mm,
    string? Flash,
    string? GpsAltitude,
    IReadOnlyList<PhotoMetadataTagResponse> Tags);

public sealed record PhotoDetailsResponse(
    string RevisionId,
    string FileName,
    IReadOnlyList<PhotoDetailsPersonResponse> People,
    PhotoMetadataResponse? Metadata = null);

public sealed record PhotoPersonMutationRequest(string PersonId);

public sealed record PhotoPersonErrorResponse(string Error);
