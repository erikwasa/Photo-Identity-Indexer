using ImageMagick;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;
using Xunit;

namespace PhotoIdentity_Recognition_Tests;

public sealed class HeicImageDecoderTests
{
    private readonly OpenCvImageDecoder _decoder = new();

    [Fact]
    public void Bundled_imagemagick_runtime_exposes_a_heic_read_delegate()
    {
        var heic = Assert.Single(
            MagickNET.SupportedFormats.Where(format => format.Format == MagickFormat.Heic));

        Assert.True(heic.SupportsReading);
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
}
