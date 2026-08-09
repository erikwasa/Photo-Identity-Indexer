using System.Runtime.InteropServices;
using OpenCvSharp;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

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
            ImageFrame image = OpenCvImageDecoder.DecodeEncoded(
                sourceBytes,
                new DecodeOptions(new ImageSize(profile.MaximumLongEdge, profile.MaximumLongEdge)),
                cancellationToken);
            if (image.Format != PixelFormat.Bgr24 ||
                image.Stride != checked(image.Size.Width * ImageFrame.BytesPerPixel(PixelFormat.Bgr24)))
            {
                throw new InvalidDataException("The review-proxy decoder did not return packed BGR24 pixels.");
            }

            byte[] pixels = image.ToArray();
            using Mat prepared = new(image.Size.Height, image.Size.Width, MatType.CV_8UC3);
            Marshal.Copy(pixels, 0, prepared.Data, pixels.Length);

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
                image.Size.Width,
                image.Size.Height);
        }
        catch (ImageDecodingException exception)
        {
            throw new InvalidDataException("The source image could not be decoded for the review proxy.", exception);
        }
        catch (OpenCVException exception)
        {
            throw new InvalidDataException("OpenCV could not render the review proxy.", exception);
        }
    }
}
