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
