using ImageMagick;

namespace PhotoIdentity.Imaging.OpenCv;

internal sealed record DngPreview(byte[] Jpeg, OrientationType Orientation);

/// <summary>
/// Reads the full-resolution JPEG preview from DNG IFD0 without demosaicing the RAW mosaic.
/// Only the deliberately verified single-strip, 8-bit RGB/YCbCr layout is accepted; every
/// other valid DNG layout falls back to the full RAW delegate.
/// </summary>
internal static class DngEmbeddedPreview
{
    private const ushort DngVersionTag = 0xc612;
    private const ushort ImageWidthTag = 0x0100;
    private const ushort ImageHeightTag = 0x0101;
    private const ushort BitsPerSampleTag = 0x0102;
    private const ushort CompressionTag = 0x0103;
    private const ushort PhotometricInterpretationTag = 0x0106;
    private const ushort StripOffsetsTag = 0x0111;
    private const ushort OrientationTag = 0x0112;
    private const ushort SamplesPerPixelTag = 0x0115;
    private const ushort RowsPerStripTag = 0x0116;
    private const ushort StripByteCountsTag = 0x0117;

    private const uint JpegCompression = 7;
    private const uint RgbPhotometricInterpretation = 2;
    private const uint YCbCrPhotometricInterpretation = 6;

    public static bool IsDng(ReadOnlySpan<byte> encoded) =>
        TryReadIfd0(encoded, out TiffIfd ifd) && ifd.Contains(DngVersionTag);

    public static bool TryExtract(ReadOnlySpan<byte> encoded, out DngPreview preview)
    {
        preview = null!;
        if (!TryReadIfd0(encoded, out TiffIfd ifd) ||
            !ifd.Contains(DngVersionTag) ||
            !ifd.TryReadSingle(ImageWidthTag, out uint width) || width == 0 ||
            !ifd.TryReadSingle(ImageHeightTag, out uint height) || height == 0 ||
            !ifd.TryReadSingle(CompressionTag, out uint compression) || compression != JpegCompression ||
            !ifd.TryReadSingle(PhotometricInterpretationTag, out uint photometric) ||
            (photometric != RgbPhotometricInterpretation && photometric != YCbCrPhotometricInterpretation) ||
            !ifd.TryReadValues(BitsPerSampleTag, out uint[] bitsPerSample) ||
            bitsPerSample is not [8, 8, 8] ||
            !ifd.TryReadSingle(SamplesPerPixelTag, out uint samplesPerPixel) || samplesPerPixel != 3 ||
            !ifd.TryReadSingle(RowsPerStripTag, out uint rowsPerStrip) || rowsPerStrip != height ||
            !ifd.TryReadSingle(StripOffsetsTag, out uint stripOffset) ||
            !ifd.TryReadSingle(StripByteCountsTag, out uint stripByteCount) || stripByteCount == 0 ||
            stripOffset > int.MaxValue ||
            stripByteCount > int.MaxValue ||
            (long)stripOffset + stripByteCount > encoded.Length)
        {
            return false;
        }

        ReadOnlySpan<byte> jpeg = encoded.Slice((int)stripOffset, (int)stripByteCount);
        if (jpeg.Length < 4 ||
            jpeg[0] != 0xff || jpeg[1] != 0xd8 ||
            jpeg[^2] != 0xff || jpeg[^1] != 0xd9)
        {
            return false;
        }

        uint rawOrientation = ifd.TryReadSingle(OrientationTag, out uint orientation)
            ? orientation
            : 1;
        if (rawOrientation is < 1 or > 8)
        {
            return false;
        }

        preview = new DngPreview(jpeg.ToArray(), (OrientationType)rawOrientation);
        return true;
    }

    private static bool TryReadIfd0(ReadOnlySpan<byte> encoded, out TiffIfd ifd)
    {
        ifd = default;
        if (encoded.Length < 10)
        {
            return false;
        }

        bool littleEndian;
        if (encoded[0] == (byte)'I' && encoded[1] == (byte)'I')
        {
            littleEndian = true;
        }
        else if (encoded[0] == (byte)'M' && encoded[1] == (byte)'M')
        {
            littleEndian = false;
        }
        else
        {
            return false;
        }

        TiffReader reader = new(encoded, littleEndian);
        if (!reader.TryReadUInt16(2, out ushort magic) || magic != 42 ||
            !reader.TryReadUInt32(4, out uint rawIfdOffset) ||
            rawIfdOffset > int.MaxValue)
        {
            return false;
        }

        int ifdOffset = (int)rawIfdOffset;
        if (!reader.TryReadUInt16(ifdOffset, out ushort entryCount))
        {
            return false;
        }

        long entriesEnd = (long)ifdOffset + 2L + (entryCount * 12L);
        if (entriesEnd > encoded.Length)
        {
            return false;
        }

        ifd = new TiffIfd(reader, ifdOffset + 2, entryCount);
        return true;
    }

