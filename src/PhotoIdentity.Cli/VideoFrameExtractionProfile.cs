using System.Globalization;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity.Cli;

/// <summary>
/// Fixed SDR-only JPEG extraction profile for a separately authorized staged
/// local copy. This builder does NOT authorize a source or start FFmpeg.
/// </summary>
public static class VideoFrameExtractionProfile
{
    public const string Version = "wi0190-sdr-frame-jpeg-v1";
    public const int MaximumLongEdge = 640;
    public const long MaximumJpegBytes = 2L * 1024 * 1024;
    public const string OutputName = "output.jpg";
    public const string VideoFilter =
        "scale=640:640:force_original_aspect_ratio=decrease,format=yuv420p";

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
            throw new ArgumentException("Frame extraction requires bounded local staged input and timestamp.");

        // Seek AFTER input for accurate presentation-time decoding. --autorotate
        // is deliberate; no model should see unrotated camera storage pixels.
        // FFmpeg also imposes a file-size cap, independently rechecked on read.
        return [
            "-nostdin", "-hide_banner", "-loglevel", "error",
            "-protocol_whitelist", "file", "-threads", "1",
            "-filter_threads", "1", "-autorotate",
            "-i", stagedLocalFile,
            "-ss", (frame.TimestampMilliseconds / 1_000m)
                .ToString("0.000", CultureInfo.InvariantCulture),
            "-map", "0:v:0", "-frames:v", "1",
            "-an", "-sn", "-dn", "-vf", VideoFilter,
            "-q:v", "4", "-f", "image2",
            "-fs", MaximumJpegBytes.ToString(CultureInfo.InvariantCulture),
            "-y", Path.Combine(workspace, OutputName)
        ];
    }

    /// <summary>
    /// Reuses the SAME still-photo JPEG -> BGR24 decoding path as other photo
    /// analysis. This is compatibility at the decoded image boundary, not
    /// proof that a CLIP model or caption model produces meaningful output.
    /// Never process unbounded or linked/unverified frame output.
    /// </summary>
    public static async Task<ImageFrame> ReadValidatedFrameAsync(
        string outputPath, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        FileInfo info = new(outputPath);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            info.Length is < 100 or > MaximumJpegBytes)
            throw new InvalidDataException("Extracted video frame is missing, linked or oversized.");

        byte[] header = new byte[3];
        await using FileStream input = new(outputPath, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.SequentialScan | FileOptions.Asynchronous);
        if (await input.ReadAsync(header, token) != 3 ||
            header[0] != 0xff || header[1] != 0xd8 || header[2] != 0xff)
            throw new InvalidDataException("Extracted video frame is not a JPEG.");
        input.Position = 0;
        ImageFrame image = await new OpenCvImageDecoder().DecodeAsync(
            input, new DecodeOptions(), token);
        if (image.Size.Width is < 1 or > MaximumLongEdge ||
            image.Size.Height is < 1 or > MaximumLongEdge ||
            image.Format != PixelFormat.Bgr24 ||
            image.Stride != image.Size.Width * 3)
            throw new InvalidDataException("Extracted frame violates the still-image model input contract.");
        return image;
    }
}
