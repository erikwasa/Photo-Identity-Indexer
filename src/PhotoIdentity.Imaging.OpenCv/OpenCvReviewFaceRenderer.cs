using OpenCvSharp;
using PhotoIdentity.Core.Geometry;

namespace PhotoIdentity.Imaging.OpenCv;

public sealed record EncodedReviewFace(
    byte[] Content,
    string ContentType,
    int Width,
    int Height);

/// <summary>
/// Renders a face-centered JPEG from an already privacy-safe review proxy.
/// The face receives surrounding context for human review and pixels are never upscaled.
/// </summary>
public sealed class OpenCvReviewFaceRenderer
{
    public const double ContextScale = 2.2d;
    private const int JpegQuality = 90;

    public async Task<EncodedReviewFace?> RenderAsync(
        string path,
        NormalizedBoundingBox boundingBox,
        int maximumEdge,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEdge);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] sourceBytes;
        try
        {
            sourceBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return null;
        }

        try
        {
            using Mat source = Cv2.ImDecode(sourceBytes, ImreadModes.Color);
            if (source.Empty())
            {
                return null;
            }

            Rect crop = CalculateCrop(source.Cols, source.Rows, boundingBox);
            using Mat cropped = new(source, crop);

            double scale = Math.Min(1d, (double)maximumEdge / Math.Max(cropped.Cols, cropped.Rows));
            int targetWidth = Math.Max(
                1,
                (int)Math.Round(cropped.Cols * scale, MidpointRounding.AwayFromZero));
            int targetHeight = Math.Max(
                1,
                (int)Math.Round(cropped.Rows * scale, MidpointRounding.AwayFromZero));

            using Mat prepared = new();
            if (scale < 1d)
            {
                Cv2.Resize(
                    cropped,
                    prepared,
                    new Size(targetWidth, targetHeight),
                    interpolation: InterpolationFlags.Area);
            }
            else
            {
                cropped.CopyTo(prepared);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Cv2.ImEncode(
                ".jpg",
                prepared,
                out byte[] encoded,
                new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality));
            if (encoded.Length == 0)
            {
                return null;
            }

            return new EncodedReviewFace(
                encoded,
                "image/jpeg",
                prepared.Cols,
                prepared.Rows);
        }
        catch (OpenCVException)
        {
            return null;
        }
    }

    private static Rect CalculateCrop(
        int imageWidth,
        int imageHeight,
        NormalizedBoundingBox boundingBox)
    {
        double faceX = boundingBox.X * imageWidth;
        double faceY = boundingBox.Y * imageHeight;
        double faceWidth = boundingBox.Width * imageWidth;
        double faceHeight = boundingBox.Height * imageHeight;
        double centerX = faceX + (faceWidth / 2d);
        double centerY = faceY + (faceHeight / 2d);
        double side = Math.Min(
            Math.Min(imageWidth, imageHeight),
            Math.Max(faceWidth, faceHeight) * ContextScale);

        side = Math.Max(1d, side);
        double left = Math.Clamp(centerX - (side / 2d), 0d, Math.Max(0d, imageWidth - side));
        double top = Math.Clamp(centerY - (side / 2d), 0d, Math.Max(0d, imageHeight - side));

        int x = Math.Clamp((int)Math.Floor(left), 0, imageWidth - 1);
        int y = Math.Clamp((int)Math.Floor(top), 0, imageHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling(left + side), x + 1, imageWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(top + side), y + 1, imageHeight);
        return new Rect(x, y, right - x, bottom - y);
    }
}
