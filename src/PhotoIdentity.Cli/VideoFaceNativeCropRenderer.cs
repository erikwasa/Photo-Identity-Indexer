using System.Runtime.InteropServices;
using OpenCvSharp;
using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.Cli;

public sealed record VideoFaceNativeCropBox(int X, int Y, int Width, int Height);
public sealed record VideoFaceNativeCropJpeg(byte[] Bytes, int Width, int Height);

/// <summary>
/// Render a private, single-face native-pixel JPEG from an already decoded
/// source-attested frame. The face is geometrically selected by immutable
/// PostgreSQL observation coordinates, never by client-supplied crop bounds.
/// No resize, upscaling, media path, EXIF or frame persistence.
/// </summary>
public static class VideoFaceNativeCropRenderer
{
    public const int MaximumEncodedBytes = 2 * 1024 * 1024;
    private const int JpegQuality = 86;

    public static VideoFaceNativeCropBox CalculateBox(
        int nativeWidth, int nativeHeight,
        double x, double y, double width, double height)
    {
        if (nativeWidth is < 1 or > VideoFaceNativeFrameProfile.MaximumEdge ||
            nativeHeight is < 1 or > VideoFaceNativeFrameProfile.MaximumEdge ||
            !double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(width) || !double.IsFinite(height) ||
            x < 0 || y < 0 || width <= 0 || height <= 0 ||
            x >= 1 || y >= 1 || x + width > 1.00001 ||
            y + height > 1.00001)
            throw new InvalidDataException("Untrusted native face observation geometry.");

        int left = Math.Clamp((int)Math.Floor(x * nativeWidth), 0, nativeWidth - 1);
        int top = Math.Clamp((int)Math.Floor(y * nativeHeight), 0, nativeHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling((x + width) * nativeWidth),
            left + 1, nativeWidth);
        int bottom = Math.Clamp((int)Math.Ceiling((y + height) * nativeHeight),
            top + 1, nativeHeight);

        // Small bounded native-pixel context helps review, but cannot fabricate
        // detail through enlargement. Clamp to the actual decoded frame.
        int padding = Math.Min(64, (int)Math.Round(
            Math.Min(right - left, bottom - top) * 0.15,
            MidpointRounding.AwayFromZero));
        left = Math.Max(0, left - padding);
        top = Math.Max(0, top - padding);
        right = Math.Min(nativeWidth, right + padding);
        bottom = Math.Min(nativeHeight, bottom + padding);
        return new(left, top, right - left, bottom - top);
    }

    public static VideoFaceNativeCropJpeg Render(
        ImageFrame nativeFrame, int expectedWidth, int expectedHeight,
        double x, double y, double width, double height,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(nativeFrame);
        token.ThrowIfCancellationRequested();
        VideoFaceNativeFrameProfile.ValidateDecodedImage(nativeFrame);
        if (nativeFrame.Size.Width != expectedWidth ||
            nativeFrame.Size.Height != expectedHeight)
            throw new InvalidDataException(
                "Decoded source frame geometry differs from the published face observation.");

        VideoFaceNativeCropBox box = CalculateBox(
            expectedWidth, expectedHeight, x, y, width, height);
        byte[] pixels = nativeFrame.ToArray();
        try
        {
            using Mat frame = new(expectedHeight, expectedWidth, MatType.CV_8UC3);
            Marshal.Copy(pixels, 0, frame.Data, pixels.Length);
            using Mat crop = new(frame,
                new Rect(box.X, box.Y, box.Width, box.Height));
            token.ThrowIfCancellationRequested();
            Cv2.ImEncode(".jpg", crop, out byte[] encoded,
                new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality));
            if (encoded.Length is < 100 or > MaximumEncodedBytes)
                throw new InvalidDataException(
                    "The private native face crop exceeds the bounded JPEG contract.");
            return new(encoded, box.Width, box.Height);
        }
        catch (OpenCVException)
        {
            throw new InvalidDataException(
                "The private native face crop could not be rendered.");
        }
        finally
        {
            Array.Clear(pixels);
        }
    }
}
