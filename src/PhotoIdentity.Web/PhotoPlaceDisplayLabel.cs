using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public static class PhotoPlaceDisplayLabel
{
    private static readonly string[] CitySuffixes =
    [
        " city",
        " municipality",
    ];

    public static string Format(PhotoPlaceResponse place)
    {
        ArgumentNullException.ThrowIfNull(place);

        string[] segments = place.Value
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string specific = string.IsNullOrWhiteSpace(place.Name)
            ? segments.LastOrDefault() ?? place.Value
            : place.Name.Trim();

        if (segments.Length <= 1)
        {
            return specific;
        }

        if (string.Equals(place.Source, "automatic", StringComparison.OrdinalIgnoreCase))
        {
            string? city = FindProviderAwareCity(segments);
            if (!string.IsNullOrWhiteSpace(city) &&
                !string.Equals(city, specific, StringComparison.OrdinalIgnoreCase))
            {
                return $"{city} · {specific}";
            }
        }

        string parent = segments[^2];
        return string.Equals(parent, specific, StringComparison.OrdinalIgnoreCase)
            ? specific
            : $"{parent} · {specific}";
    }

    private static string? FindProviderAwareCity(IReadOnlyList<string> segments)
    {
        // Country is first and the populated locality is last. Administrative depth in between
        // varies by country, so look for provider-derived city/municipality semantics rather than
        // assuming a fixed segment position.
        for (int index = segments.Count - 2; index >= 1; index--)
        {
            string segment = segments[index].Trim();

            if (segment.EndsWith(" stad", StringComparison.OrdinalIgnoreCase))
            {
                string city = segment[..^" stad".Length].Trim();
                if (city.EndsWith('s') &&
                    HasSiblingAdministrativePrefix(segments, index, city))
                {
                    city = city[..^1];
                }

                return city;
            }

            if (segment.StartsWith("City of ", StringComparison.OrdinalIgnoreCase))
            {
                return segment["City of ".Length..].Trim();
            }

            foreach (string suffix in CitySuffixes)
            {
                if (segment.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return segment[..^suffix.Length].Trim();
                }
            }
        }

        return null;
    }

    private static bool HasSiblingAdministrativePrefix(
        IReadOnlyList<string> segments,
        int cityIndex,
        string cityWithGenitiveS)
    {
        for (int index = 1; index < segments.Count - 1; index++)
        {
            if (index == cityIndex)
            {
                continue;
            }

            if (segments[index].StartsWith(cityWithGenitiveS + " ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
