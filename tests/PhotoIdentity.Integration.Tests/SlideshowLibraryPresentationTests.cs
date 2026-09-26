using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowLibraryPresentationTests
{
    [Fact]
    public void Cover_selection_uses_the_first_saved_query_result_deterministically()
    {
        SmartCollectionPhotoResponse first = Photo("/first-thumbnail");
        SmartCollectionPhotoResponse second = Photo("/second-thumbnail");

        string? selected = SlideshowLibraryPresentation.SelectCoverThumbnailUrl(
            Page(first, second));

        Assert.Equal("/first-thumbnail", selected);
    }

    [Fact]
    public void Cover_selection_has_a_neutral_fallback_for_empty_or_failed_queries()
    {
        Assert.Null(SlideshowLibraryPresentation.SelectCoverThumbnailUrl(Page()));
        Assert.Null(SlideshowLibraryPresentation.SelectCoverThumbnailUrl(null));
    }

    [Fact]
    public void Play_accessibility_label_names_the_collection_and_action()
    {
        Assert.Equal(
            "Play Family favourites slideshow",
            SlideshowLibraryPresentation.PlayAriaLabel("  Family favourites  "));
    }

    [Fact]
    public void Manual_photo_count_uses_exact_persisted_revision_membership()
    {
        PhotoListCollectionResponse collection = new(
            Guid.NewGuid().ToString("D"),
            "Manual",
            [Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D")],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        Assert.Equal(3, SlideshowLibraryPresentation.ManualPhotoCount(collection));
    }

    [Theory]
    [InlineData(0, "0 photos")]
    [InlineData(1, "1 photo")]
    [InlineData(53, "53 photos")]
    public void Exact_photo_count_label_is_compact_and_pluralized(int count, string expected)
    {
        Assert.Equal(expected, SlideshowLibraryPresentation.PhotoCountLabel(count));
    }

    [Fact]
    public void Creative_target_is_not_presented_as_an_exact_materialized_count()
    {
        Assert.Equal("Up to 50 photos", SlideshowLibraryPresentation.CreativeTargetLabel(50));
    }

    [Theory]
    [InlineData("ready", false, true)]
    [InlineData("READY", false, true)]
    [InlineData("ready", true, false)]
    [InlineData("preparing", false, false)]
    [InlineData(null, false, false)]
    public void Prepared_indicator_requires_verified_ready_state_without_higher_priority_activity(
        string? state,
        bool busy,
        bool expected)
    {
        Assert.Equal(expected, SlideshowLibraryPresentation.ShowPreparedIndicator(state, busy));
    }

    private static SmartCollectionPageResponse Page(params SmartCollectionPhotoResponse[] items) =>
        new(
            items,
            Offset: 0,
            Limit: 1,
            Total: items.Length,
            new SmartCollectionFilterResponse(
                People: [],
                PeopleMatch: "any",
                Tags: [],
                TagMatch: "any",
                Location: null,
                Taken: null));

    private static SmartCollectionPhotoResponse Photo(string thumbnailUrl) =>
        new(
            RevisionId: Guid.NewGuid().ToString("D"),
            AssetId: Guid.NewGuid().ToString("D"),
            ThumbnailUrl: thumbnailUrl,
            PreviewUrl: "/preview",
            OriginalUrl: "/original",
            ObservedAtUtc: DateTimeOffset.UtcNow,
            MediaType: "image/jpeg",
            Width: 1600,
            Height: 1200,
            TakenAtLocal: null,
            Latitude: null,
            Longitude: null);
}
