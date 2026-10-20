using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using PhotoIdentity.Core.Catalogue;

namespace PhotoIdentity.Cli;

/// <summary>
/// Unregistered, explicitly experimental WI-0188/WI-0189 handoff profile.
/// NOT a production transcode policy. In particular, HDR/unknown transfer,
/// unreviewed audio and unsupported rotation are deliberately deferred.
/// </summary>
public static class VideoProxyCandidateProfile
{
    public const string Version = "wi0188-sdr-h264-aac-720p-pilot-v1";
    public const long MaximumOutputBytes = 256L * 1024 * 1024;
    public const int MaximumDurationSeconds = 600;

    public static bool Eligible(VideoRevisionInspection? input) =>
        input is
        {
            InspectionStatus: "supported",
            ColorTransfer: "bt709",
            VideoCodec: "h264" or "hevc",
            PixelFormat: "yuv420p" or "yuv420p10le",
            CodedWidth: >= 16 and <= 4096,
            CodedHeight: >= 16 and <= 4096,
            DurationSeconds: >= 0.2m and <= 600m,
            DisplayRotationDegrees: 0 or 90 or -90 or 180 or -180
        } &&
        input.AudioCodec is null or "aac" or "pcm_s16le" or "pcm_s24le";

    /// <summary>
    /// No shell, overwrite, network, metadata copying, GPU codecs, additional
    /// streams or arbitrary filter injection. FFmpeg's default autorotation
    /// is retained; bounding both axes respects portrait and landscape.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(
        string stagedFile, string toolWorkspace)
    {
        if (!Path.IsPathFullyQualified(stagedFile) ||
            !Path.IsPathFullyQualified(toolWorkspace))
            throw new ArgumentException("A verified absolute staged source and tool workspace are required.");

        string output = Path.Combine(toolWorkspace, "output.mp4");
        return
        [
            "-nostdin", "-hide_banner", "-loglevel", "error",
            "-protocol_whitelist", "file",
            "-probesize", "2097152", "-analyzeduration", "2000000",
            "-threads", "1", "-filter_threads", "1",
            "-i", stagedFile,
            "-map", "0:v:0", "-map", "0:a:0?",
            "-sn", "-dn", "-map_metadata", "-1", "-map_chapters", "-1",
            "-t", MaximumDurationSeconds.ToString(CultureInfo.InvariantCulture),
            "-vf", "scale=w=720:h=720:force_original_aspect_ratio=decrease:force_divisible_by=2,fps=30,format=yuv420p",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "24",
            "-profile:v", "main", "-level:v", "3.1",
            // Container color fields alone are not sufficient: libx264 can
            // otherwise leave transfer/primaries unspecified in the H.264
            // VUI. Independent FFprobe validation requires explicit values.
            "-x264-params", "colorprim=bt709:transfer=bt709:colormatrix=bt709",
            "-maxrate", "2500k", "-bufsize", "5000k",
            "-pix_fmt", "yuv420p", "-color_primaries", "bt709",
            "-color_trc", "bt709", "-colorspace", "bt709",
            "-c:a", "aac", "-b:a", "96k", "-ac", "2", "-ar", "48000",
            "-movflags", "+faststart", "-f", "mp4",
            "-fs", MaximumOutputBytes.ToString(CultureInfo.InvariantCulture),
            output
        ];
    }

    /// <summary>Validate untrusted *bounded* ffprobe JSON; no media/user tags are returned.</summary>
    public static VideoProxyInspection ValidateOutputJson(
        ReadOnlySpan<byte> utf8, long actualLength)
    {
        if (utf8.Length is < 20 or > 65536 || actualLength is < 128 or > MaximumOutputBytes)
            throw new InvalidDataException("Video candidate probe output is unbounded or incomplete.");

        using JsonDocument document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
        {
            MaxDepth = 12
        });
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("format", out JsonElement format) ||
            !format.TryGetProperty("format_name", out JsonElement formatName) ||
            formatName.GetString() is not string container ||
            !container.Split(',').Contains("mp4", StringComparer.Ordinal) ||
            !TryNumeric(format, "duration", out double seconds) ||
            seconds is < 0.1 or > MaximumDurationSeconds + 1 ||
            !TryLong(format, "size", out long measuredSize) ||
            measuredSize != actualLength)
            throw new InvalidDataException("Video candidate has an unsupported container or duration.");

        if (!root.TryGetProperty("streams", out JsonElement streams) ||
            streams.ValueKind != JsonValueKind.Array ||
            streams.GetArrayLength() is < 1 or > 2)
            throw new InvalidDataException("Video candidate streams do not match the pilot profile.");

        int videoCount = 0, audioCount = 0;
        int width = 0, height = 0;
        foreach (JsonElement stream in streams.EnumerateArray())
        {
            if (!stream.TryGetProperty("codec_type", out JsonElement codecType))
                throw new InvalidDataException("Video candidate has an untyped stream.");
            switch (codecType.GetString())
            {
                case "video":
                    if (++videoCount != 1 ||
                        !EqualsText(stream, "codec_name", "h264") ||
                        !EqualsText(stream, "pix_fmt", "yuv420p") ||
                        !EqualsText(stream, "color_transfer", "bt709") ||
                        !EqualsText(stream, "color_primaries", "bt709") ||
                        !EqualsText(stream, "color_space", "bt709") ||
                        !TryInt(stream, "width", out width) ||
                        !TryInt(stream, "height", out height) ||
                        width is < 16 or > 720 || height is < 16 or > 720 ||
                        width % 2 != 0 || height % 2 != 0 ||
                        !stream.TryGetProperty("r_frame_rate", out JsonElement rate) ||
                        !FrameRateAllowed(rate.GetString()))
                        throw new InvalidDataException("Video candidate video stream fails the SDR 720p contract.");
                    if (stream.TryGetProperty("tags", out JsonElement tags) &&
                        tags.TryGetProperty("rotate", out JsonElement rotation) &&
                        rotation.GetString() is string rot && rot != "0")
                        throw new InvalidDataException("Video candidate retained a display rotation.");
                    if (stream.TryGetProperty("side_data_list", out JsonElement side) &&
                        side.ValueKind == JsonValueKind.Array &&
                        side.EnumerateArray().Any(item =>
                            item.TryGetProperty("rotation", out JsonElement rotation) &&
                            rotation.GetInt32() != 0))
                        throw new InvalidDataException("Video candidate has unnormalized display orientation.");
                    break;
                case "audio":
                    if (++audioCount > 1 ||
                        !EqualsText(stream, "codec_name", "aac") ||
                        !TryInt(stream, "channels", out int channels) ||
                        channels is < 1 or > 2)
                        throw new InvalidDataException("Video candidate audio track is not AAC mono/stereo.");
                    break;
                default:
                    throw new InvalidDataException("Video candidate contains an unexpected media stream.");
            }
        }

        if (videoCount != 1)
            throw new InvalidDataException("Video candidate is missing its H.264 stream.");
        return new VideoProxyInspection(width, height, audioCount == 1, seconds);
    }

    public static void ValidateFastStart(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 128 or > MaximumOutputBytes)
            throw new InvalidDataException("MP4 candidate size is invalid.");

        bool ftyp = false, moov = false, mdat = false;
        Span<byte> box = stackalloc byte[16];
        for (int i = 0; stream.Position < stream.Length && i < 256; i++)
        {
            long pos = stream.Position;
            if (stream.Read(box[..8]) != 8)
                throw new InvalidDataException("MP4 top-level box header is truncated.");
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(box[..4]);
            string type = System.Text.Encoding.ASCII.GetString(box.Slice(4, 4));
            int header = 8;
            if (size == 1)
            {
                if (stream.Read(box[..8]) != 8)
                    throw new InvalidDataException("MP4 extended box header is truncated.");
                size = BinaryPrimitives.ReadUInt64BigEndian(box[..8]);
                header = 16;
            }
            else if (size == 0)
                size = (ulong)(stream.Length - pos);

            if (size < (ulong)header || size > (ulong)(stream.Length - pos))
                throw new InvalidDataException("MP4 top-level box size is invalid.");
            if (i == 0 && type != "ftyp")
                throw new InvalidDataException("MP4 must begin with a file type box.");
            if (type == "ftyp") ftyp = true;
            if (type == "moov") moov = true;
            if (type == "mdat")
            {
                if (!moov) throw new InvalidDataException("MP4 is not fast-start seekable.");
                mdat = true;
            }
            stream.Position = pos + (long)size;
        }
        if (!ftyp || !moov || !mdat || stream.Position != stream.Length)
            throw new InvalidDataException("MP4 candidate lacks a complete fast-start layout.");
    }

    private static bool EqualsText(JsonElement element, string name, string value) =>
        element.TryGetProperty(name, out JsonElement field) &&
        field.ValueKind == JsonValueKind.String && field.GetString() == value;

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement field) &&
            field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out value);
    }

    private static bool TryLong(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement field) &&
            long.TryParse(field.ToString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value);
    }

    private static bool TryNumeric(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement field) &&
            double.TryParse(field.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value);
    }

    private static bool FrameRateAllowed(string? value)
    {
        if (value is null) return false;
        string[] bits = value.Split('/');
        return bits.Length == 2 &&
            uint.TryParse(bits[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint n) &&
            uint.TryParse(bits[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint d) &&
            d > 0 && n > 0 && (double)n / d <= 30.1;
    }
}

/// <summary>No video source paths, archive metadata, hashes or private names.</summary>
public sealed record VideoProxyInspection(int Width, int Height, bool HasAudio, double DurationSeconds);
