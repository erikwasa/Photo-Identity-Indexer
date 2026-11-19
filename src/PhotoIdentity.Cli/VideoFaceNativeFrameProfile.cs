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
    public const string Version = "wi0155-native-sdr-face-jpeg-v1";
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

        await using FileStream input = new(outputPath, FileMode.Open,
            FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] header = new byte[3];
        if (await input.ReadAsync(header, token) != 3 ||
            header[0] != 0xff || header[1] != 0xd8 || header[2] != 0xff)
            throw new InvalidDataException("Native video frame must be JPEG.");
        input.Position = 0;
        ImageFrame image = await new OpenCvImageDecoder().DecodeAsync(
            input, new DecodeOptions(new ImageSize(MaximumEdge, MaximumEdge)), token);
        ValidateDecodedImage(image);
        return image;
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
