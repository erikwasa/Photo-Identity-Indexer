using System.Globalization;

namespace PhotoIdentity.Web;

/// <summary>
/// Browser-local recording-time edits are textual on purpose: native
/// datetime-local inputs may display a different locale format and Blazor may
/// deliver their change values as DateTime rather than invariant text.
/// The wire API continues receiving an explicit-offset ISO 8601 instant.
/// </summary>
public static class VideoDetailsPresentation
{
    public const string LocalRecordingTimePattern = "yyyy-MM-dd HH:mm";

    public static string FormatLocalRecordingTimeInput(DateTimeOffset? instant) =>
        instant?.ToLocalTime().ToString(LocalRecordingTimePattern, CultureInfo.InvariantCulture) ?? "";

    public static bool TryParseLocalRecordingTime(
        string? input, TimeZoneInfo timeZone, out DateTimeOffset instant, out string? error)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        instant = default;
        if (!DateTime.TryParseExact(input?.Trim(), LocalRecordingTimePattern,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime wallClock))
        {
            error = "Enter a valid local recording date and time (YYYY-MM-DD HH:MM).";
            return false;
        }

        // Never guess which instant an ambiguous fall-back clock reading meant,
        // or normalize a clock reading that never existed during spring-forward.
        if (timeZone.IsAmbiguousTime(wallClock) || timeZone.IsInvalidTime(wallClock))
        {
            error = "This local time is ambiguous or does not exist because of daylight-saving time. Choose another time.";
            return false;
        }

        instant = new DateTimeOffset(wallClock, timeZone.GetUtcOffset(wallClock));
        error = null;
        return true;
    }

    /// <summary>Use the encoded video stream bitrate only; it is not a quality score.</summary>
    public static string FormatVideoBitrate(long? bitsPerSecond) =>
        bitsPerSecond is > 0
            ? (bitsPerSecond.Value / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + " Mb/s"
            : "Unknown";

    public static string FormatAudioDetails(
        string? codec, int? channels, string? layout, int? sampleRateHz)
    {
        string? friendlyCodec = codec?.ToLowerInvariant() switch
        {
            "aac" => "AAC",
            "mp3" => "MP3",
            "opus" => "Opus",
            "pcm_s16le" => "PCM (16-bit)",
            null or "" => null,
            _ => codec
        };
        string? friendlyLayout = layout?.Trim().ToLowerInvariant() switch
        {
            "mono" => "Mono",
            "stereo" => "Stereo",
            null or "" => channels switch { 1 => "Mono", 2 => "Stereo", > 2 => channels + " channels", _ => null },
            _ => layout?.Trim()
        };
        string? sampling = sampleRateHz is > 0
            ? (sampleRateHz.Value / 1000d).ToString("0.###", CultureInfo.InvariantCulture) + " kHz"
            : null;
        string[] known = [friendlyCodec, friendlyLayout, sampling]
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!)
            .ToArray();
        return known.Length > 0 ? string.Join(" · ", known) : "Unknown";
    }

    public static string FormatFrameRate(string? rational)
    {
        string[] parts = (rational ?? "").Split('/');
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out double numerator) ||
            !double.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out double denominator) ||
            numerator <= 0 || denominator <= 0 ||
            !double.IsFinite(numerator / denominator))
            return "Unknown";

        double nearest = Math.Round(numerator / denominator, 0, MidpointRounding.AwayFromZero);
        return nearest.ToString("0", CultureInfo.InvariantCulture) + " fps";
    }
}
