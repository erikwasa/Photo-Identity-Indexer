namespace PhotoIdentity.Core.Sources;

/// <summary>
/// Photographic capture metadata read from the immutable source revision.
/// Camera timestamps without an offset remain unspecified/local wall-clock values.
/// </summary>
public sealed record PhotoCaptureMetadata
{
    public PhotoCaptureMetadata(
        DateTime? takenAtLocal = null,
        TimeSpan? utcOffset = null,
        double? latitude = null,
        double? longitude = null)
    {
        if ((latitude is null) != (longitude is null))
        {
            throw new ArgumentException("Latitude and longitude must either both be supplied or both be omitted.");
        }

        if (latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90 degrees.");
        }

        if (longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180 degrees.");
        }

        if (utcOffset is < TimeSpan.FromHours(-14) or > TimeSpan.FromHours(14))
        {
            throw new ArgumentOutOfRangeException(nameof(utcOffset), "Capture UTC offset must be between -14 and +14 hours.");
        }

        if (utcOffset is not null && takenAtLocal is null)
        {
            throw new ArgumentException("A capture UTC offset cannot be stored without a capture timestamp.", nameof(utcOffset));
        }

        TakenAtLocal = takenAtLocal is null
            ? null
            : DateTime.SpecifyKind(takenAtLocal.Value, DateTimeKind.Unspecified);
        UtcOffset = utcOffset;
        Latitude = latitude;
        Longitude = longitude;
    }

    public DateTime? TakenAtLocal { get; }

    public TimeSpan? UtcOffset { get; }

    public double? Latitude { get; }

    public double? Longitude { get; }

    public bool HasCaptureTime => TakenAtLocal.HasValue;

    public bool HasLocation => Latitude.HasValue;

    public bool HasAnyValue => HasCaptureTime || HasLocation;
}

public interface IPhotoMetadataReader
{
    Task<PhotoCaptureMetadata> ReadAsync(
        Stream content,
        string? mediaType,
        CancellationToken cancellationToken = default);
}
