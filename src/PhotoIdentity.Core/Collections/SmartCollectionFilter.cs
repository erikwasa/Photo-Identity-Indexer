using System.Globalization;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Places;
using PhotoIdentity.Core.Tags;

namespace PhotoIdentity.Core.Collections;

public static class SmartCollectionMatchModes
{
    public const string Any = "any";
    public const string All = "all";

    public static string Normalize(string? value, string parameterName)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? All
            : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            Any => Any,
            All => All,
            _ => throw new ArgumentException(
                $"Unsupported match mode '{value}'. Use 'any' or 'all'.",
                parameterName),
        };
    }
}

public sealed record SmartCollectionGeoBounds
{
    public SmartCollectionGeoBounds(double south, double west, double north, double east)
    {
        if (!double.IsFinite(south) || south is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(south));
        }

        if (!double.IsFinite(north) || north is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(north));
        }

        if (!double.IsFinite(west) || west is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(west));
        }

        if (!double.IsFinite(east) || east is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(east));
        }

        if (south > north)
        {
            throw new ArgumentException("South latitude cannot be greater than north latitude.");
        }

        if (west > east)
        {
            throw new ArgumentException("West longitude cannot be greater than east longitude in the initial bounds model.");
        }

        South = south;
        West = west;
        North = north;
        East = east;
    }

    public double South { get; }
    public double West { get; }
    public double North { get; }
    public double East { get; }
}

public sealed record SmartCollectionDateRange
{
    public SmartCollectionDateRange(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            throw new ArgumentException("The taken-date start cannot be later than the end date.");
        }

        From = from;
        To = to;
    }

    public DateOnly From { get; }
    public DateOnly To { get; }

    public static SmartCollectionDateRange Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string text = value.Trim();

        if (text.Length == 4 && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int year))
        {
            ValidateYear(year);
            return new SmartCollectionDateRange(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        }

        if (text.Length == 9 && text[4] == '-' &&
            int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int fromYear) &&
            int.TryParse(text.AsSpan(5, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int toYear))
        {
            ValidateYear(fromYear);
            ValidateYear(toYear);
            return new SmartCollectionDateRange(
                new DateOnly(fromYear, 1, 1),
                new DateOnly(toYear, 12, 31));
        }

        if (text.Length == 21 && text[4] == '/' && text[7] == '/' && text[10] == '-' && text[15] == '/' && text[18] == '/')
        {
            DateOnly from = ParseDate(text[..10]);
            DateOnly to = ParseDate(text[11..]);
            return new SmartCollectionDateRange(from, to);
        }

        if (text.Length == 10 && text[4] == '/' && text[7] == '/')
        {
            DateOnly day = ParseDate(text);
            return new SmartCollectionDateRange(day, day);
        }

        throw new FormatException(
            "Taken date must use YYYY, YYYY-YYYY, YYYY/MM/DD, or YYYY/MM/DD-YYYY/MM/DD.");
    }

    private static DateOnly ParseDate(string value) => DateOnly.ParseExact(
        value,
        "yyyy/MM/dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None);

    private static void ValidateYear(int year)
    {
        if (year is < 1 or > 9999)
        {
            throw new FormatException("Taken-date year must be between 0001 and 9999.");
        }
    }
}

public sealed record SmartCollectionFilter
{
    public SmartCollectionFilter(
        IEnumerable<PersonId>? people = null,
        string? peopleMatch = null,
        IEnumerable<string>? tags = null,
        string? tagMatch = null,
        SmartCollectionGeoBounds? location = null,
        SmartCollectionDateRange? taken = null,
        string? locationPlace = null)
    {
        People = (people ?? []).Distinct().ToArray();
        if (People.Count > 100)
        {
            throw new ArgumentException("A smart collection can contain at most 100 people.", nameof(people));
        }

        PeopleMatch = SmartCollectionMatchModes.Normalize(peopleMatch, nameof(peopleMatch));

        PhotoTagPath[] parsedTags = (tags ?? [])
            .Select(PhotoTagPath.Parse)
            .ToArray();
        string normalizedTagMatch = SmartCollectionMatchModes.Normalize(tagMatch, nameof(tagMatch));
        PhotoTagPath[] legacyPlaceTags = parsedTags
            .Where(PhotoPlacePath.IsReservedTagPath)
            .DistinctBy(path => path.NormalizedValue, StringComparer.Ordinal)
            .ToArray();
        if (legacyPlaceTags.Length > 1)
        {
            throw new ArgumentException(
                "A legacy Smart Collection cannot migrate more than one distinct Places tag into the single named-place Location criterion.",
                nameof(tags));
        }

        PhotoTagPath[] genericTags = parsedTags
            .Where(path => !PhotoPlacePath.IsReservedTagPath(path))
            .ToArray();
        if (legacyPlaceTags.Length == 1 && genericTags.Length > 0 &&
            normalizedTagMatch == SmartCollectionMatchModes.Any)
        {
            throw new ArgumentException(
                "A legacy Smart Collection using tagMatch 'any' cannot losslessly migrate a Places tag alongside generic tags because Location and Tags combine with AND semantics.",
                nameof(tags));
        }

        Tags = genericTags
            .Select(path => path.NormalizedValue)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (Tags.Count > 100)
        {
            throw new ArgumentException("A smart collection can contain at most 100 tags.", nameof(tags));
        }

        TagMatch = normalizedTagMatch;
        Location = location;

        string? explicitLocationPlace = string.IsNullOrWhiteSpace(locationPlace)
            ? null
            : PhotoPlacePath.Parse(locationPlace).CanonicalNormalizedValue;
        string? legacyLocationPlace = legacyPlaceTags.Length == 0
            ? null
            : PhotoPlacePath.FromCanonicalTagPath(legacyPlaceTags[0]).CanonicalNormalizedValue;
        if (explicitLocationPlace is not null && legacyLocationPlace is not null &&
            !string.Equals(explicitLocationPlace, legacyLocationPlace, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The legacy Places tag and named-place Location criterion refer to different canonical places.",
                nameof(locationPlace));
        }

        LocationPlace = explicitLocationPlace ?? legacyLocationPlace;
        Taken = taken;
    }

    public IReadOnlyList<PersonId> People { get; }
    public string PeopleMatch { get; }
    public IReadOnlyList<string> Tags { get; }
    public string TagMatch { get; }
    public SmartCollectionGeoBounds? Location { get; }
    public string? LocationPlace { get; }
    public SmartCollectionDateRange? Taken { get; }
}
