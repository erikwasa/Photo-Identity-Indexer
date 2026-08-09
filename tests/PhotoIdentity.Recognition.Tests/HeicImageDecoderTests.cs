using ImageMagick;
using OpenCvSharp;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using Xunit;

namespace PhotoIdentity_Recognition_Tests;

public sealed class HeicImageDecoderTests
{
    private readonly OpenCvImageDecoder _decoder = new();

    [Fact]
    public async Task Decode_heic_returns_packed_bgr_pixels_and_resizes()
    {
        byte[] encoded = CreateHeic(width: 80, height: 40);
        using MemoryStream stream = new(encoded);

        ImageFrame result = await _decoder.DecodeAsync(
            stream,
            new DecodeOptions(new ImageSize(40, 40)),
            CancellationToken.None);

        Assert.Equal(new ImageSize(40, 20), result.Size);
        Assert.Equal(PixelFormat.Bgr24, result.Format);
        Assert.Equal(120, result.Stride);
        Assert.Equal(result.Stride * result.Size.Height, result.Data.Length);
    }

    [Fact]
    public void Review_proxy_renderer_uses_the_same_heic_decode_path()
    {
        byte[] encoded = CreateHeic(width: 120, height: 60);
        OpenCvReviewProxyRenderer renderer = new();

        EncodedReviewProxy proxy = renderer.Render(
            encoded,
            new ReviewProxyProfile("heic-test", 80, 82));

        Assert.Equal(80, proxy.Width);
        Assert.Equal(40, proxy.Height);
        Assert.Equal("image/jpeg", proxy.ContentType);
        using Mat decoded = Cv2.ImDecode(proxy.Content, ImreadModes.Color);
        Assert.False(decoded.Empty());
        Assert.Equal(80, decoded.Cols);
        Assert.Equal(40, decoded.Rows);
    }

    [Fact]
    public async Task Heif_signature_with_invalid_payload_is_corrupt_not_unsupported()
    {
        byte[] corruptHeic =
        [
            0x00, 0x00, 0x00, 0x18,
            0x66, 0x74, 0x79, 0x70,
            0x68, 0x65, 0x69, 0x63,
            0x00, 0x00, 0x00, 0x00,
            0x6d, 0x69, 0x66, 0x31,
            0x68, 0x65, 0x69, 0x63,
        ];
        using MemoryStream stream = new(corruptHeic);

        ImageDecodingException exception = await Assert.ThrowsAsync<ImageDecodingException>(
            () => _decoder.DecodeAsync(stream, new DecodeOptions(), CancellationToken.None));

        Assert.Equal(ImageDecodingFailure.CorruptMedia, exception.Failure);
    }

    private static byte[] CreateHeic(uint width, uint height)
    {
        using MagickImage image = new(MagickColors.CornflowerBlue, width, height);
        image.Format = MagickFormat.Heic;
        image.Quality = 90;
        return image.ToByteArray();
    }
}
