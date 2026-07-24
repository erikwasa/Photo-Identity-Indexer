using PhotoIdentity.Core.Geometry;

namespace PhotoIdentity.Core.Imaging;

public enum PixelFormat
{
    Gray8,
    Rgb24,
    Bgr24,
    Rgba32,
    Bgra32,
}

public sealed class ImageFrame
{
    private readonly byte[] _data;

    public ImageFrame(ImageSize size, PixelFormat pixelFormat, int stride, ReadOnlySpan<byte> data)
    {
        int minimumStride = checked(size.Width * BytesPerPixel(pixelFormat));
        if (stride < minimumStride)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "Stride is smaller than the packed row size.");
        }

        int requiredLength = checked(stride * size.Height);
        if (data.Length != requiredLength)
        {
            throw new ArgumentException("Image data length must equal stride multiplied by height.", nameof(data));
        }

        Size = size;
        Format = pixelFormat;
        Stride = stride;
        _data = data.ToArray();
    }

    public ImageSize Size { get; }
    public PixelFormat Format { get; }
    public int Stride { get; }
    public ReadOnlySpan<byte> Data => _data;

    public byte[] ToArray() => (byte[])_data.Clone();

    public static int BytesPerPixel(PixelFormat pixelFormat) => pixelFormat switch
    {
        Imaging.PixelFormat.Gray8 => 1,
        Imaging.PixelFormat.Rgb24 or Imaging.PixelFormat.Bgr24 => 3,
        Imaging.PixelFormat.Rgba32 or Imaging.PixelFormat.Bgra32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat)),
    };
}
