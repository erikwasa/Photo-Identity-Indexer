using System.Globalization;
using System.Text;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Xmp;
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
        XmpDirectory? xmp = directories.OfType<XmpDirectory>().FirstOrDefault();
        IReadOnlyDictionary<string, string> xmpProperties = XmpProperties(xmp);

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
        else if (TryGetXmpDate(xmpProperties, out DateTime xmpDate, out TimeSpan? xmpOffset))
        {
            takenAtLocal = xmpDate;
            utcOffset = xmpOffset;
        }

        GeoLocation? location = gps?.GetGeoLocation();
        IReadOnlyList<PhotoMetadataTag> rawTags = CaptureRawTags(directories, xmpProperties, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new PhotoCaptureMetadata(
            takenAtLocal,
            utcOffset,
            location?.Latitude,
            location?.Longitude,
            FirstNonEmpty(
                Description(ifd0, ExifDirectoryBase.TagMake),
                XmpValue(xmpProperties, "tiff:Make")),
            FirstNonEmpty(
                Description(ifd0, ExifDirectoryBase.TagModel),
                XmpValue(xmpProperties, "tiff:Model")),
            FirstNonEmpty(
                Description(exif, ExifDirectoryBase.TagLensModel),
                XmpValue(xmpProperties, "aux:Lens"),
                XmpValue(xmpProperties, "exifEX:LensModel")),
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

    private static bool TryGetXmpDate(
        IReadOnlyDictionary<string, string> properties,
        out DateTime takenAtLocal,
        out TimeSpan? utcOffset)
    {
        takenAtLocal = default;
        utcOffset = null;
        string? raw = FirstNonEmpty(
            XmpValue(properties, "exif:DateTimeOriginal"),
            XmpValue(properties, "photoshop:DateCreated"),
            XmpValue(properties, "xmp:CreateDate"));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string value = raw.Trim();
        bool hasExplicitOffset = value.EndsWith('Z') || HasTrailingOffset(value);
        if (hasExplicitOffset && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset timestamp))
        {
            takenAtLocal = DateTime.SpecifyKind(timestamp.DateTime, DateTimeKind.Unspecified);
            utcOffset = timestamp.Offset;
            return true;
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime parsed))
        {
            takenAtLocal = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            return true;
        }

        return false;
    }

    private static bool HasTrailingOffset(string value)
    {
        if (value.Length < 6)
        {
            return false;
        }

        int start = value.Length - 6;
        return (value[start] == '+' || value[start] == '-') &&
               char.IsDigit(value[start + 1]) &&
               char.IsDigit(value[start + 2]) &&
               value[start + 3] == ':' &&
               char.IsDigit(value[start + 4]) &&
               char.IsDigit(value[start + 5]);
    }

    private static string? Description(MetadataExtractor.Directory? directory, int tagType)
    {
        string? value = directory?.GetDescription(tagType);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : Sanitize(value, MaximumTagValueLength);
    }

    private static IReadOnlyDictionary<string, string> XmpProperties(XmpDirectory? directory)
    {
        if (directory is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(directory.GetXmpProperties(), StringComparer.OrdinalIgnoreCase);
    }

    private static string? XmpValue(IReadOnlyDictionary<string, string> properties, string key) =>
        properties.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? Sanitize(value, MaximumTagValueLength)
            : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<PhotoMetadataTag> CaptureRawTags(
        IReadOnlyList<MetadataExtractor.Directory> directories,
        IReadOnlyDictionary<string, string> xmpProperties,
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

        foreach ((string key, string value) in xmpProperties.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tags.Count >= MaximumRawTags)
            {
                break;
            }

            string safeName = Sanitize(key, MaximumTagNameLength);
            string safeValue = Sanitize(value, MaximumTagValueLength);
            if (safeName.Length == 0 || safeValue.Length == 0 || !SafeForSnapshot("XMP", safeName))
            {
                continue;
            }

            tags.Add(new PhotoMetadataTag("XMP", safeName, safeValue));
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
