using System.Text.Json;
using PhotoIdentity.Core.Geometry;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// For sampler-created native PNG manifests, prevent silent loss of source pixels
/// between the extracted file and the frame handed to face detection.
/// Absent sidecars retain compatibility with operator-supplied still manifests.
/// </summary>
public static class VideoFaceExtractionFidelity
{
    public static ImageSize? ExpectedNativeDimensions(string manifestDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);
        string path = Path.Combine(Path.GetFullPath(manifestDirectory),
            "extraction-metadata.json");
        if (!File.Exists(path))
        {
            return null;
        }

        FileInfo info = new(path);
        FileAttributes unsafeAttributes = FileAttributes.ReparsePoint | FileAttributes.Offline;
        if (info.Length is < 2 or > 8192 ||
            (info.Attributes & unsafeAttributes) != 0 ||
            ((long)info.Attributes & (0x00040000L | 0x00400000L)) != 0)
        {
            throw new ArgumentException("The private extraction metadata is not an approved local file.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new ArgumentException("Unsupported extraction metadata version.");
        }

        string? encoding = root.GetProperty("frameEncoding").GetString();
        if (encoding == "LegacyJpeg")
        {
            return null;
        }
        if (encoding != "NativeLosslessPng")
        {
            throw new ArgumentException("Unknown private extraction frame encoding.");
        }

        int width = root.GetProperty("codedWidth").GetInt32();
        int height = root.GetProperty("codedHeight").GetInt32();
        int rotation = root.GetProperty("rotationDegrees").GetInt32();
        int displayWidth = root.GetProperty("expectedDisplayWidth").GetInt32();
        int displayHeight = root.GetProperty("expectedDisplayHeight").GetInt32();
        if (width is < 1 or > 3840 || height is < 1 or > 3840 ||
            rotation is not (0 or 90 or -90 or 180 or -180) ||
            displayWidth != (Math.Abs(rotation) == 90 ? height : width) ||
            displayHeight != (Math.Abs(rotation) == 90 ? width : height))
        {
            throw new ArgumentException("The private extraction metadata has inconsistent dimensions.");
        }

        return new ImageSize(displayWidth, displayHeight);
    }

    public static void VerifyDecodedSize(ImageSize actual, ImageSize? expected)
    {
        if (expected is ImageSize required && actual != required)
        {
            throw new InvalidOperationException(
                "The decoded native-lossless frame does not match its approved source display dimensions.");
        }
    }
}
