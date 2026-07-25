using OpenCvSharp;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;

namespace PhotoIdentity_Recognition_Tests;

public sealed class ImageDecoderTests
{
    private readonly OpenCvImageDecoder _decoder = new();

    [Fact]
    public async Task Decode_png_returns_packed_bgr_pixels()
    {
        using Mat source = new(2, 3, MatType.CV_8UC3, new Scalar(10, 20, 30));
        byte[] encoded = Encode(source, ".png");
        using MemoryStream stream = new(encoded);

        ImageFrame result = await _decoder.DecodeAsync(
            stream,
            new DecodeOptions(),
            CancellationToken.None);

        Assert.Equal(new ImageSize(3, 2), result.Size);
        Assert.Equal(PixelFormat.Bgr24, result.Format);
        Assert.Equal(9, result.Stride);
        Assert.Equal(new byte[] { 10, 20, 30 }, result.Data[..3].ToArray());
    }

    [Fact]
    public async Task Decode_jpeg_applies_exif_orientation()
    {
        using Mat source = new(20, 40, MatType.CV_8UC3, new Scalar(10, 20, 30));
        byte[] jpeg = Encode(source, ".jpg");
        byte[] oriented = AddExifOrientation(jpeg, orientation: 6);
        using MemoryStream stream = new(oriented);

        ImageFrame result = await _decoder.DecodeAsync(
            stream,
            new DecodeOptions(),
            CancellationToken.None);

        Assert.Equal(new ImageSize(20, 40), result.Size);
    }

    [Fact]
    public async Task Decode_resizes_to_fit_maximum_without_upscaling()
    {
        using Mat source = new(200, 400, MatType.CV_8UC3, new Scalar(1, 2, 3));
        byte[] encoded = Encode(source, ".png");
        using MemoryStream stream = new(encoded);

        ImageFrame result = await _decoder.DecodeAsync(
            stream,
            new DecodeOptions(new ImageSize(100, 100)),
            CancellationToken.None);

        Assert.Equal(new ImageSize(100, 50), result.Size);
    }

    [Fact]
    public async Task Decode_rejects_unsupported_file_signatures()
    {
        byte[] gif = "GIF89a"u8.ToArray();
        using MemoryStream stream = new(gif);

        ImageDecodingException exception = await Assert.ThrowsAsync<ImageDecodingException>(
            () => _decoder.DecodeAsync(stream, new DecodeOptions(), CancellationToken.None));

        Assert.Equal(ImageDecodingFailure.UnsupportedFormat, exception.Failure);
    }

    [Fact]
    public async Task Decode_reports_corrupt_supported_media()
    {
        byte[] corruptPng =
        [
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
            0x00, 0x00, 0x00, 0x00,
        ];
        using MemoryStream stream = new(corruptPng);

        ImageDecodingException exception = await Assert.ThrowsAsync<ImageDecodingException>(
            () => _decoder.DecodeAsync(stream, new DecodeOptions(), CancellationToken.None));

        Assert.Equal(ImageDecodingFailure.CorruptMedia, exception.Failure);
    }

    [Fact]
    public async Task Decode_honours_cancellation_before_reading()
    {
        using MemoryStream stream = new("GIF89a"u8.ToArray());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _decoder.DecodeAsync(stream, new DecodeOptions(), cancellation.Token));
    }

    [Fact]
    public void Core_contract_does_not_reference_opencv()
    {
        Assert.DoesNotContain(
            typeof(IImageDecoder).Assembly.GetReferencedAssemblies(),
            assembly => string.Equals(assembly.Name, "OpenCvSharp", StringComparison.Ordinal));
    }

    private static byte[] Encode(Mat image, string extension)
    {
        bool encoded = Cv2.ImEncode(extension, image, out byte[] bytes);
        Assert.True(encoded);
        return bytes;
    }

    private static byte[] AddExifOrientation(byte[] jpeg, ushort orientation)
    {
        Assert.True(jpeg.Length >= 2 && jpeg[0] == 0xff && jpeg[1] == 0xd8);
        Assert.InRange(orientation, (ushort)1, (ushort)8);

        byte[] payload =
        [
            0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
            0x49, 0x49, 0x2a, 0x00,
            0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01,
            0x03, 0x00,
            0x01, 0x00, 0x00, 0x00,
            (byte)(orientation & 0xff), (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];

        int app1Length = payload.Length + 2;
        byte[] app1 = new byte[payload.Length + 4];
        app1[0] = 0xff;
        app1[1] = 0xe1;
        app1[2] = (byte)(app1Length >> 8);
        app1[3] = (byte)app1Length;
        payload.CopyTo(app1, 4);

        byte[] result = new byte[jpeg.Length + app1.Length];
        jpeg.AsSpan(0, 2).CopyTo(result);
        app1.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + app1.Length));
        return result;
    }
}
