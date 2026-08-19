using System.Text;
using PhotoIdentity.Source.Local;
using Xunit;

namespace PhotoIdentity_Source_Tests;

public sealed class MetadataExtractorPhotoMetadataReaderTests
{
    [Fact]
    public async Task Xmp_only_jpeg_preserves_capture_wall_clock_offset_and_camera_identity()
    {
        const string xmp = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description
                    xmlns:xmp="http://ns.adobe.com/xap/1.0/"
                    xmlns:tiff="http://ns.adobe.com/tiff/1.0/"
                    xmp:CreateDate="2026-08-19T14:15:16+02:00"
                    tiff:Make="Example Camera Co."
                    tiff:Model="Phone Model X" />
              </rdf:RDF>
            </x:xmpmeta>
            """;
        await using MemoryStream stream = new(BuildXmpOnlyJpeg(xmp), writable: false);

        PhotoIdentity.Core.Sources.PhotoCaptureMetadata metadata =
            await new MetadataExtractorPhotoMetadataReader().ReadAsync(stream, "image/jpeg");

        Assert.Equal(
            new DateTime(2026, 8, 19, 14, 15, 16, DateTimeKind.Unspecified),
            metadata.TakenAtLocal);
        Assert.Equal(TimeSpan.FromHours(2), metadata.UtcOffset);
        Assert.Equal("Example Camera Co.", metadata.CameraMake);
        Assert.Equal("Phone Model X", metadata.CameraModel);
        Assert.Contains(metadata.RawTags, tag =>
            tag.Directory == "XMP" &&
            tag.Name.Equals("xmp:CreateDate", StringComparison.OrdinalIgnoreCase) &&
            tag.Value.Contains("2026-08-19T14:15:16+02:00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Xmp_timestamp_without_offset_remains_timezone_less()
    {
        const string xmp = """
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description
                    xmlns:exif="http://ns.adobe.com/exif/1.0/"
                    exif:DateTimeOriginal="2024-01-02T03:04:05" />
              </rdf:RDF>
            </x:xmpmeta>
            """;
        await using MemoryStream stream = new(BuildXmpOnlyJpeg(xmp), writable: false);

        PhotoIdentity.Core.Sources.PhotoCaptureMetadata metadata =
            await new MetadataExtractorPhotoMetadataReader().ReadAsync(stream, "image/jpeg");

        Assert.Equal(
            new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Unspecified),
            metadata.TakenAtLocal);
        Assert.Null(metadata.UtcOffset);
    }

    private static byte[] BuildXmpOnlyJpeg(string xmp)
    {
        byte[] signature = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");
        byte[] packet = Encoding.UTF8.GetBytes(xmp);
        int payloadLength = signature.Length + packet.Length;
        int segmentLength = payloadLength + 2;
        if (segmentLength > ushort.MaxValue)
        {
            throw new InvalidOperationException("Synthetic XMP packet is too large for one JPEG APP1 segment.");
        }

        using MemoryStream stream = new();
        stream.WriteByte(0xFF);
        stream.WriteByte(0xD8); // SOI
        stream.WriteByte(0xFF);
        stream.WriteByte(0xE1); // APP1
        stream.WriteByte((byte)(segmentLength >> 8));
        stream.WriteByte((byte)segmentLength);
        stream.Write(signature);
        stream.Write(packet);
        stream.WriteByte(0xFF);
        stream.WriteByte(0xD9); // EOI
        return stream.ToArray();
    }
}
