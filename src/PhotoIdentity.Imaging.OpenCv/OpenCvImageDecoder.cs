using ImageMagick;
using OpenCvSharp;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Imaging.OpenCv;

public sealed class OpenCvImageDecoder : IImageDecoder
{
    private const int CopyBufferSize = 64 * 1024;

    public async Task<ImageFrame> DecodeAsync(
        Stream source,
        DecodeOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] encoded = await ReadAllAsync(source, cancellationToken);
        return DecodeEncoded(encoded, options, cancellationToken);
    }

    internal static ImageFrame DecodeEncoded(
        ReadOnlySpan<byte> encoded,
        DecodeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        ImageFileFormat format = ImageFileSignature.Detect(encoded);
        if (format == ImageFileFormat.Unsupported)
        {
            throw new ImageDecodingException(
                ImageDecodingFailure.UnsupportedFormat,
                "Only JPEG, PNG, HEIC and HEIF images are supported by the image decoder.");
        }

        try
        {
            using Mat decoded = format == ImageFileFormat.Heif
                ? DecodeHeif(encoded)
                : Cv2.ImDecode(encoded.ToArray(), ImreadModes.Color);
            if (decoded.Empty())
            {
                throw new ImageDecodingException(
                    ImageDecodingFailure.CorruptMedia,
                    $"The {format} image could not be decoded.");
            }

            using Mat prepared = ResizeToMaximum(decoded, options.MaximumSize);
            return ToImageFrame(prepared);
        }
        catch (ImageDecodingException)
        {
            throw;
        }
        catch (MagickException exception)
        {
            throw new ImageDecodingException(
                ImageDecodingFailure.CorruptMedia,
                $"The {format} image could not be decoded.",
                exception);
        }
        catch (OpenCVException exception)
        {
            throw new ImageDecodingException(
                ImageDecodingFailure.CorruptMedia,
                $"The {format} image could not be decoded.",
                exception);
        }
    }

    private static Mat DecodeHeif(ReadOnlySpan<byte> encoded)
    {
        using MagickImage image = new(encoded.ToArray());
        image.AutoOrient();
        image.TransformColorSpace(ColorProfiles.SRGB);
        image.Strip();
        image.Format = MagickFormat.Bmp;
        byte[] bitmap = image.ToByteArray();
        return Cv2.ImDecode(bitmap, ImreadModes.Color);
    }

    private static async Task<byte[]> ReadAllAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await source.CopyToAsync(buffer, CopyBufferSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return buffer.ToArray();
    }

    private static Mat ResizeToMaximum(Mat source, ImageSize? maximumSize)
    {
        if (maximumSize is null ||
            (source.Cols <= maximumSize.Value.Width && source.Rows <= maximumSize.Value.Height))
        {
            return source.Clone();
        }

        double scale = Math.Min(
            (double)maximumSize.Value.Width / source.Cols,
            (double)maximumSize.Value.Height / source.Rows);

        int width = Math.Max(1, (int)Math.Round(source.Cols * scale, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(source.Rows * scale, MidpointRounding.AwayFromZero));

        Mat resized = new();
        Cv2.Resize(
            source,
            resized,
            new Size(width, height),
            interpolation: InterpolationFlags.Area);
        return resized;
    }

    private static ImageFrame ToImageFrame(Mat image)
    {
        if (image.Type() != MatType.CV_8UC3)
        {
            throw new InvalidOperationException(
                $"Expected an 8-bit three-channel image but received {image.Type()}.");
        }

        if (!image.GetArray<Vec3b>(out Vec3b[] pixels))
        {
            throw new InvalidOperationException("OpenCV could not copy the decoded pixel data.");
        }

        byte[] data = new byte[checked(pixels.Length * 3)];
        int offset = 0;
        foreach (Vec3b pixel in pixels)
        {
            data[offset++] = pixel.Item0;
            data[offset++] = pixel.Item1;
            data[offset++] = pixel.Item2;
        }

        ImageSize size = new(image.Cols, image.Rows);
        return new ImageFrame(
            size,
            PixelFormat.Bgr24,
            checked(size.Width * ImageFrame.BytesPerPixel(PixelFormat.Bgr24)),
            data);
    }
}

internal enum ImageFileFormat
{
    Unsupported,
    Jpeg,
    Png,
    Heif,
}

internal static class ImageFileSignature
{
    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static ImageFileFormat Detect(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length >= 3 &&
            encoded[0] == 0xff &&
            encoded[1] == 0xd8 &&
            encoded[2] == 0xff)
        {
            return ImageFileFormat.Jpeg;
        }

        if (encoded.StartsWith(PngSignature))
        {
            return ImageFileFormat.Png;
        }

        if (IsHeif(encoded))
        {
            return ImageFileFormat.Heif;
        }

        return ImageFileFormat.Unsupported;
    }

    private static bool IsHeif(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < 12 || !encoded.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return false;
        }

        if (IsHeifBrand(encoded.Slice(8, 4)))
        {
            return true;
        }

        int scanEnd = Math.Min(encoded.Length, 64);
        for (int offset = 16; offset + 4 <= scanEnd; offset += 4)
        {
            if (IsHeifBrand(encoded.Slice(offset, 4)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHeifBrand(ReadOnlySpan<byte> brand) =>
        brand.SequenceEqual("heic"u8) ||
        brand.SequenceEqual("heix"u8) ||
        brand.SequenceEqual("hevc"u8) ||
        brand.SequenceEqual("hevx"u8) ||
        brand.SequenceEqual("heim"u8) ||
        brand.SequenceEqual("heis"u8) ||
        brand.SequenceEqual("mif1"u8) ||
        brand.SequenceEqual("msf1"u8);
}
