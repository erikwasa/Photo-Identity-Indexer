using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web.Components;

public static class SmartCollectionPeoplePickerModel
{
    public static IReadOnlyList<ReviewPersonResponse> SelectedPeople(
        IEnumerable<ReviewPersonResponse> people,
        IReadOnlySet<string> selectedPersonIds) =>
        people
            .Where(person => selectedPersonIds.Contains(person.Id))
            .OrderBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.Id, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<ReviewPersonResponse> SearchCandidates(
        IEnumerable<ReviewPersonResponse> people,
        IReadOnlySet<string> selectedPersonIds,
        string? searchText)
    {
        string normalizedSearch = searchText?.Trim() ?? string.Empty;
        return people
            .Where(person => !person.HiddenFromSmartCollections)
            .Where(person => !selectedPersonIds.Contains(person.Id))
            .Where(person => normalizedSearch.Length == 0 ||
                person.DisplayName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(person => person.IsFavorite)
            .ThenBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(person => person.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static string FallbackInitial(ReviewPersonResponse person)
    {
        string name = person.DisplayName.Trim();
        return name.Length == 0 ? "?" : name[0].ToString().ToUpperInvariant();
    }
}
