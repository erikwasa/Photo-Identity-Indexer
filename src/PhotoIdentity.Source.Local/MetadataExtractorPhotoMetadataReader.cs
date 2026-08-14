using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Source.Local;

public sealed class MetadataExtractorPhotoMetadataReader : IPhotoMetadataReader
{
    public Task<PhotoCaptureMetadata> ReadAsync(Stream content, string? mediaType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(content);
        }
        catch (ImageProcessingException)
        {
            return Task.FromResult(new PhotoCaptureMetadata());
        }

        DateTime? takenAtLocal = null;
        TimeSpan? utcOffset = null;
        ExifSubIfdDirectory? exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (exif is not null && exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime original))
        {
            takenAtLocal = DateTime.SpecifyKind(original, DateTimeKind.Unspecified);
            utcOffset = ParseOffset(exif.GetString(ExifDirectoryBase.TagTimeZoneOriginal));
        }

        GeoLocation? location = directories.OfType<GpsDirectory>().FirstOrDefault()?.GetGeoLocation();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PhotoCaptureMetadata(
            takenAtLocal,
            utcOffset,
            location?.Latitude,
            location?.Longitude));
    }

    private static TimeSpan? ParseOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !TimeSpan.TryParse(value.Trim(), CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            return null;
        }

        return parsed < TimeSpan.FromHours(-14) || parsed > TimeSpan.FromHours(14)
            ? null
            : parsed;
    }
}
