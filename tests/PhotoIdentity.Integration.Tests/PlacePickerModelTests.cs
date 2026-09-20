using PhotoIdentity.Web.Components;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PlacePickerModelTests
{
    [Fact]
    public void Search_matches_leaf_name_canonical_path_and_parent_path_case_insensitively()
    {
        PhotoPlaceDefinitionResponse[] places =
        [
            Place("1", "Sweden", "Sweden"),
            Place("2", "Stockholm region", "Sweden/Stockholm region", "1", "Sweden"),
            Place("3", "Norrtälje", "Sweden/Stockholm region/Norrtälje", "2", "Sweden/Stockholm region"),
            Place("4", "Paris", "France/Île-de-France/Paris", "5", "France/Île-de-France"),
        ];

        IReadOnlyList<PhotoPlaceDefinitionResponse> matches =
            PlacePickerModel.SearchCandidates(places, "STOCKHOLM");

        Assert.Equal(
            ["Sweden/Stockholm region", "Sweden/Stockholm region/Norrtälje"],
            matches.Select(place => place.Value));
    }

    [Fact]
    public void Duplicate_leaf_names_keep_distinct_canonical_paths_and_parent_labels()
    {
        PhotoPlaceDefinitionResponse[] places =
        [
            Place("1", "Springfield", "USA/Illinois/Springfield", "10", "USA/Illinois"),
            Place("2", "Springfield", "USA/Massachusetts/Springfield", "20", "USA/Massachusetts"),
        ];

        IReadOnlyList<PhotoPlaceDefinitionResponse> matches =
            PlacePickerModel.SearchCandidates(places, "springfield");

        Assert.Equal(2, matches.Count);
        Assert.Equal("USA/Illinois/Springfield", matches[0].Value);
        Assert.Equal("USA/Illinois", matches[0].ParentValue);
        Assert.Equal("USA/Massachusetts/Springfield", matches[1].Value);
        Assert.Equal("USA/Massachusetts", matches[1].ParentValue);
    }

    [Fact]
    public void Search_order_is_deterministic_without_rewriting_canonical_values()
    {
        PhotoPlaceDefinitionResponse[] places =
        [
            Place("z", "Visby", "Sweden/Gotland/Visby", "2", "Sweden/Gotland"),
            Place("a", "Gotland", "Sweden/Gotland", "1", "Sweden"),
            Place("b", "Sweden", "Sweden"),
        ];

        IReadOnlyList<PhotoPlaceDefinitionResponse> matches =
            PlacePickerModel.SearchCandidates(places, "");

        Assert.Equal(
            ["Sweden", "Sweden/Gotland", "Sweden/Gotland/Visby"],
            matches.Select(place => place.Value));
    }

    [Fact]
    public void Keyboard_navigation_wraps_and_supports_home_end()
    {
        Assert.Equal(0, PlacePickerModel.MoveActiveIndex(-1, 3, "ArrowDown"));
        Assert.Equal(1, PlacePickerModel.MoveActiveIndex(0, 3, "ArrowDown"));
        Assert.Equal(0, PlacePickerModel.MoveActiveIndex(2, 3, "ArrowDown"));
        Assert.Equal(2, PlacePickerModel.MoveActiveIndex(-1, 3, "ArrowUp"));
        Assert.Equal(2, PlacePickerModel.MoveActiveIndex(0, 3, "ArrowUp"));
        Assert.Equal(0, PlacePickerModel.MoveActiveIndex(2, 3, "Home"));
        Assert.Equal(2, PlacePickerModel.MoveActiveIndex(0, 3, "End"));
        Assert.Equal(-1, PlacePickerModel.MoveActiveIndex(0, 0, "ArrowDown"));
    }

    private static PhotoPlaceDefinitionResponse Place(
        string id,
        string name,
        string value,
        string? parentId = null,
        string? parentValue = null) =>
        new(id, name, value, parentId, parentValue);
}
