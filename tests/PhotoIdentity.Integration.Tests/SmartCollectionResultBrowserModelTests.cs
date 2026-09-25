using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionResultBrowserModelTests
{
    [Fact]
    public void Append_adds_the_next_bounded_batch_without_duplicates()
    {
        SmartCollectionPageResponse current = Page(0, 5, Photo("r1"), Photo("r2"));
        SmartCollectionPageResponse next = Page(2, 5, Photo("r3"), Photo("r4"));

        SmartCollectionPageResponse merged = SmartCollectionResultBrowserModel.Append(current, next);

        Assert.Equal(["r1", "r2", "r3", "r4"], merged.Items.Select(photo => photo.RevisionId));
        Assert.Equal(0, merged.Offset);
        Assert.Equal(5, merged.Total);
    }

    [Fact]
    public void Append_treats_an_identical_retry_overlap_as_idempotent()
    {
        SmartCollectionPageResponse current = Page(0, 4, Photo("r1"), Photo("r2"), Photo("r3"), Photo("r4"));
        SmartCollectionPageResponse retry = Page(2, 4, Photo("r3"), Photo("r4"));

        SmartCollectionPageResponse merged = SmartCollectionResultBrowserModel.Append(current, retry);

        Assert.Equal(["r1", "r2", "r3", "r4"], merged.Items.Select(photo => photo.RevisionId));
    }

    [Fact]
    public void Append_accepts_a_stable_partial_overlap_and_adds_only_the_tail()
    {
        SmartCollectionPageResponse current = Page(0, 5, Photo("r1"), Photo("r2"), Photo("r3"));
        SmartCollectionPageResponse next = Page(2, 5, Photo("r3"), Photo("r4"), Photo("r5"));

        SmartCollectionPageResponse merged = SmartCollectionResultBrowserModel.Append(current, next);

        Assert.Equal(["r1", "r2", "r3", "r4", "r5"], merged.Items.Select(photo => photo.RevisionId));
    }

    [Fact]
    public void Append_rejects_a_gap_between_loaded_batches()
    {
        SmartCollectionPageResponse current = Page(0, 5, Photo("r1"), Photo("r2"));
        SmartCollectionPageResponse next = Page(3, 5, Photo("r4"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SmartCollectionResultBrowserModel.Append(current, next));

        Assert.Contains("skip results", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Append_rejects_changed_order_inside_a_retry_overlap()
    {
        SmartCollectionPageResponse current = Page(0, 4, Photo("r1"), Photo("r2"), Photo("r3"));
        SmartCollectionPageResponse next = Page(2, 4, Photo("different"), Photo("r4"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SmartCollectionResultBrowserModel.Append(current, next));

        Assert.Contains("ordering changed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transient_continuation_preserves_filter_dimensions_and_requested_bounds()
    {
        SmartCollectionFilterResponse filter = new(
            ["person-1"],
            "any",
            ["family"],
            "all",
            new SmartCollectionLocationRequest(
                59,
                17,
                60,
                18,
                Places: ["Sweden/Stockholm"]),
            new SmartCollectionDateRangeResponse("2025-05-01", "2025-05-31"),
            new SmartCollectionAgeRequest("person-1", 4, 7),
            new SmartCollectionRelationshipRequest("person-2", ["child"]));

        SmartCollectionQueryRequest request = SmartCollectionResultBrowserModel.BuildTransientRequest(filter, 80, 40);

        Assert.Equal(80, request.Offset);
        Assert.Equal(40, request.Limit);
        Assert.Equal(filter.People, request.People);
        Assert.Equal(filter.Tags, request.Tags);
        Assert.Equal(filter.Location, request.Location);
        Assert.Equal("2025-05-01", request.TakenRange?.From);
        Assert.Equal("2025-05-31", request.TakenRange?.To);
        Assert.Equal(filter.Age, request.Age);
        Assert.Equal(filter.Relationship, request.Relationship);
    }

    [Theory]
    [InlineData("https://photoidentity.local/smart-collections?mode=saved&collection=x&offset=40&focus=revision-42", "revision-42")]
    [InlineData("https://photoidentity.local/smart-collections?focus=revision%2F42", "revision/42")]
    [InlineData("https://photoidentity.local/smart-collections?mode=saved", null)]
    public void Focus_revision_round_trip_is_parsed_from_workspace_url(string uri, string? expected)
    {
        Assert.Equal(expected, SmartCollectionResultBrowserModel.ParseFocusRevisionId(uri));
    }

    private static SmartCollectionPageResponse Page(
        int offset,
        int total,
        params SmartCollectionPhotoResponse[] photos) => new(
        photos,
        offset,
        2,
        total,
        new SmartCollectionFilterResponse([], "all", [], "all", null, null));

    private static SmartCollectionPhotoResponse Photo(string revisionId) => new(
        revisionId,
        $"asset-{revisionId}",
        $"/thumbnail/{revisionId}",
        $"/preview/{revisionId}",
        $"/original/{revisionId}",
        DateTimeOffset.UnixEpoch,
        "image/jpeg",
        100,
        100,
        null,
        null,
        null);
}
