namespace PhotoIdentity.Core.Sources;

public sealed record PhotoMetadataTag(string Directory, string Name, string Value);

public sealed record PhotoCaptureMetadata
{
    public PhotoCaptureMetadata(
        DateTime? takenAtLocal = null,
        TimeSpan? utcOffset = null,
        double? latitude = null,
        double? longitude = null,
        string? cameraMake = null,
        string? cameraModel = null,
        string? lensModel = null,
        string? orientation = null,
        string? exposureTime = null,
        string? aperture = null,
        string? iso = null,
        string? focalLength = null,
        string? focalLength35Mm = null,
        string? flash = null,
        string? gpsAltitude = null,
        IReadOnlyList<PhotoMetadataTag>? rawTags = null)
    {
        if ((latitude is null) != (longitude is null))
            throw new ArgumentException("Latitude and longitude must either both be supplied or both be omitted.");
        if (latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90 degrees.");
        if (longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180 degrees.");
        if (utcOffset is not null &&
            (utcOffset.Value < TimeSpan.FromHours(-14) || utcOffset.Value > TimeSpan.FromHours(14)))
            throw new ArgumentOutOfRangeException(nameof(utcOffset), "Capture UTC offset must be between -14 and +14 hours.");
        if (utcOffset is not null && takenAtLocal is null)
            throw new ArgumentException("A capture UTC offset cannot be stored without a capture timestamp.", nameof(utcOffset));

        TakenAtLocal = takenAtLocal is null
            ? null
            : DateTime.SpecifyKind(takenAtLocal.Value, DateTimeKind.Unspecified);
        UtcOffset = utcOffset;
        Latitude = latitude;
        Longitude = longitude;
        CameraMake = Optional(cameraMake);
        CameraModel = Optional(cameraModel);
        LensModel = Optional(lensModel);
        Orientation = Optional(orientation);
        ExposureTime = Optional(exposureTime);
        Aperture = Optional(aperture);
        Iso = Optional(iso);
        FocalLength = Optional(focalLength);
        FocalLength35Mm = Optional(focalLength35Mm);
        Flash = Optional(flash);
        GpsAltitude = Optional(gpsAltitude);
        RawTags = rawTags is null
            ? []
            : rawTags
                .Where(tag =>
                    !string.IsNullOrWhiteSpace(tag.Directory) &&
                    !string.IsNullOrWhiteSpace(tag.Name) &&
                    !string.IsNullOrWhiteSpace(tag.Value))
                .ToArray();
    }

    public DateTime? TakenAtLocal { get; }
    public TimeSpan? UtcOffset { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }
    public string? CameraMake { get; }
    public string? CameraModel { get; }
    public string? LensModel { get; }
    public string? Orientation { get; }
    public string? ExposureTime { get; }
    public string? Aperture { get; }
    public string? Iso { get; }
    public string? FocalLength { get; }
    public string? FocalLength35Mm { get; }
    public string? Flash { get; }
    public string? GpsAltitude { get; }
    public IReadOnlyList<PhotoMetadataTag> RawTags { get; }
    public bool HasCaptureTime => TakenAtLocal.HasValue;
    public bool HasLocation => Latitude.HasValue;
    public bool HasExtendedMetadata =>
        CameraMake is not null ||
        CameraModel is not null ||
        LensModel is not null ||
        Orientation is not null ||
        ExposureTime is not null ||
        Aperture is not null ||
        Iso is not null ||
        FocalLength is not null ||
        FocalLength35Mm is not null ||
        Flash is not null ||
        GpsAltitude is not null;
    public bool HasAnyValue => HasCaptureTime || HasLocation || HasExtendedMetadata || RawTags.Count > 0;

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public interface IPhotoMetadataReader
{
    Task<PhotoCaptureMetadata> ReadAsync(
        Stream content,
        string? mediaType,
        CancellationToken cancellationToken = default);
}
