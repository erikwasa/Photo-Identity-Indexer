using System.Globalization;
using System.Text.Json;
using PhotoIdentity.Core.Catalogue;

namespace PhotoIdentity.Cli;

/// <summary>
/// WI-0210 isolated evaluation contract, NOT a registered worker profile or
/// operator command. Only a separately approved, locally staged original
/// and a bounded independent FFprobe result may enter a future trial.
/// This module never reads original bytes, opens PostgreSQL or starts FFmpeg.
/// </summary>
public static class VideoRec709FullRangeEvaluationProfile
{
    public const string Version = "wi0210-rec709-fullrange-720p-evaluation-v1";
    private const long MaximumSourceBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Confirm an explicitly full-range, correctly signalled Rec.709 stream
    /// against the CURRENT persisted revision. Pixel format alone does NOT
    /// establish full range, matrix, primaries, rotation or local availability.
    /// The caller must independently check actual local source availability,
    /// revision identity, SHA-256, exclusions and approved tool provenance.
    /// </summary>
    public static void ValidateInputProbeJson(
        ReadOnlySpan<byte> utf8, VideoRevisionInspection? current,
        long registeredSourceBytes)
    {
        if (utf8.Length is < 20 or > 65536)
            throw new InvalidDataException("Bounded full-range input probe required.");
        if (current is not
            {
                InspectionStatus: "supported",
                ColorTransfer: "bt709",
                PixelFormat: "yuvj420p",
                VideoCodec: "h264" or "hevc",
                CodedWidth: >= 16 and <= 4096,
                CodedHeight: >= 16 and <= 4096,
                DurationSeconds: >= 0.2m and <= 600m,
                DisplayRotationDegrees: 0 or 90 or -90 or 180 or -180
            } ||
            current.AudioCodec is not (null or "aac" or "pcm_s16le" or "pcm_s24le") ||
            registeredSourceBytes is < 1 or > MaximumSourceBytes)
            throw new InvalidDataException("Input is outside the exact full-range metadata trial.");

        using JsonDocument doc = JsonDocument.Parse(utf8.ToArray(),
            new JsonDocumentOptions { MaxDepth = 12 });
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("format", out JsonElement format) ||
            Text(format, "format_name") is not string container ||
            !container.Split(',').Any(x => x is "mov" or "mp4"))
            throw new InvalidDataException("Input container was not approved.");
        if (!TryLong(format, "size", out long bytes) ||
            bytes != registeredSourceBytes ||
            !TryDecimal(format, "duration", out decimal duration) ||
            duration is < 0.2m or > 600m ||
            Math.Abs(duration - current.DurationSeconds!.Value) > 0.5m)
            throw new InvalidDataException("Input size or duration does not match revision evidence.");

        if (!root.TryGetProperty("streams", out JsonElement streams) ||
            streams.ValueKind != JsonValueKind.Array ||
            streams.GetArrayLength() is < 1 or > 2)
            throw new InvalidDataException("Unapproved input stream layout.");

