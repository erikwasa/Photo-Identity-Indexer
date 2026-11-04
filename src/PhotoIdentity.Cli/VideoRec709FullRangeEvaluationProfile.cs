using System.Globalization;
using System.Text.Json;
using PhotoIdentity.Core.Catalogue;

namespace PhotoIdentity.Cli;

/// <summary>
/// Sanitized, operator-visible outcome for an explicitly authorized real
/// source's independent FFprobe. Never contains private stream values,
/// original paths, revision IDs, full FFprobe output or exception text.
/// </summary>
public sealed class VideoFullRangeInputRejectionException : Exception
{
    public string ReasonCode { get; }

    public VideoFullRangeInputRejectionException(string reasonCode)
        : base("Real full-range input evidence did not meet the approved contract.")
    {
        if (reasonCode is not (
            "source-registered-class-ineligible" or "source-probe-data-invalid" or
            "source-container-unapproved" or "source-size-or-duration-mismatch" or
            "source-stream-layout-unsupported" or "source-ancillary-stream-unapproved" or
            "source-video-codec-mismatch" or
            "source-pixel-format-unproven" or "source-full-range-unproven" or
            "source-bt709-signalling-unproven" or "source-geometry-mismatch" or
            "source-rotation-unproven" or "source-audio-layout-mismatch" or
            "source-probe-execution-incomplete"))
            throw new ArgumentException("Unrecognized sanitized input rejection code.");
        ReasonCode = reasonCode;
    }
}

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
    /// Only the exact persisted SDR pixel class previously accepted under
    /// one privately authorized real-video evaluation can be selected.
    /// This is a metadata preflight, NOT permission to encode: the worker
    /// must independently FFprobe the staged, SHA-verified original.
    /// </summary>
    public static bool Eligible(VideoRevisionInspection? input) =>
        input is
        {
            InspectionStatus: "supported",
            ColorTransfer: "bt709",
            PixelFormat: "yuvj420p",
            VideoCodec: "h264" or "hevc",
            CodedWidth: >= 16 and <= 4096,
            CodedHeight: >= 16 and <= 4096,
            DurationSeconds: >= 0.2m and <= 600m,
            DisplayRotationDegrees: 0 or 90 or -90 or 180 or -180
        } && input.AudioCodec is null or "aac" or "pcm_s16le" or "pcm_s24le";


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
        static VideoFullRangeInputRejectionException Reject(string reason) => new(reason);

        if (utf8.Length is < 20 or > 65536)
            throw Reject("source-probe-data-invalid");
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
            throw Reject("source-registered-class-ineligible");

        // Only bounded, allowlisted field *categories* leave this
        // validator. Actual JSON and all media values stay private.
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8.ToArray(),
                new JsonDocumentOptions { MaxDepth = 12 });
        }
        catch (JsonException)
        {
            throw Reject("source-probe-data-invalid");
        }
        using (document)
        {
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("format", out JsonElement format) ||
                Text(format, "format_name") is not string container ||
                !container.Split(',').Any(x => x is "mov" or "mp4"))
                throw Reject("source-container-unapproved");
            if (!TryLong(format, "size", out long bytes) ||
                bytes != registeredSourceBytes ||
                !TryDecimal(format, "duration", out decimal duration) ||
                duration is < 0.2m or > 600m ||
                Math.Abs(duration - current.DurationSeconds!.Value) > 0.5m)
                throw Reject("source-size-or-duration-mismatch");

            if (!root.TryGetProperty("streams", out JsonElement streams) ||
                streams.ValueKind != JsonValueKind.Array ||
                streams.GetArrayLength() is < 1 or > 4)
                throw Reject("source-stream-layout-unsupported");

            // A MOV/MP4 camera may have auxiliary data tracks whose private
            // four-character codes and codec names are not safely enumerable.
            // Only codec_type=data is ignored, for up to TWO input tracks
            // within the overall 4-stream limit. The fixed encoder maps only
            // the proven video and optional audio; never data. No codec tags,
            // telemetry or GPS payloads are extracted or reported.
            // No other non-A/V stream types (including subtitles/attachments)
            // are approved and no broader colour/codec gate is changed.
            int videos = 0, audios = 0, dataTracks = 0;
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                switch (Text(stream, "codec_type"))
                {
                    case "video":
                        if (++videos != 1)
                            throw Reject("source-stream-layout-unsupported");
                        if (Text(stream, "codec_name") != current.VideoCodec)
                            throw Reject("source-video-codec-mismatch");
                        if (Text(stream, "pix_fmt") != "yuvj420p")
                            throw Reject("source-pixel-format-unproven");
                        if (Text(stream, "color_range") != "pc")
                            throw Reject("source-full-range-unproven");
                        if (Text(stream, "color_transfer") != "bt709" ||
                            Text(stream, "color_primaries") != "bt709" ||
                            Text(stream, "color_space") != "bt709")
                            throw Reject("source-bt709-signalling-unproven");
                        if (!TryInt(stream, "width", out int width) ||
                            !TryInt(stream, "height", out int height) ||
                            width != current.CodedWidth || height != current.CodedHeight)
                            throw Reject("source-geometry-mismatch");
                        if (!TryExplicitRotation(stream, out int rotation,
                                allowAbsentAsZero: current.DisplayRotationDegrees == 0) ||
                            rotation != current.DisplayRotationDegrees)
                            throw Reject("source-rotation-unproven");
                        break;
                    case "audio":
                        if (++audios > 1 ||
                            Text(stream, "codec_name") != current.AudioCodec)
                            throw Reject("source-audio-layout-mismatch");
                        break;
                    case "data":
                        if (++dataTracks > 2)
                            throw Reject("source-ancillary-stream-unapproved");
                        break;
                    default:
                        throw Reject("source-stream-layout-unsupported");
                }
            }
            if (videos != 1)
                throw Reject("source-stream-layout-unsupported");
            if ((audios == 0 && current.AudioCodec is not null) ||
                (audios == 1 && current.AudioCodec is null))
                throw Reject("source-audio-layout-mismatch");
        }
    }

    /// <summary>
    /// The standard 720p software recipe is inherited; the fixed,
    /// full-to-limited BT.709 range conversion and explicit limited-range
    /// signaling plus stream-metadata stripping are added. No caller-provided
    /// filters, shell, streams or protocols.
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

        if (args.IndexOf("-color_primaries") < 0)
            throw new InvalidOperationException("Missing baseline Rec.709 output flags.");
        // Source stream-level tags can otherwise be implicitly mapped with
        // selected A/V streams, even though auxiliary data is unmapped.
        // Disable both global (baseline) and stream-level metadata copying.
        int streamMetadata = args.IndexOf("-map_metadata");
        if (streamMetadata < 0 || streamMetadata + 1 >= args.Count ||
            args[streamMetadata + 1] != "-1" ||
            args.IndexOf("-dn") < 0 || args.IndexOf("-sn") < 0 ||
            args.IndexOf("-map_chapters") < 0 ||
            args.Count(x => x == "-map") != 2 ||
            !args.Contains("0:v:0") || !args.Contains("0:a:0?"))
            throw new InvalidOperationException(
                "Full-range evaluation requires a fixed A/V-only output map.");
        args.InsertRange(streamMetadata + 2, ["-map_metadata:s", "-1"]);
        args.InsertRange(args.IndexOf("-color_primaries"), ["-color_range", "tv"]);
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

    /// <summary>
    /// Prove that the synthetic pilot actually generated a full-range test
    /// source BEFORE conversion. This never authorizes a real archive source:
    /// actual source admission still requires the persisted-revision contract
    /// and independent availability/exclusion/identity checks.
    /// </summary>
    public static void ValidateSyntheticInputProbeJson(
        ReadOnlySpan<byte> utf8, int width, int height, bool audio, long bytes)
    {
        if (width is not (720 or 1280) || height is not (720 or 1280) ||
            width == height || bytes is < 128 or > 32L * 1024 * 1024 ||
            utf8.Length is < 20 or > 65536)
            throw new InvalidDataException("Synthetic full-range source envelope rejected.");

        using JsonDocument document = JsonDocument.Parse(utf8.ToArray(),
            new JsonDocumentOptions { MaxDepth = 12 });
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("format", out JsonElement format) ||
            Text(format, "format_name") is not string container ||
            !container.Split(',').Contains("mp4", StringComparer.Ordinal) ||
            !TryLong(format, "size", out long statedBytes) ||
            statedBytes != bytes ||
            !TryDecimal(format, "duration", out decimal duration) ||
            duration is < 2.5m or > 3.5m ||
            !root.TryGetProperty("streams", out JsonElement streams) ||
            streams.ValueKind != JsonValueKind.Array ||
            streams.GetArrayLength() != (audio ? 2 : 1))
            throw new InvalidDataException("Synthetic source structure differs from its fixed recipe.");

        int videoCount = 0, audioCount = 0;
        foreach (JsonElement stream in streams.EnumerateArray())
        {
            switch (Text(stream, "codec_type"))
            {
                case "video":
                    if (++videoCount != 1 ||
                        Text(stream, "codec_name") != "h264" ||
                        Text(stream, "pix_fmt") != "yuvj420p" ||
                        Text(stream, "color_range") != "pc" ||
                        Text(stream, "color_transfer") != "bt709" ||
                        Text(stream, "color_primaries") != "bt709" ||
                        Text(stream, "color_space") != "bt709" ||
                        !TryInt(stream, "width", out int actualWidth) ||
                        actualWidth != width ||
                        !TryInt(stream, "height", out int actualHeight) ||
                        actualHeight != height ||
                        (stream.TryGetProperty("tags", out JsonElement tags) &&
                         Text(tags, "rotate") is not (null or "0")))
                        throw new InvalidDataException("Synthetic source lacks explicit full-range BT.709.");
                    if (stream.TryGetProperty("side_data_list", out JsonElement side) &&
                        side.ValueKind == JsonValueKind.Array &&
                        side.EnumerateArray().Any(item =>
                            item.TryGetProperty("rotation", out JsonElement rotation) &&
                            (rotation.ValueKind != JsonValueKind.Number ||
                             !rotation.TryGetInt32(out int n) || n != 0)))
                        throw new InvalidDataException("Synthetic source contains unexpected rotation.");
                    break;
                case "audio":
                    if (++audioCount != 1 || !audio ||
                        Text(stream, "codec_name") != "aac" ||
                        !TryInt(stream, "channels", out int channels) ||
                        channels is < 1 or > 2)
                        throw new InvalidDataException("Synthetic audio differs from the fixed source.");
                    break;
                default:
                    throw new InvalidDataException("Synthetic source has an unapproved stream.");
            }
        }
        if (videoCount != 1 || (audioCount == 1) != audio)
            throw new InvalidDataException("Synthetic source streams are incomplete.");
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

    private static bool TryExplicitRotation(JsonElement stream, out int rotation,
        bool allowAbsentAsZero = false)
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

        // Absent QuickTime rotation signalling means a default zero
        // display transform ONLY when the registered revision also says 0.
        // Never silently infer a nonzero rotation or overlook conflicts.
        if (value is null && allowAbsentAsZero)
            return true;
        if (value is not (0 or 90 or -90 or 180 or -180))
            return false;
        rotation = value.Value;
        return true;
    }
}
