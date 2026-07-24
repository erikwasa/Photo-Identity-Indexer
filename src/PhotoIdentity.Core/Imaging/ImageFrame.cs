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
        PixelFormat = pixelFormat;
        Stride = stride;
        _data = data.ToArray();
    }

    public ImageSize Size { get; }
    public PixelFormat PixelFormat { get; }
    public int Stride { get; }
    public ReadOnlySpan<byte> Data => _data;

    public byte[] ToArray() => (byte[])_data.Clone();

    public static int BytesPerPixel(PixelFormat pixelFormat) => pixelFormat switch
    {
        PixelFormat.Gray8 => 1,
        PixelFormat.Rgb24 or PixelFormat.Bgr24 => 3,
        PixelFormat.Rgba32 or PixelFormat.Bgra32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat)),
    };
}
