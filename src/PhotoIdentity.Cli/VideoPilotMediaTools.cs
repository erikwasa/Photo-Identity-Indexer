using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PhotoIdentity.Cli;

/// <summary>
/// Technical evidence extracted from FFprobe. Missing tags stay unknown, not synthesized
/// from filesystem timestamps; visual interpretation of HLG needs a separate tone-map test.
/// </summary>
public sealed record VideoPilotMetadata(
    string? Container,
    string? VideoCodec,
    string? Profile,
    string? PixelFormat,
    string? ColorTransfer,
    string? AudioCodec,
    int? Width,
    int? Height,
    int? RotationDegrees,
    double? DurationSeconds,
    string? AverageFrameRate,
    bool HasCreationTime,
    bool HasLocation)
{
    // These fields may contain private location and capture information.
    // They are used exclusively for opt-in local catalogue persistence.
    // Never include them in the pilot's serialized JSON diagnostic report.
    [JsonIgnore]
    public DateTimeOffset? CaptureAtUtc { get; init; }
    [JsonIgnore]
    public string? CaptureTimeProvenance { get; init; }
    [JsonIgnore]
    public double? GpsLatitude { get; init; }
    [JsonIgnore]
    public double? GpsLongitude { get; init; }
    [JsonIgnore]
    public string? CameraMake { get; init; }
    [JsonIgnore]
    public string? CameraModel { get; init; }

    // Only explicit source stream colour signalling qualifies HLG preview.
    // Never infer BT.2020 from camera, codec or pixel format.
    public string? ColorPrimaries { get; init; }
    public string? ColorSpace { get; init; }
}

