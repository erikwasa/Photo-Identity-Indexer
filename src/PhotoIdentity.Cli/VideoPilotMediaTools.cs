using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

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
    bool HasLocation);

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
        string[] keys = tags.ValueKind == JsonValueKind.Object
            ? tags.EnumerateObject().Select(item => item.Name).ToArray()
            : [];
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
                             name.Contains("gps", StringComparison.OrdinalIgnoreCase)));
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
    private readonly string _ffmpeg;
    private readonly TimeSpan _timeout;

    public VideoPilotMediaTools(string ffprobe, string ffmpeg, TimeSpan timeout)
    {
        _ffprobe = ValidateExecutable(ffprobe);
        _ffmpeg = ValidateExecutable(ffmpeg);
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
            "format=format_name,duration:format_tags=creation_time,location,com.apple.quicktime.creationdate,com.apple.quicktime.location.ISO6709:stream=codec_type,codec_name,profile,width,height,pix_fmt,avg_frame_rate,color_transfer:stream_tags=rotate:stream_side_data=rotation",
            "-of", "json", "-i", sourcePath
        ], cancellationToken);
        if (json.Length > 1024 * 1024)
            throw new InvalidDataException("FFprobe metadata exceeded the configured size limit.");
        return VideoPilotMetadataParser.Parse(json);
    }

    public async Task RenderPosterAsync(string sourcePath, string temporaryJpegPath, CancellationToken cancellationToken)
    {
        await RunAsync(_ffmpeg, [
            "-nostdin", "-hide_banner", "-loglevel", "error",
            "-protocol_whitelist", "file", "-autorotate", "-i", sourcePath,
            "-map", "0:v:0", "-frames:v", "1", "-an", "-sn", "-dn",
            "-vf", "scale=1280:1280:force_original_aspect_ratio=decrease,format=yuv420p",
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
        try
        {
            if (!process.Start()) throw new IOException("Video utility could not be launched.");
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
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
