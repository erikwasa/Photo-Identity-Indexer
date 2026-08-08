using OpenCvSharp;
using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.Imaging.OpenCv;

public sealed record EncodedReviewProxy(
    byte[] Content,
    string ContentType,
    int Width,
    int Height);

/// <summary>
/// Renders a metadata-free JPEG review derivative with exact versioned settings.
/// The source is never modified and images are never upscaled.
/// </summary>
public sealed class OpenCvReviewProxyRenderer
{
    public async Task<EncodedReviewProxy> RenderAsync(
        string path,
        ReviewProxyProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] sourceBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return Render(sourceBytes, profile, cancellationToken);
    }

    public EncodedReviewProxy Render(
        ReadOnlySpan<byte> sourceBytes,
        ReviewProxyProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceBytes.IsEmpty)
        {
            throw new InvalidDataException("The source image is empty.");
        }

        try
        {
            using Mat source = Cv2.ImDecode(sourceBytes.ToArray(), ImreadModes.Color);
            if (source.Empty())
            {
                throw new InvalidDataException("The source image could not be decoded as JPEG or PNG content.");
            }

            double scale = Math.Min(
                1d,
                (double)profile.MaximumLongEdge / Math.Max(source.Cols, source.Rows));
            int width = Math.Max(
                1,
                (int)Math.Round(source.Cols * scale, MidpointRounding.AwayFromZero));
            int height = Math.Max(
                1,
                (int)Math.Round(source.Rows * scale, MidpointRounding.AwayFromZero));

            using Mat prepared = new();
            if (scale < 1d)
            {
                Cv2.Resize(
                    source,
                    prepared,
                    new Size(width, height),
                    interpolation: InterpolationFlags.Area);
            }
            else
            {
                source.CopyTo(prepared);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Cv2.ImEncode(
                ".jpg",
                prepared,
                out byte[] encoded,
                new ImageEncodingParam(ImwriteFlags.JpegQuality, profile.JpegQuality));
            if (encoded.Length == 0)
            {
                throw new InvalidDataException("OpenCV produced an empty review proxy.");
            }

            return new EncodedReviewProxy(
                encoded,
                ReviewProxyProfile.ContentType,
                width,
                height);
        }
        catch (OpenCVException exception)
        {
            throw new InvalidDataException("OpenCV could not render the review proxy.", exception);
        }
    }
}