public static class VideoPilotMetadataParser
{
    public static VideoPilotMetadata Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        JsonElement root = doc.RootElement;
        JsonElement format = Child(root, "format");
        JsonElement video = default;
        JsonElement audio = default;
        JsonElement streams = Child(root, "streams");
        if (streams.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                switch (Text(stream, "codec_type"))
                {
                    case "video" when video.ValueKind == JsonValueKind.Undefined:
                        video = stream;
                        break;
                    case "audio" when audio.ValueKind == JsonValueKind.Undefined:
                        audio = stream;
                        break;
                }
            }
        }

        int? rotation = Integer(Child(video, "tags"), "rotate");
        JsonElement sideData = Child(video, "side_data_list");
        if (sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in sideData.EnumerateArray())
            {
                rotation = Integer(entry, "rotation") ?? rotation;
            }
        }

        JsonElement tags = Child(format, "tags");
        JsonElement streamTags = Child(video, "tags");
        string[] keys = Keys(tags).Concat(Keys(streamTags)).ToArray();

        (DateTimeOffset? capture, string? provenance) = SelectCapture(tags, streamTags);
        (double? latitude, double? longitude) = SelectCoordinates(tags, streamTags);

        return new VideoPilotMetadata(
            Text(format, "format_name"),
            Text(video, "codec_name"),
            Text(video, "profile"),
            Text(video, "pix_fmt"),
            Text(video, "color_transfer"),
            Text(audio, "codec_name"),
            Integer(video, "width"),
            Integer(video, "height"),
            rotation,
            Double(format, "duration"),
            Text(video, "avg_frame_rate"),
            keys.Any(name => name.Contains("creation", StringComparison.OrdinalIgnoreCase)),
            keys.Any(name => name.Contains("location", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("gps", StringComparison.OrdinalIgnoreCase)))
        {
            CaptureAtUtc = capture,
            CaptureTimeProvenance = provenance,
            GpsLatitude = latitude,
            GpsLongitude = longitude,
            CameraMake = SelectShortTag(tags, streamTags, "com.apple.quicktime.make", "make"),
            CameraModel = SelectShortTag(tags, streamTags, "com.apple.quicktime.model", "model"),
            ColorPrimaries = Text(video, "color_primaries"),
            ColorSpace = Text(video, "color_space")
        };
    }

    private static IEnumerable<string> Keys(JsonElement tags) =>
        tags.ValueKind == JsonValueKind.Object
            ? tags.EnumerateObject().Select(static item => item.Name)
            : [];

    private static string? Tag(JsonElement tags, string name)
    {
        if (tags.ValueKind != JsonValueKind.Object) return null;
        foreach (JsonProperty property in tags.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }
        return null;
    }

    private static (DateTimeOffset?, string?) SelectCapture(JsonElement format, JsonElement video)
    {
        // Camera/QuickTime original capture tag has priority over generic muxing
        // creation_time. Only explicit timezone offsets are accepted. No timezone
        // is inferred from Windows locale, file mtime, or QuickTime's ambiguous clock.
        foreach ((JsonElement scope, string label) in new[]
        {
            (format, "format"),
            (video, "video")
        })
        {
            string? raw = Tag(scope, "com.apple.quicktime.creationdate");
            if (TryCaptureTimestamp(raw, out DateTimeOffset capture))
                return (capture, label + ":com.apple.quicktime.creationdate");
        }
        foreach ((JsonElement scope, string label) in new[]
        {
            (format, "format"),
            (video, "video")
        })
        {
            string? raw = Tag(scope, "creation_time");
            if (TryCaptureTimestamp(raw, out DateTimeOffset capture))
                return (capture, label + ":creation_time");
        }
        return (null, null);
    }

    private static bool TryCaptureTimestamp(string? input, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 80)
            return false;
        string raw = input.Trim();

        // Require YYYY-MM-DD (or the QuickTime YYYY:MM:DD variant), seconds
        // and an explicit Z or signed timezone offset. Reject floating local times.
        if (!Regex.IsMatch(raw, @"^\d{4}[-:]\d{2}[-:]\d{2}[T ]\d{2}:\d{2}:\d{2}",
                RegexOptions.CultureInvariant))
            return false;
        if (raw.EndsWith('Z'))
            raw = raw[..^1] + "+00:00";
        else if (Regex.IsMatch(raw, @"[+-]\d{4}$", RegexOptions.CultureInvariant))
            raw = raw.Insert(raw.Length - 2, ":");
        if (!Regex.IsMatch(raw, @"[+-]\d{2}:\d{2}$", RegexOptions.CultureInvariant))
            return false;

        string[] accepted =
        [
            "yyyy-MM-dd'T'HH:mm:sszzz",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            "yyyy-MM-dd HH:mm:sszzz",
            "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz",
            "yyyy:MM:dd HH:mm:sszzz",
            "yyyy:MM:dd HH:mm:ss.FFFFFFFzzz"
        ];
        if (!DateTimeOffset.TryParseExact(raw, accepted, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTimeOffset parsed))
            return false;

        result = parsed.ToUniversalTime();
        return true;
    }

    private static (double?, double?) SelectCoordinates(JsonElement format, JsonElement video)
    {
        // Accept only a defensible ISO-6709 decimal-degree subset used by iPhone
        // QuickTime tags. Ambiguous DMS, non-ISO pairs, altitude-only, malformed or
        // out-of-range values remain unknown rather than being guessed.
        foreach (JsonElement tags in new[] { format, video })
        {
            foreach (string name in new[]
                { "com.apple.quicktime.location.ISO6709", "location" })
            {
                if (TryCoordinates(Tag(tags, name), out double latitude, out double longitude))
                    return (latitude, longitude);
            }
        }
        return (null, null);
    }

    private static bool TryCoordinates(string? input, out double latitude, out double longitude)
    {
        latitude = longitude = default;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 100) return false;
        Match match = Regex.Match(input.Trim(),
            @"^([+-]\d{2}(?:\.\d+)?)([+-]\d{3}(?:\.\d+)?)(?:[+-]\d+(?:\.\d+)?)?/$",
            RegexOptions.CultureInvariant);
        if (!match.Success ||
            !double.TryParse(match.Groups[1].Value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out latitude) ||
            !double.TryParse(match.Groups[2].Value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out longitude) ||
            !double.IsFinite(latitude) || !double.IsFinite(longitude) ||
            latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return false;
        return true;
    }

    private static string? SelectShortTag(
        JsonElement format, JsonElement video, string preferredName, string alternateName)
    {
        foreach (JsonElement tags in new[] { format, video })
        {
            foreach (string name in new[] { preferredName, alternateName })
            {
                string? value = Tag(tags, name)?.Trim();
                if (value is { Length: > 0 and <= 128 } &&
                    !value.Any(char.IsControl))
                    return value;
            }
        }
        return null;
    }

    private static JsonElement Child(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement value)
            ? value : default;

    private static string? Text(JsonElement item, string name)
    {
        JsonElement value = Child(item, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int? Integer(JsonElement item, string name)
    {
        JsonElement value = Child(item, name);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n)) return n;
        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)
            ? n : null;
    }

    private static double? Double(JsonElement item, string name)
    {
        string? text = Text(item, name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) &&
               double.IsFinite(n) && n >= 0 ? n : null;
    }
}

public sealed class VideoPilotMediaTools
{
    private readonly string _ffprobe;
    private readonly string? _ffmpeg;
    private readonly TimeSpan _timeout;

    public VideoPilotMediaTools(string ffprobe, string ffmpeg, TimeSpan timeout)
        : this(ffprobe, timeout)
    {
        _ffmpeg = ValidateExecutable(ffmpeg);
    }

