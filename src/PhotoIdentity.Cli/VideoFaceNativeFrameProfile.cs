using System.Globalization;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Cli;

/// <summary>
/// Native pixel-size, bounded, SDR video-face frame extraction. Unlike
/// CLIP frame evidence, this profile NEVER scales video frames to 640 px:
/// the original detected face pixels must be preserved for YuNet/SFace.
///
/// This is an argument builder and validated decoder, not source
/// authorization, a worker, an API endpoint or permission to run FFmpeg.
/// </summary>
public static class VideoFaceNativeFrameProfile
{
    public const string Version = VideoFaceEvidenceLeaseIdentity.PresetVersion;
    public const int MaximumEdge = 3840;
    public const long MaximumJpegBytes = 12L * 1024 * 1024;
    public const string OutputName = "native-face-frame.jpg";

    public static IReadOnlyList<string> BuildArguments(
        string stagedLocalFile, string workspace,
        VideoFrameSample frame, long durationMilliseconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedLocalFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentNullException.ThrowIfNull(frame);
        if (!Path.IsPathFullyQualified(stagedLocalFile) ||
            !Path.IsPathFullyQualified(workspace) ||
            (OperatingSystem.IsWindows() &&
             (stagedLocalFile.StartsWith(@"\\", StringComparison.Ordinal) ||
              workspace.StartsWith(@"\\", StringComparison.Ordinal))) ||
            durationMilliseconds is < 200 or > 3_600_000 ||
            frame.TimestampMilliseconds < 0 ||
            frame.TimestampMilliseconds >= durationMilliseconds)
            throw new ArgumentException(
                "Native face extraction requires a staged local video and bounded timestamp.");

        // Output is presentation-oriented (autorotate). No scale, pad, crop
        // or upsample filter is permitted. Native coded sizes above the
        // bounded decoder contract fail closed rather than being resized.
        return [
            "-nostdin", "-hide_banner", "-loglevel", "error",
            "-protocol_whitelist", "file", "-threads", "1",
            "-filter_threads", "1", "-autorotate",
            "-i", stagedLocalFile,
            "-ss", (frame.TimestampMilliseconds / 1_000m)
                .ToString("0.000", CultureInfo.InvariantCulture),
            "-map", "0:v:0", "-frames:v", "1",
            "-an", "-sn", "-dn", "-vf", "format=yuv420p",
            "-q:v", "3", "-f", "image2",
            "-fs", MaximumJpegBytes.ToString(CultureInfo.InvariantCulture),
            "-y", Path.Combine(workspace, OutputName)
        ];
    }

    public static async Task<ImageFrame> ReadValidatedFrameAsync(
        string outputPath, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        FileInfo info = new(outputPath);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 ||
            info.Length is < 100 or > MaximumJpegBytes)
            throw new InvalidDataException("Native video frame is missing, linked or oversized.");

        byte[] jpeg = await File.ReadAllBytesAsync(outputPath, token);
        if (jpeg.LongLength is < 100 or > MaximumJpegBytes)
            throw new InvalidDataException("Native video JPEG changed during reading.");
        ImageSize declared = ReadBoundedJpegSize(jpeg);
        // IMPORTANT: DecodeOptions.MaximumSize causes automatic downscaling
        // in the shared OpenCV decoder. Never supply it for native faces.
        // The JPEG SOF preflight bounds pixel allocation BEFORE decoding.
        using MemoryStream input = new(jpeg, writable: false);
        ImageFrame image = await new OpenCvImageDecoder().DecodeAsync(
            input, new DecodeOptions(), token);
        ValidateDecodedImage(image);
        if (image.Size != declared)
            throw new InvalidDataException(
                "Native video JPEG dimensions changed during decoding.");
        return image;
    }

    /// <summary>
    /// Reject huge JPEG dimensions BEFORE OpenCV allocates a bitmap. Do not
    /// downscale an oversized frame as the ordinary photo decoder would.
    /// Inspect only JPEG structure, not a user-supplied path or other media.
    /// </summary>
    public static ImageSize ReadBoundedJpegSize(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 10 || jpeg[0] != 0xff || jpeg[1] != 0xd8)
            throw new InvalidDataException("Native frame lacks a JPEG start marker.");

        int offset = 2;
        while (offset + 3 < jpeg.Length)
        {
            if (jpeg[offset++] != 0xff)
                throw new InvalidDataException("Invalid JPEG segment boundary.");
            while (offset < jpeg.Length && jpeg[offset] == 0xff)
                offset++;
            if (offset >= jpeg.Length)
                break;
            int marker = jpeg[offset++];
            if (marker == 0xda || marker == 0xd9)
                break; // No SOF before scan or end.
            if (marker == 0x01 || marker is >= 0xd0 and <= 0xd7)
                continue;
            if (offset + 1 >= jpeg.Length)
                break;
            int length = jpeg[offset] * 256 + jpeg[offset + 1];
            if (length < 2 || length > jpeg.Length - offset)
                throw new InvalidDataException("Invalid JPEG segment length.");
            bool startOfFrame =
                marker is >= 0xc0 and <= 0xc3 or
                    >= 0xc5 and <= 0xc7 or
                    >= 0xc9 and <= 0xcb or
                    >= 0xcd and <= 0xcf;
            if (startOfFrame)
            {
                if (length < 7)
                    throw new InvalidDataException("Invalid JPEG frame header.");
                int height = jpeg[offset + 3] * 256 + jpeg[offset + 4];
                int width = jpeg[offset + 5] * 256 + jpeg[offset + 6];
                if (width is < 1 or > MaximumEdge ||
                    height is < 1 or > MaximumEdge)
                    throw new InvalidDataException(
                        "Native video frame exceeds maximum pixel geometry; no resizing permitted.");
                return new ImageSize(width, height);
            }
            offset += length;
        }
        throw new InvalidDataException("Native video JPEG is missing dimensions.");
    }

    public static void ValidateDecodedImage(ImageFrame image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Size.Width is < 1 or > MaximumEdge ||
            image.Size.Height is < 1 or > MaximumEdge ||
            image.Format != PixelFormat.Bgr24 ||
            image.Stride != checked(image.Size.Width * 3))
            throw new InvalidDataException(
                "Video face input must be bounded native BGR24 pixels with packed stride.");
    }
}
