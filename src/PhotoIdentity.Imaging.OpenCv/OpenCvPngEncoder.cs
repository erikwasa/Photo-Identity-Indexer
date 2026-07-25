using System.Runtime.InteropServices;
using OpenCvSharp;
using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.Imaging.OpenCv;

public sealed class OpenCvPngEncoder
{
    public async Task EncodeAsync(
        ImageFrame image,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        }

        if (image.Format != PixelFormat.Bgr24)
        {
            throw new NotSupportedException(
                $"PNG verification output currently supports only {PixelFormat.Bgr24} frames.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        byte[] packed = PackRows(image);
        using Mat matrix = new(image.Size.Height, image.Size.Width, MatType.CV_8UC3);
        Marshal.Copy(packed, 0, matrix.Data, packed.Length);

        if (!Cv2.ImEncode(".png", matrix, out byte[] encoded))
        {
            throw new InvalidOperationException("OpenCV could not encode the verification PNG.");
        }

        await destination.WriteAsync(encoded, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static byte[] PackRows(ImageFrame image)
    {
        int packedStride = checked(image.Size.Width * ImageFrame.BytesPerPixel(image.Format));
        byte[] packed = new byte[checked(packedStride * image.Size.Height)];
        ReadOnlySpan<byte> source = image.Data;

        for (int row = 0; row < image.Size.Height; row++)
        {
            source.Slice(row * image.Stride, packedStride)
                .CopyTo(packed.AsSpan(row * packedStride, packedStride));
        }

        return packed;
    }
}
