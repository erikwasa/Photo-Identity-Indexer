namespace PhotoIdentity.Web;

public static class SlideshowLibraryFilter
{
    public static string NormalizeQuery(string? query) => query?.Trim() ?? string.Empty;

    public static IReadOnlyList<SlideshowLibraryItem> ByName(
        IReadOnlyList<SlideshowLibraryItem> items,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(items);

        string normalizedQuery = NormalizeQuery(query);
        if (normalizedQuery.Length == 0)
        {
            return items;
        }

        return items
            .Where(item => item.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
