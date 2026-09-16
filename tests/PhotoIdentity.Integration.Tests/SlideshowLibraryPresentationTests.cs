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