        int videos = 0, audios = 0;
        foreach (JsonElement stream in streams.EnumerateArray())
        {
            switch (Text(stream, "codec_type"))
            {
                case "video":
                    if (++videos != 1 ||
                        Text(stream, "codec_name") != current.VideoCodec ||
                        Text(stream, "pix_fmt") != "yuvj420p" ||
                        Text(stream, "color_range") != "pc" ||
                        Text(stream, "color_transfer") != "bt709" ||
                        Text(stream, "color_primaries") != "bt709" ||
                        Text(stream, "color_space") != "bt709" ||
                        !TryInt(stream, "width", out int width) ||
                        !TryInt(stream, "height", out int height) ||
                        width != current.CodedWidth || height != current.CodedHeight ||
                        !TryExplicitRotation(stream, out int rotation) ||
                        rotation != current.DisplayRotationDegrees)
                        throw new InvalidDataException("Input colour, geometry or rotation is not proven.");
                    break;
                case "audio":
                    if (++audios > 1 ||
                        Text(stream, "codec_name") != current.AudioCodec)
                        throw new InvalidDataException("Audio stream differs from revision evidence.");
                    break;
                default:
                    throw new InvalidDataException("Unsupported input stream type.");
            }
        }
        if (videos != 1 || (audios == 0 && current.AudioCodec is not null) ||
            (audios == 1 && current.AudioCodec is null))
            throw new InvalidDataException("Input stream set differs from revision evidence.");
    }

    /// <summary>
    /// The standard 720p software recipe is inherited; ONLY the fixed,
    /// full-to-limited BT.709 range conversion and explicit limited range
    /// signaling differ. No caller-provided filters, shell or protocols.
    /// This is not a registered production preset/version.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string stagedFile, string workspace)
    {
        List<string> args = [.. VideoProxyCandidateProfile.BuildArguments(stagedFile, workspace)];
        int filter = args.IndexOf("-vf");
        if (filter < 0 || filter == args.Count - 1)
            throw new InvalidOperationException("The baseline encoder profile is incomplete.");
        args[filter + 1] =
            "scale=w=720:h=720:in_range=full:out_range=limited:" +
            "in_color_matrix=bt709:out_color_matrix=bt709:" +
            "force_original_aspect_ratio=decrease:force_divisible_by=2," +
            "fps=30,format=yuv420p";

        int colour = args.IndexOf("-color_primaries");
        if (colour < 0)
            throw new InvalidOperationException("Missing baseline Rec.709 output flags.");
        args.InsertRange(colour, ["-color_range", "tv"]);
        return args;
    }

    /// <summary>
    /// Existing independent MP4 H.264/AAC, colour VUI, duration and faststart
    /// proof still applies; require an additional explicit LIMITED output range.
    /// </summary>
    public static VideoProxyInspection ValidateOutputProbeJson(
        ReadOnlySpan<byte> utf8, long outputBytes)
    {
        VideoProxyInspection result =
            VideoProxyCandidateProfile.ValidateOutputJson(utf8, outputBytes);
        using JsonDocument doc = JsonDocument.Parse(utf8.ToArray(),
            new JsonDocumentOptions { MaxDepth = 12 });
        JsonElement video = doc.RootElement.GetProperty("streams")
            .EnumerateArray().Single(x => Text(x, "codec_type") == "video");
        if (Text(video, "color_range") != "tv")
            throw new InvalidDataException("Full-range trial output must declare limited range.");
        return result;
    }

    private static string? Text(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(key, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryInt(JsonElement element, string key, out int number)
    {
        number = default;
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(key, out JsonElement value) &&
            value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out number);
    }

    private static bool TryLong(JsonElement element, string key, out long number)
    {
        number = default;
        return long.TryParse(Text(element, key), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out number);
    }

    private static bool TryDecimal(JsonElement element, string key, out decimal number)
    {
        number = default;
        return decimal.TryParse(Text(element, key), NumberStyles.Float,
            CultureInfo.InvariantCulture, out number);
    }

    private static bool TryExplicitRotation(JsonElement stream, out int rotation)
    {
        rotation = default;
        int? value = null;
        if (stream.TryGetProperty("tags", out JsonElement tags) &&
            Text(tags, "rotate") is string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out int tagRotation))
                return false;
            value = tagRotation;
        }

        if (stream.TryGetProperty("side_data_list", out JsonElement sideData) &&
            sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in sideData.EnumerateArray())
            {
                if (!entry.TryGetProperty("rotation", out JsonElement rawRotation))
                    continue;
                if (rawRotation.ValueKind != JsonValueKind.Number ||
                    !rawRotation.TryGetInt32(out int sideRotation) ||
                    (value is not null && value != sideRotation))
                    return false;
                value = sideRotation;
            }
        }

        if (value is not (0 or 90 or -90 or 180 or -180))
            return false;
        rotation = value.Value;
        return true;
    }
}
