using System.Globalization;
using System.Text;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Source.Local;

public sealed class MetadataExtractorPhotoMetadataReader : IPhotoMetadataReader
{
    private const int MaximumRawTags = 300;
    private const int MaximumDirectoryLength = 120;
    private const int MaximumTagNameLength = 160;
    private const int MaximumTagValueLength = 512;

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

        ExifSubIfdDirectory? exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        ExifIfd0Directory? ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        GpsDirectory? gps = directories.OfType<GpsDirectory>().FirstOrDefault();

        DateTime? takenAtLocal = null;
        TimeSpan? utcOffset = null;
        if (TryGetOriginalDate(exif, out DateTime original))
        {
            takenAtLocal = DateTime.SpecifyKind(original, DateTimeKind.Unspecified);
            utcOffset = ParseOffset(exif?.GetString(ExifDirectoryBase.TagTimeZoneOriginal));
        }
        else if (TryGetOriginalDate(ifd0, out original))
        {
            takenAtLocal = DateTime.SpecifyKind(original, DateTimeKind.Unspecified);
            utcOffset = ParseOffset(ifd0?.GetString(ExifDirectoryBase.TagTimeZoneOriginal));
        }

        GeoLocation? location = gps?.GetGeoLocation();
        IReadOnlyList<PhotoMetadataTag> rawTags = CaptureRawTags(directories, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new PhotoCaptureMetadata(
            takenAtLocal,
            utcOffset,
            location?.Latitude,
            location?.Longitude,
            Description(ifd0, ExifDirectoryBase.TagMake),
            Description(ifd0, ExifDirectoryBase.TagModel),
            Description(exif, ExifDirectoryBase.TagLensModel),
            Description(ifd0, ExifDirectoryBase.TagOrientation),
            Description(exif, ExifDirectoryBase.TagExposureTime),
            Description(exif, ExifDirectoryBase.TagFNumber),
            Description(exif, ExifDirectoryBase.TagIsoEquivalent),
            Description(exif, ExifDirectoryBase.TagFocalLength),
            Description(exif, ExifDirectoryBase.Tag35MMFilmEquivFocalLength),
            Description(exif, ExifDirectoryBase.TagFlash),
            Description(gps, GpsDirectory.TagAltitude),
            rawTags));
    }

    private static bool TryGetOriginalDate(ExifDirectoryBase? directory, out DateTime value)
    {
        value = default;
        return directory is not null &&
            directory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out value);
    }

    private static string? Description(MetadataExtractor.Directory? directory, int tagType)
    {
        string? value = directory?.GetDescription(tagType);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Sanitize(value, MaximumTagValueLength);
    }

    private static IReadOnlyList<PhotoMetadataTag> CaptureRawTags(
        IReadOnlyList<MetadataExtractor.Directory> directories,
        CancellationToken cancellationToken)
    {
        List<PhotoMetadataTag> tags = [];
        foreach (MetadataExtractor.Directory directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (Tag tag in directory.Tags)
            {
                if (tags.Count >= MaximumRawTags)
                {
                    return tags;
                }

                if (!SafeForSnapshot(directory.Name, tag.Name) || string.IsNullOrWhiteSpace(tag.Description))
                {
                    continue;
                }

                string value = Sanitize(tag.Description, MaximumTagValueLength);
                if (value.Length == 0)
                {
                    continue;
                }

                tags.Add(new PhotoMetadataTag(
                    Sanitize(directory.Name, MaximumDirectoryLength),
                    Sanitize(tag.Name, MaximumTagNameLength),
                    value));
            }
        }

        return tags;
    }

    private static bool SafeForSnapshot(string directoryName, string tagName)
    {
        if (directoryName.Contains("thumbnail", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !tagName.Contains("makernote", StringComparison.OrdinalIgnoreCase) &&
               !tagName.Contains("thumbnail data", StringComparison.OrdinalIgnoreCase) &&
               !tagName.Contains("preview image", StringComparison.OrdinalIgnoreCase) &&
               !tagName.Contains("image data", StringComparison.OrdinalIgnoreCase) &&
               !tagName.Contains("intercolor profile", StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string value, int maximumLength)
    {
        StringBuilder builder = new(Math.Min(value.Length, maximumLength));
        bool pendingSpace = false;
        foreach (char character in value.Trim())
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace && builder.Length < maximumLength)
            {
                builder.Append(' ');
            }
            pendingSpace = false;

            if (builder.Length >= maximumLength)
            {
                break;
            }
            builder.Append(character);
        }

        return builder.ToString();
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
