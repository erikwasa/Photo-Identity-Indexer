namespace PhotoIdentity.Web;

public static class SmartCollectionPlaceLabel
{
    public static string? FormatCompact(string? effectivePlace)
    {
        if (string.IsNullOrWhiteSpace(effectivePlace))
        {
            return null;
        }

        string[] segments = effectivePlace
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !string.Equals(segment, "Places", StringComparison.OrdinalIgnoreCase))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();

        return segments.Length switch
        {
            0 => null,
            1 => segments[0],
            _ when string.Equals(segments[0], segments[^1], StringComparison.OrdinalIgnoreCase) => segments[^1],
            _ => $"{segments[^1]} · {segments[0]}",
        };
    }
}
