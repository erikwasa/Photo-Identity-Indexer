using System.Globalization;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public static class SmartCollectionDateModes
{
    public const string Any = "any";
    public const string Year = "year";
    public const string Month = "month";
    public const string Date = "date";
    public const string Range = "range";
}

public sealed record SmartCollectionDateEditorState(
    string Mode,
    string Year = "",
    string Month = "",
    string Date = "",
    string From = "",
    string To = "");

public static class SmartCollectionDateEditorModel
{
    public static bool TryBuildRange(
        string? mode,
        string? year,
        string? month,
        string? date,
        string? from,
        string? to,
        out SmartCollectionDateRangeRequest? range,
        out string? error)
    {
        range = null;
        error = null;
        string normalizedMode = string.IsNullOrWhiteSpace(mode)
            ? SmartCollectionDateModes.Any
            : mode.Trim().ToLowerInvariant();

        switch (normalizedMode)
        {
            case SmartCollectionDateModes.Any:
                return true;

            case SmartCollectionDateModes.Year:
                if (!TryParseYear(year, out int parsedYear))
                {
                    error = "Enter a four-digit year.";
                    return false;
                }

                string yearText = parsedYear.ToString("0000", CultureInfo.InvariantCulture);
                range = new SmartCollectionDateRangeRequest(
                    $"{yearText}-01-01",
                    $"{yearText}-12-31");
                return true;

            case SmartCollectionDateModes.Month:
                if (!TryParseMonth(month, out DateOnly parsedMonth))
                {
                    error = "Choose a valid year and month.";
                    return false;
                }

                range = new SmartCollectionDateRangeRequest(
                    new DateOnly(parsedMonth.Year, parsedMonth.Month, 1)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    new DateOnly(
                        parsedMonth.Year,
                        parsedMonth.Month,
                        DateTime.DaysInMonth(parsedMonth.Year, parsedMonth.Month))
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return true;

            case SmartCollectionDateModes.Date:
                if (!TryParseDate(date, out DateOnly parsedDate))
                {
                    error = "Choose a valid date.";
                    return false;
                }

                string exact = parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                range = new SmartCollectionDateRangeRequest(exact, exact);
                return true;

            case SmartCollectionDateModes.Range:
                if (!TryParseDate(from, out DateOnly parsedFrom))
                {
                    error = "Choose a valid From date.";
                    return false;
                }

                if (!TryParseDate(to, out DateOnly parsedTo))
                {
                    error = "Choose a valid To date.";
                    return false;
                }

                if (parsedFrom > parsedTo)
                {
                    error = "From date cannot be later than To date.";
                    return false;
                }

                range = new SmartCollectionDateRangeRequest(
                    parsedFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    parsedTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return true;

            default:
                error = "Choose a supported date mode.";
                return false;
        }
    }

    public static SmartCollectionDateEditorState FromRange(
        SmartCollectionDateRangeResponse? range)
    {
        if (range is null ||
            !TryParseDate(range.From, out DateOnly from) ||
            !TryParseDate(range.To, out DateOnly to) ||
            from > to)
        {
            return new SmartCollectionDateEditorState(SmartCollectionDateModes.Any);
        }

        if (from == new DateOnly(from.Year, 1, 1) &&
            to == new DateOnly(from.Year, 12, 31))
        {
            return new SmartCollectionDateEditorState(
                SmartCollectionDateModes.Year,
                Year: from.ToString("yyyy", CultureInfo.InvariantCulture));
        }

        if (from.Year == to.Year &&
            from.Month == to.Month &&
            from.Day == 1 &&
            to.Day == DateTime.DaysInMonth(from.Year, from.Month))
        {
            return new SmartCollectionDateEditorState(
                SmartCollectionDateModes.Month,
                Month: from.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        }

        if (from == to)
        {
            return new SmartCollectionDateEditorState(
                SmartCollectionDateModes.Date,
                Date: from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return new SmartCollectionDateEditorState(
            SmartCollectionDateModes.Range,
            From: from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To: to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public static SmartCollectionDateEditorState FromLegacyExpression(string? value)
    {
        string text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return new SmartCollectionDateEditorState(SmartCollectionDateModes.Any);
        }

        if (text.Length == 4 && TryParseYear(text, out int year))
        {
            return new SmartCollectionDateEditorState(
                SmartCollectionDateModes.Year,
                Year: year.ToString("0000", CultureInfo.InvariantCulture));
        }

        string normalized = text.Replace('/', '-');
        if (normalized.Length == 10 && TryParseDate(normalized, out DateOnly exact))
        {
            return new SmartCollectionDateEditorState(
                SmartCollectionDateModes.Date,
                Date: exact.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (TryParseLegacyRange(text, out DateOnly from, out DateOnly to))
        {
            return FromRange(new SmartCollectionDateRangeResponse(
                from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return new SmartCollectionDateEditorState(SmartCollectionDateModes.Any);
    }

    public static string Summary(SmartCollectionDateRangeResponse? range)
    {
        SmartCollectionDateEditorState state = FromRange(range);
        return state.Mode switch
        {
            SmartCollectionDateModes.Any => "Any taken date",
            SmartCollectionDateModes.Year => $"Taken in {state.Year}",
            SmartCollectionDateModes.Month => $"Taken in {state.Month}",
            SmartCollectionDateModes.Date => $"Taken on {state.Date}",
            _ => $"Taken {state.From} to {state.To}",
        };
    }

    private static bool TryParseYear(string? value, out int year)
    {
        year = 0;
        string text = value?.Trim() ?? string.Empty;
        return text.Length == 4 &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out year) &&
            year is >= 1 and <= 9999;
    }

    private static bool TryParseMonth(string? value, out DateOnly month)
    {
        string text = value?.Trim() ?? string.Empty;
        return DateOnly.TryParseExact(
            text + "-01",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out month);
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(
            value?.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private static bool TryParseLegacyRange(
        string value,
        out DateOnly from,
        out DateOnly to)
    {
        from = default;
        to = default;

        if (value.Length == 9 &&
            value[4] == '-' &&
            TryParseYear(value[..4], out int fromYear) &&
            TryParseYear(value[5..], out int toYear) &&
            fromYear <= toYear)
        {
            from = new DateOnly(fromYear, 1, 1);
            to = new DateOnly(toYear, 12, 31);
            return true;
        }

        string normalized = value.Replace('/', '-');
        if (normalized.Length == 21 &&
            normalized[10] == '-' &&
            TryParseDate(normalized[..10], out from) &&
            TryParseDate(normalized[11..], out to) &&
            from <= to)
        {
            return true;
        }

        return false;
    }
}