    private readonly ref struct TiffIfd
    {
        private readonly TiffReader _reader;
        private readonly int _entriesOffset;
        private readonly int _entryCount;

        public TiffIfd(TiffReader reader, int entriesOffset, int entryCount)
        {
            _reader = reader;
            _entriesOffset = entriesOffset;
            _entryCount = entryCount;
        }

        public bool Contains(ushort tag) => TryFind(tag, out _);

        public bool TryReadSingle(ushort tag, out uint value)
        {
            value = 0;
            return TryFind(tag, out TiffEntry entry) &&
                entry.Count == 1 &&
                TryReadEntryValue(entry, 0, out value);
        }

        public bool TryReadValues(ushort tag, out uint[] values)
        {
            values = [];
            if (!TryFind(tag, out TiffEntry entry) || entry.Count == 0 || entry.Count > 16)
            {
                return false;
            }

            values = new uint[entry.Count];
            for (int index = 0; index < values.Length; index++)
            {
                if (!TryReadEntryValue(entry, index, out values[index]))
                {
                    values = [];
                    return false;
                }
            }

            return true;
        }

        private bool TryFind(ushort tag, out TiffEntry entry)
        {
            for (int index = 0; index < _entryCount; index++)
            {
                int offset = _entriesOffset + (index * 12);
                if (_reader.TryReadUInt16(offset, out ushort candidate) && candidate == tag &&
                    _reader.TryReadUInt16(offset + 2, out ushort type) &&
                    _reader.TryReadUInt32(offset + 4, out uint count))
                {
                    entry = new TiffEntry(type, count, offset + 8);
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private bool TryReadEntryValue(TiffEntry entry, int index, out uint value)
        {
            int elementSize = entry.Type switch
            {
                1 => 1,
                3 => 2,
                4 => 4,
                _ => 0,
            };
            if (elementSize == 0 || index < 0 || (uint)index >= entry.Count)
            {
                value = 0;
                return false;
            }

            long byteCount = (long)entry.Count * elementSize;
            int valueOffset;
            if (byteCount <= 4)
            {
                valueOffset = entry.ValueFieldOffset + (index * elementSize);
            }
            else
            {
                if (!_reader.TryReadUInt32(entry.ValueFieldOffset, out uint rawOffset) || rawOffset > int.MaxValue)
                {
                    value = 0;
                    return false;
                }
                long indexedOffset = (long)rawOffset + (index * elementSize);
                if (indexedOffset > int.MaxValue)
                {
                    value = 0;
                    return false;
                }
                valueOffset = (int)indexedOffset;
            }

            value = 0;
            return entry.Type switch
            {
                1 => _reader.TryReadByte(valueOffset, out value),
                3 => _reader.TryReadUInt16(valueOffset, out value),
                4 => _reader.TryReadUInt32(valueOffset, out value),
                _ => false,
            };
        }
    }

    private readonly record struct TiffEntry(ushort Type, uint Count, int ValueFieldOffset);

    private readonly ref struct TiffReader
    {
        private readonly ReadOnlySpan<byte> _source;
        private readonly bool _littleEndian;

        public TiffReader(ReadOnlySpan<byte> source, bool littleEndian)
        {
            _source = source;
            _littleEndian = littleEndian;
        }

        public bool TryReadByte(int offset, out uint value)
        {
            if ((uint)offset >= (uint)_source.Length)
            {
                value = 0;
                return false;
            }
            value = _source[offset];
            return true;
        }

        public bool TryReadUInt16(int offset, out ushort value)
        {
            if (offset < 0 || offset > _source.Length - 2)
            {
                value = 0;
                return false;
            }
            value = _littleEndian
                ? (ushort)(_source[offset] | (_source[offset + 1] << 8))
                : (ushort)((_source[offset] << 8) | _source[offset + 1]);
            return true;
        }

        public bool TryReadUInt16(int offset, out uint value)
        {
            bool result = TryReadUInt16(offset, out ushort parsed);
            value = parsed;
            return result;
        }

        public bool TryReadUInt32(int offset, out uint value)
        {
            if (offset < 0 || offset > _source.Length - 4)
            {
                value = 0;
                return false;
            }
            value = _littleEndian
                ? (uint)(_source[offset] |
                    (_source[offset + 1] << 8) |
                    (_source[offset + 2] << 16) |
                    (_source[offset + 3] << 24))
                : (uint)((_source[offset] << 24) |
                    (_source[offset + 1] << 16) |
                    (_source[offset + 2] << 8) |
                    _source[offset + 3]);
            return true;
        }
    }
}