    // Metadata-only opt-in ingest needs ffprobe, never ffmpeg/poster rendering.
    public VideoPilotMediaTools(string ffprobe, TimeSpan timeout)
    {
        _ffprobe = ValidateExecutable(ffprobe);
        if (timeout < TimeSpan.FromSeconds(1) || timeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _timeout = timeout;
    }

    public static string ValidateExecutable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new ArgumentException("FFmpeg executable must be an existing absolute path.");
        return Path.GetFullPath(path);
    }

    public async Task<VideoPilotMetadata> ProbeAsync(string sourcePath, CancellationToken cancellationToken)
    {
        string json = await RunAsync(_ffprobe, [
            "-v", "error", "-protocol_whitelist", "file",
            "-probesize", "1048576", "-analyzeduration", "1500000",
            "-show_format", "-show_streams",
            "-show_entries",
            "format=format_name,duration:format_tags=creation_time,location,make,model,com.apple.quicktime.creationdate,com.apple.quicktime.location.ISO6709,com.apple.quicktime.make,com.apple.quicktime.model:stream=codec_type,codec_name,profile,width,height,pix_fmt,avg_frame_rate,color_transfer,color_primaries,color_space:stream_tags=rotate,creation_time,location,make,model,com.apple.quicktime.creationdate,com.apple.quicktime.location.ISO6709,com.apple.quicktime.make,com.apple.quicktime.model:stream_side_data=rotation",
            "-of", "json", "-i", sourcePath
        ], cancellationToken);
        if (json.Length > 1024 * 1024)
            throw new InvalidDataException("FFprobe metadata exceeded the configured size limit.");
        return VideoPilotMetadataParser.Parse(json);
    }

    // This is an isolated evaluation filter, NOT the durable SDR poster profile.
    // zscale operates in linear RGB, then Mobius compresses highlights into
    // Rec.709 for the bounded JPEG. Requires explicitly signalled BT.2020 HLG.
    // FFmpeg must have zscale and tonemap filters; missing filters fail closed.
    public const string HlgPreviewFilter =
        "setparams=color_primaries=bt2020:color_trc=arib-std-b67:colorspace=bt2020nc," +
        "zscale=t=linear:npl=100,format=gbrpf32le," +
        "tonemap=mobius:param=0.3:desat=0," +
        "zscale=p=709:t=709:m=709:r=limited,format=yuv420p," +
        "scale=1280:1280:force_original_aspect_ratio=decrease";

    public Task RenderPosterAsync(string sourcePath, string temporaryJpegPath,
        CancellationToken cancellationToken) =>
        RenderJpegAsync(sourcePath, temporaryJpegPath,
            "scale=1280:1280:force_original_aspect_ratio=decrease,format=yuv420p",
            cancellationToken);

    public Task RenderHlgPreviewAsync(string sourcePath, string temporaryJpegPath,
        CancellationToken cancellationToken) =>
        RenderJpegAsync(sourcePath, temporaryJpegPath, HlgPreviewFilter, cancellationToken);

    private async Task RenderJpegAsync(string sourcePath, string temporaryJpegPath,
        string videoFilter, CancellationToken cancellationToken)
    {
        if (_ffmpeg is null)
            throw new InvalidOperationException("Poster rendering requires a separately configured ffmpeg executable.");
        await RunAsync(_ffmpeg, [
            "-nostdin", "-hide_banner", "-loglevel", "error",
            "-protocol_whitelist", "file", "-autorotate", "-i", sourcePath,
            "-map", "0:v:0", "-frames:v", "1", "-an", "-sn", "-dn",
            "-vf", videoFilter,
            "-q:v", "4", "-f", "image2", "-y", temporaryJpegPath
        ], cancellationToken);
        FileInfo file = new(temporaryJpegPath);
        if (!file.Exists || file.Length < 100 || file.Length > 8 * 1024 * 1024)
            throw new InvalidDataException("FFmpeg produced an invalid or oversized poster.");
        await using FileStream stream = new(temporaryJpegPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] signature = new byte[3];
        if (await stream.ReadAsync(signature, cancellationToken) != 3 ||
            signature[0] != 0xff || signature[1] != 0xd8 || signature[2] != 0xff)
            throw new InvalidDataException("FFmpeg output was not a JPEG.");
    }

    private async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
    {
        ProcessStartInfo start = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = new() { StartInfo = start };
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_timeout);
        bool started = false;
        try
        {
            started = process.Start();
            if (!started) throw new IOException("Video utility could not be launched.");
            Task<string> standard = process.StandardOutput.ReadToEndAsync();
            Task<string> errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(deadline.Token);
            string output = await standard.WaitAsync(deadline.Token);
            _ = await errors.WaitAsync(deadline.Token); // Never return raw ffmpeg diagnostics (may include file paths).
            if (process.ExitCode != 0)
                throw new InvalidDataException("Video utility failed to process the local input.");
            return output;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Video utility exceeded the permitted execution time.");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
