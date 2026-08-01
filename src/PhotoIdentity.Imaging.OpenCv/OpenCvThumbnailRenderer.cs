using OpenCvSharp;

namespace PhotoIdentity.Imaging.OpenCv;

public sealed record EncodedThumbnail(
    byte[] Content,
    string ContentType,
    int Width,
    int Height);

/// <summary>
/// Produces a small, fixed-size JPEG preview while keeping original image paths and bytes server-side.
/// </summary>
public sealed class OpenCvThumbnailRenderer
{
    public const int ThumbnailWidth = 480;
    public const int ThumbnailHeight = 320;
    private const int JpegQuality = 82;

    public async Task<EncodedThumbnail?> RenderAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
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

            double scale = Math.Min(
                1d,
                Math.Min(
                    (double)ThumbnailWidth / source.Cols,
                    (double)ThumbnailHeight / source.Rows));
            int resizedWidth = Math.Max(
                1,
                (int)Math.Round(source.Cols * scale, MidpointRounding.AwayFromZero));
            int resizedHeight = Math.Max(
                1,
                (int)Math.Round(source.Rows * scale, MidpointRounding.AwayFromZero));

            using Mat resized = new();
            Cv2.Resize(
                source,
                resized,
                new Size(resizedWidth, resizedHeight),
                interpolation: scale < 1d ? InterpolationFlags.Area : InterpolationFlags.Linear);

            using Mat canvas = new(
                new Size(ThumbnailWidth, ThumbnailHeight),
                MatType.CV_8UC3,
                new Scalar(44, 47, 47));
            int left = (ThumbnailWidth - resizedWidth) / 2;
            int top = (ThumbnailHeight - resizedHeight) / 2;
            using Mat target = new(canvas, new Rect(left, top, resizedWidth, resizedHeight));
            resized.CopyTo(target);

            cancellationToken.ThrowIfCancellationRequested();
            bool encoded = Cv2.ImEncode(
                ".jpg",
                canvas,
                out byte[] thumbnail,
                new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality));
            if (!encoded || thumbnail.Length == 0)
            {
                return null;
            }

            return new EncodedThumbnail(
                thumbnail,
                "image/jpeg",
                ThumbnailWidth,
                ThumbnailHeight);
        }
        catch (OpenCVException)
        {
            return null;
        }
    }
}
