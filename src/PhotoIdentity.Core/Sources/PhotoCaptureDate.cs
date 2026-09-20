using System.Globalization;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Sources;

public static class PhotoCaptureDatePrecisions
{
    public const string Year = "year";
    public const string Month = "month";
    public const string Day = "day";
}

public static class PhotoCaptureDateSources
{
    public const string Manual = "manual";
    public const string Extracted = "extracted";
}

public static class PhotoCaptureDateActionKinds
{
    public const string Set = "set";
    public const string Clear = "clear";
}

public sealed record PhotoCaptureDateRange
{
    public PhotoCaptureDateRange(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            throw new ArgumentException("Capture-date range start cannot be later than its end.");
        }

        From = from;
        To = to;
    }

    public DateOnly From { get; }
    public DateOnly To { get; }
}

public sealed record PhotoCaptureDateValue
{
    public PhotoCaptureDateValue(int year, int? month = null, int? day = null)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "Capture-date year must be between 1 and 9999.");
        }

        if (month is null && day is not null)
        {
            throw new ArgumentException("Capture-date day cannot be supplied without a month.", nameof(day));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Capture-date month must be between 1 and 12.");
        }

        if (day is not null)
        {
            _ = new DateOnly(year, month!.Value, day.Value);
        }

        Year = year;
        Month = month;
        Day = day;
    }

    public int Year { get; }
    public int? Month { get; }
    public int? Day { get; }

    public string Precision => Day is not null
        ? PhotoCaptureDatePrecisions.Day
        : Month is not null
            ? PhotoCaptureDatePrecisions.Month
            : PhotoCaptureDatePrecisions.Year;

    public PhotoCaptureDateRange InclusiveRange => Precision switch
    {
        PhotoCaptureDatePrecisions.Year => new(
            new DateOnly(Year, 1, 1),
            new DateOnly(Year, 12, 31)),
        PhotoCaptureDatePrecisions.Month => new(
            new DateOnly(Year, Month!.Value, 1),
            new DateOnly(
                Year,
                Month.Value,
                DateTime.DaysInMonth(Year, Month.Value))),
        _ => new(
            new DateOnly(Year, Month!.Value, Day!.Value),
            new DateOnly(Year, Month.Value, Day.Value)),
    };

    public static PhotoCaptureDateValue Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string text = value.Trim();

        if (text.Length == 4 &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int year))
        {
            return new PhotoCaptureDateValue(year);
        }

        if (text.Length == 7 &&
            text[4] == '-' &&
            int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year) &&
            int.TryParse(text.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int month))
        {
            return new PhotoCaptureDateValue(year, month);
        }

        if (text.Length == 10 &&
            text[4] == '-' &&
            text[7] == '-' &&
            int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year) &&
            int.TryParse(text.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month) &&
            int.TryParse(text.AsSpan(8, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int day))
        {
            return new PhotoCaptureDateValue(year, month, day);
        }

        throw new FormatException("Manual capture date must use YYYY, YYYY-MM, or YYYY-MM-DD.");
    }

    public override string ToString() => Precision switch
    {
        PhotoCaptureDatePrecisions.Year => Year.ToString("0000", CultureInfo.InvariantCulture),
        PhotoCaptureDatePrecisions.Month => FormattableString.Invariant($"{Year:0000}-{Month!.Value:00}"),
        _ => FormattableString.Invariant($"{Year:0000}-{Month!.Value:00}-{Day!.Value:00}"),
    };
}

public sealed record PhotoCaptureDateAction(
    long Id,
    AssetRevisionId RevisionId,
    string ActionKind,
    PhotoCaptureDateValue? Value,
    string Actor,
    DateTimeOffset CreatedAtUtc);

public sealed record PhotoCaptureDateState(
    AssetRevisionId RevisionId,
    DateTime? ExtractedTakenAtLocal,
    PhotoCaptureDateValue? ManualDate,
    PhotoCaptureDateRange? EffectiveRange,
    string? EffectiveSource,
    IReadOnlyList<PhotoCaptureDateAction> History);
