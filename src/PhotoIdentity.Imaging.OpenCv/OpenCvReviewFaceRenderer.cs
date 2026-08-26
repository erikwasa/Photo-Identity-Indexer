using System.Runtime.InteropServices;
using OpenCvSharp;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Imaging.OpenCv;

public sealed record EncodedReviewFace(
    byte[] Content,
    string ContentType,
    int Width,
    int Height);

/// <summary>
/// Renders face-centered JPEG review derivatives from decoded source pixels. The face receives
/// surrounding context for human review and pixels are never upscaled.
/// </summary>
public sealed class OpenCvReviewFaceRenderer
{
    public const double ContextScale = 2.2d;
    public const int JpegQuality = 90;

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

        return Render(sourceBytes, boundingBox, maximumEdge, cancellationToken);
    }

    public async Task<EncodedReviewFace?> RenderAsync(
        Stream source,
        NormalizedBoundingBox boundingBox,
        int maximumEdge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEdge);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using MemoryStream buffer = new();
            await source.CopyToAsync(buffer, cancellationToken);
            return Render(buffer.ToArray(), boundingBox, maximumEdge, cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ObjectDisposedException)
        {
            return null;
        }
    }

    public EncodedReviewFace? Render(
        ReadOnlySpan<byte> sourceBytes,
        NormalizedBoundingBox boundingBox,
        int maximumEdge,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EncodedReviewFace?> rendered = RenderMany(
            sourceBytes,
            [boundingBox],
            maximumEdge,
            cancellationToken);
        return rendered[0];
    }

    public IReadOnlyList<EncodedReviewFace?> RenderMany(
        ReadOnlySpan<byte> sourceBytes,
        IReadOnlyList<NormalizedBoundingBox> boundingBoxes,
        int maximumEdge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boundingBoxes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEdge);
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceBytes.IsEmpty)
        {
            return Enumerable.Repeat<EncodedReviewFace?>(null, boundingBoxes.Count).ToArray();
        }

        try
        {
            ImageFrame image = OpenCvImageDecoder.DecodeEncoded(
                sourceBytes,
                new DecodeOptions(),
                cancellationToken);
            if (image.Format != PixelFormat.Bgr24 ||
                image.Stride != checked(image.Size.Width * ImageFrame.BytesPerPixel(PixelFormat.Bgr24)))
            {
                return Enumerable.Repeat<EncodedReviewFace?>(null, boundingBoxes.Count).ToArray();
            }

            byte[] pixels = image.ToArray();
            using Mat source = new(image.Size.Height, image.Size.Width, MatType.CV_8UC3);
            Marshal.Copy(pixels, 0, source.Data, pixels.Length);

            EncodedReviewFace?[] results = new EncodedReviewFace?[boundingBoxes.Count];
            for (int index = 0; index < boundingBoxes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[index] = RenderDecoded(source, boundingBoxes[index], maximumEdge, cancellationToken);
            }

            return results;
        }
        catch (ImageDecodingException)
        {
            return Enumerable.Repeat<EncodedReviewFace?>(null, boundingBoxes.Count).ToArray();
        }
        catch (OpenCVException)
        {
            return Enumerable.Repeat<EncodedReviewFace?>(null, boundingBoxes.Count).ToArray();
        }
    }

    public static NormalizedBoundingBox CalculateTargetBoundingBox(
        int imageWidth,
        int imageHeight,
        NormalizedBoundingBox boundingBox)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);

        Rect crop = CalculateCrop(imageWidth, imageHeight, boundingBox);
        double faceLeft = boundingBox.X * imageWidth;
        double faceTop = boundingBox.Y * imageHeight;
        double faceRight = faceLeft + (boundingBox.Width * imageWidth);
        double faceBottom = faceTop + (boundingBox.Height * imageHeight);

        double left = Math.Clamp(faceLeft - crop.X, 0d, crop.Width);
        double top = Math.Clamp(faceTop - crop.Y, 0d, crop.Height);
        double right = Math.Clamp(faceRight - crop.X, left, crop.Width);
        double bottom = Math.Clamp(faceBottom - crop.Y, top, crop.Height);

        return new NormalizedBoundingBox(
            left / crop.Width,
            top / crop.Height,
            (right - left) / crop.Width,
            (bottom - top) / crop.Height);
    }

    private static EncodedReviewFace? RenderDecoded(
        Mat source,
        NormalizedBoundingBox boundingBox,
        int maximumEdge,
        CancellationToken cancellationToken)
    {
        try
        {
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
