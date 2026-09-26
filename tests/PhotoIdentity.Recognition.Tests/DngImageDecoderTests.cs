using System.Buffers.Binary;
using ImageMagick;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using Xunit;

namespace PhotoIdentity_Recognition_Tests;

public sealed class DngImageDecoderTests
{
    private readonly OpenCvImageDecoder _decoder = new();

    [Fact]
    public void Bundled_imagemagick_runtime_exposes_a_dng_read_delegate()
    {
        var dng = Assert.Single(
            MagickNET.SupportedFormats,
            format => format.Format == MagickFormat.Dng);

        Assert.True(dng.SupportsReading);
    }

    [Fact]
    public async Task Dng_signature_with_invalid_payload_is_corrupt_not_unsupported()
    {
        byte[] corruptDng =
        [
            0x49, 0x49, 0x2a, 0x00,
            0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0xc6, 0x01, 0x00,
            0x04, 0x00, 0x00, 0x00,
            0x01, 0x04, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        using MemoryStream stream = new(corruptDng);

        ImageDecodingException exception = await Assert.ThrowsAsync<ImageDecodingException>(
            () => _decoder.DecodeAsync(stream, new DecodeOptions(), CancellationToken.None));

        Assert.Equal(ImageDecodingFailure.CorruptMedia, exception.Failure);
    }

    [Fact]
    public async Task Full_resolution_embedded_preview_is_oriented_and_returns_packed_bgr_pixels()
    {
        byte[] dng = CreateSyntheticDng(width: 3, height: 2, orientation: 6);
        using MemoryStream stream = new(dng);

        ImageFrame image = await _decoder.DecodeAsync(
            stream,
            new DecodeOptions(),
            CancellationToken.None);

        Assert.Equal(2, image.Size.Width);
        Assert.Equal(3, image.Size.Height);
        Assert.Equal(PixelFormat.Bgr24, image.Format);
        Assert.Equal(2 * 3, image.Stride);
        Assert.Equal(image.Stride * image.Size.Height, image.ToArray().Length);
    }

    [Fact]
    public async Task Ordinary_tiff_signature_remains_unsupported()
    {
        byte[] tiff =
        [
            0x49, 0x49, 0x2a, 0x00,
            0x08, 0x00, 0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        using MemoryStream stream = new(tiff);

        ImageDecodingException exception = await Assert.ThrowsAsync<ImageDecodingException>(
            () => _decoder.DecodeAsync(stream, new DecodeOptions(), CancellationToken.None));

        Assert.Equal(ImageDecodingFailure.UnsupportedFormat, exception.Failure);
    }

    private static byte[] CreateSyntheticDng(int width, int height, ushort orientation)
    {
        using MagickImage image = new(MagickColors.Crimson, (uint)width, (uint)height)
        {
            Format = MagickFormat.Jpeg,
        };
        byte[] jpeg = image.ToByteArray();

        const ushort entryCount = 11;
        const int ifdOffset = 8;
        int valuesOffset = ifdOffset + 2 + (entryCount * 12) + 4;
        int bitsPerSampleOffset = valuesOffset;
        int jpegOffset = bitsPerSampleOffset + 6;
        byte[] dng = new byte[jpegOffset + jpeg.Length];

        dng[0] = (byte)'I';
        dng[1] = (byte)'I';
        WriteUInt16(dng, 2, 42);
        WriteUInt32(dng, 4, ifdOffset);
        WriteUInt16(dng, ifdOffset, entryCount);

        int entryOffset = ifdOffset + 2;
        WriteEntry(dng, ref entryOffset, 0x0100, 4, 1, (uint)width);
        WriteEntry(dng, ref entryOffset, 0x0101, 4, 1, (uint)height);
        WriteEntry(dng, ref entryOffset, 0x0102, 3, 3, (uint)bitsPerSampleOffset);
        WriteEntry(dng, ref entryOffset, 0x0103, 3, 1, 7);
        WriteEntry(dng, ref entryOffset, 0x0106, 3, 1, 6);
        WriteEntry(dng, ref entryOffset, 0x0111, 4, 1, (uint)jpegOffset);
        WriteEntry(dng, ref entryOffset, 0x0112, 3, 1, orientation);
        WriteEntry(dng, ref entryOffset, 0x0115, 3, 1, 3);
        WriteEntry(dng, ref entryOffset, 0x0116, 4, 1, (uint)height);
        WriteEntry(dng, ref entryOffset, 0x0117, 4, 1, (uint)jpeg.Length);
        WriteEntry(dng, ref entryOffset, 0xc612, 1, 4, 0x00000401);
        WriteUInt32(dng, entryOffset, 0);

        WriteUInt16(dng, bitsPerSampleOffset, 8);
        WriteUInt16(dng, bitsPerSampleOffset + 2, 8);
        WriteUInt16(dng, bitsPerSampleOffset + 4, 8);
        jpeg.CopyTo(dng, jpegOffset);
        return dng;
    }

    private static void WriteEntry(
        byte[] destination,
        ref int offset,
        ushort tag,
        ushort type,
        uint count,
        uint value)
    {
        WriteUInt16(destination, offset, tag);
        WriteUInt16(destination, offset + 2, type);
        WriteUInt32(destination, offset + 4, count);
        WriteUInt32(destination, offset + 8, value);
        offset += 12;
    }

    private static void WriteUInt16(byte[] destination, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] destination, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, 4), value);
}
