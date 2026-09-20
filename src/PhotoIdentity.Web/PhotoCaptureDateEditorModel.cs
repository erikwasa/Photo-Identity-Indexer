using System.Globalization;

namespace PhotoIdentity.Web;

public static class PhotoCaptureDateEditorModel
{
    public static bool TryNormalize(
        string? value,
        out string normalized,
        out string? error)
    {
        string text = value?.Trim() ?? string.Empty;
        normalized = string.Empty;

        if (text.Length == 4 &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int year) &&
            year is >= 1 and <= 9999)
        {
            normalized = year.ToString("0000", CultureInfo.InvariantCulture);
            error = null;
            return true;
        }

        if (text.Length == 7 &&
            DateOnly.TryParseExact(
                text + "-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly month))
        {
            normalized = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            error = null;
            return true;
        }

        if (text.Length == 10 &&
            DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly day))
        {
            normalized = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            error = null;
            return true;
        }

        error = "Use YYYY, YYYY-MM, or YYYY-MM-DD with a valid calendar date.";
        return false;
    }

    public static string SourceLabel(string? source) => source switch
    {
        "manual" => "Manual override",
        "extracted" => "Extracted metadata",
        _ => "No capture date",
    };

    public static string PrecisionLabel(string? precision) => precision switch
    {
        "year" => "Year only",
        "month" => "Year and month",
        "day" => "Exact date",
        "timestamp" => "Exact timestamp",
        _ => "Unknown precision",
    };

    public static string EffectiveLabel(PhotoIdentity.Web.Contracts.PhotoCaptureDateResponse? state)
    {
        if (state is null || string.IsNullOrWhiteSpace(state.EffectiveFrom))
        {
            return "No capture date";
        }

        if (state.Source == "manual" && !string.IsNullOrWhiteSpace(state.ManualValue))
        {
            return state.ManualValue;
        }

        return state.ExtractedTakenAtLocal is DateTime extracted
            ? DateTime.SpecifyKind(extracted, DateTimeKind.Unspecified)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : state.EffectiveFrom;
    }
}
