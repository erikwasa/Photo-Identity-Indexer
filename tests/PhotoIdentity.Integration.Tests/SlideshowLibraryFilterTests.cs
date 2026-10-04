using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowLibraryFilterTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  Summer trip  ", "Summer trip")]
    public void NormalizeQuery_trims_surrounding_whitespace(string? query, string expected)
    {
        Assert.Equal(expected, SlideshowLibraryFilter.NormalizeQuery(query));
    }

    [Fact]
    public void ByName_matches_partial_names_case_insensitively_after_trimming()
    {
        IReadOnlyList<SlideshowLibraryItem> items =
        [
            Item("1", "Family Summer"),
            Item("2", "Winter walk"),
            Item("3", "SUMMER evenings"),
        ];

        IReadOnlyList<SlideshowLibraryItem> matches =
            SlideshowLibraryFilter.ByName(items, "  summer  ");

        Assert.Equal(["1", "3"], matches.Select(item => item.Id));
    }

    [Fact]
    public void ByName_with_empty_query_returns_the_loaded_catalogue_without_reordering()
    {
        IReadOnlyList<SlideshowLibraryItem> items =
        [
            Item("3", "Charlie"),
            Item("1", "Alpha"),
            Item("2", "Bravo"),
        ];

        IReadOnlyList<SlideshowLibraryItem> matches =
            SlideshowLibraryFilter.ByName(items, "   ");

        Assert.Same(items, matches);
        Assert.Equal(["3", "1", "2"], matches.Select(item => item.Id));
    }

    [Fact]
    public void ByName_preserves_unified_catalogue_order_for_matching_items()
    {
        IReadOnlyList<SlideshowLibraryItem> items =
        [
            Item("1", "Alpha family", SlideshowLibraryItemKind.Smart),
            Item("2", "Family archive", SlideshowLibraryItemKind.Manual),
            Item("3", "Family sampler", SlideshowLibraryItemKind.Creative),
        ];

        IReadOnlyList<SlideshowLibraryItem> matches =
            SlideshowLibraryFilter.ByName(items, "family");

        Assert.Equal(["1", "2", "3"], matches.Select(item => item.Id));
        Assert.Equal(
            [SlideshowLibraryItemKind.Smart, SlideshowLibraryItemKind.Manual, SlideshowLibraryItemKind.Creative],
            matches.Select(item => item.Kind));
    }

    [Fact]
    public void ByName_does_not_match_creation_kind_or_source_collection_metadata()
    {
        IReadOnlyList<SlideshowLibraryItem> items =
        [
            Item("1", "Weekend", SlideshowLibraryItemKind.Creative, sourceCollectionName: "Summer"),
            Item("2", "Manual favourites", SlideshowLibraryItemKind.Manual),
        ];

        Assert.Empty(SlideshowLibraryFilter.ByName(items, "Summer"));
        Assert.Empty(SlideshowLibraryFilter.ByName(items, "Creative"));
    }

    private static SlideshowLibraryItem Item(
        string id,
        string name,
        SlideshowLibraryItemKind kind = SlideshowLibraryItemKind.Smart,
        string? sourceCollectionName = null) =>
        new(
            id,
            name,
            kind,
            CoverCollectionId: id,
            CoverRevisionId: null,
            RevisionIds: [],
            PhotoCount: null,
            CreativeTargetCount: null,
            SourceCollectionName: sourceCollectionName);
}
