using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web.Components;

public static class PlacePickerModel
{
    public static IReadOnlyList<PhotoPlaceDefinitionResponse> SearchCandidates(
        IEnumerable<PhotoPlaceDefinitionResponse> places,
        string? searchText)
    {
        string normalizedSearch = searchText?.Trim() ?? string.Empty;

        return places
            .Where(place =>
                normalizedSearch.Length == 0 ||
                place.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                place.Value.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                (place.ParentValue?.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(place => place.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(place => place.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static int MoveActiveIndex(
        int currentIndex,
        int resultCount,
        string? key)
    {
        if (resultCount <= 0)
        {
            return -1;
        }

        return key switch
        {
            "ArrowDown" => currentIndex < 0 || currentIndex >= resultCount - 1
                ? 0
                : currentIndex + 1,
            "ArrowUp" => currentIndex <= 0 || currentIndex >= resultCount
                ? resultCount - 1
                : currentIndex - 1,
            "Home" => 0,
            "End" => resultCount - 1,
            _ => currentIndex >= 0 && currentIndex < resultCount ? currentIndex : -1,
        };
    }
}
