using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class NavigationContextTests
{
    [Fact]
    public void Saved_workspace_url_preserves_collection_and_page_offset()
    {
        string url = SmartCollectionNavigation.BuildSavedWorkspaceUrl("collection-1", 80);

        Assert.Equal("/smart-collections?mode=saved&collection=collection-1&offset=80", url);
    }

    [Fact]
    public void Transient_workspace_url_preserves_only_preview_key_and_page_offset()
    {
        string url = SmartCollectionNavigation.BuildTransientWorkspaceUrl("0123456789abcdef0123456789abcdef", 40);

        Assert.Equal(
            "/smart-collections?mode=transient&preview=0123456789abcdef0123456789abcdef&offset=40",
            url);
        Assert.Equal(
            "photo-identity.smart-collections.preview.0123456789abcdef0123456789abcdef",
            SmartCollectionNavigation.PreviewStorageKey("0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public void Transient_state_round_trip_preserves_editor_filters()
    {
        SmartCollectionTransientNavigationState state = BuildTransientState();

        string json = SmartCollectionNavigation.SerializeTransientState(state);
        SmartCollectionTransientNavigationState? restored = SmartCollectionNavigation.DeserializeTransientState(json);

        Assert.NotNull(restored);
        Assert.Equal(state.EditingId, restored.EditingId);
        Assert.Equal(state.Name, restored.Name);
        Assert.Equal(state.People, restored.People);
        Assert.Equal(state.PeopleMatch, restored.PeopleMatch);
        Assert.Equal(state.Tags, restored.Tags);
        Assert.Equal(state.TagMatch, restored.TagMatch);
        Assert.Equal(state.Taken, restored.Taken);
        Assert.Equal(state.UseLocation, restored.UseLocation);
        Assert.Equal(state.South, restored.South);
        Assert.Equal(state.West, restored.West);
        Assert.Equal(state.North, restored.North);
        Assert.Equal(state.East, restored.East);
        Assert.Equal(state.Place, restored.Place);
        Assert.Equal(SmartCollectionDateModes.Range, restored.TakenMode);
        Assert.Equal("2025-05-01", restored.TakenFrom);
        Assert.Equal("2025-05-10", restored.TakenTo);
        Assert.Equal(state.Places, restored.Places);
        Assert.Equal(state.Age, restored.Age);
        Assert.NotNull(restored.Relationship);
        Assert.Equal(state.Relationship!.PersonId, restored.Relationship.PersonId);
        Assert.Equal(state.Relationship.Kinds, restored.Relationship.Kinds);
    }

    [Fact]
    public void Transient_state_rebuilds_the_same_bounded_navigation_filter()
    {
        SmartCollectionTransientNavigationState state = BuildTransientState();

        Assert.True(SmartCollectionNavigation.TryBuildTransientQuery(
            state,
            39,
            3,
            out SmartCollectionQueryRequest? request));

        Assert.NotNull(request);
        Assert.Equal(39, request.Offset);
        Assert.Equal(3, request.Limit);
        Assert.Equal(state.People, request.People);
        Assert.Equal(state.PeopleMatch, request.PeopleMatch);
        Assert.Equal(state.Tags, request.Tags);
        Assert.Equal(state.TagMatch, request.TagMatch);
        Assert.NotNull(request.Location);
        Assert.Equal(59, request.Location.South);
        Assert.Equal(17, request.Location.West);
        Assert.Equal(60, request.Location.North);
        Assert.Equal(18, request.Location.East);
        Assert.Equal(state.Places, request.Location.Places);
        Assert.Equal("2025-05-01", request.TakenRange?.From);
        Assert.Equal("2025-05-10", request.TakenRange?.To);
        Assert.Equal(state.Age, request.Age);
        Assert.NotNull(request.Relationship);
        Assert.Equal(state.Relationship!.PersonId, request.Relationship.PersonId);
        Assert.Equal(state.Relationship.Kinds, request.Relationship.Kinds);
    }

    [Fact]
    public void Legacy_transient_state_without_named_place_remains_readable()
    {
        const string json = """
            {"editingId":null,"name":"Legacy preview","people":[],"peopleMatch":"all","tags":[],"tagMatch":"all","taken":"","useLocation":true,"south":"59","west":"17","north":"60","east":"19"}
            """;

        SmartCollectionTransientNavigationState? restored = SmartCollectionNavigation.DeserializeTransientState(json);

        Assert.NotNull(restored);
        Assert.Null(restored.Place);
        Assert.Null(restored.Age);
        Assert.Null(restored.Relationship);
        Assert.True(restored.UseLocation);
        Assert.Equal("59", restored.South);
    }

    [Fact]
    public void Photo_url_escapes_the_entire_nested_return_url()
    {
        string returnUrl = "/smart-collections?mode=saved&collection=collection-1&offset=40";

        string url = SmartCollectionNavigation.BuildPhotoUrl("revision-1", returnUrl);

        Assert.StartsWith("/photo/revision-1?returnUrl=", url);
        Assert.Contains("%2Fsmart-collections%3Fmode%3Dsaved%26collection%3Dcollection-1%26offset%3D40", url);
        Assert.DoesNotContain("&offset=40", url);
    }

    [Fact]
    public void Photo_url_round_trip_recovers_saved_workspace_context()
    {
        string collectionId = Guid.NewGuid().ToString();
        string returnUrl = SmartCollectionNavigation.BuildSavedWorkspaceUrl(collectionId, 40);
        string url = SmartCollectionNavigation.BuildPhotoUrl(Guid.NewGuid().ToString(), returnUrl);

        Assert.True(SmartCollectionNavigation.TryParsePhotoUrl(
            url,
            out string? revisionId,
            out string? parsedReturnUrl));
        Assert.NotNull(revisionId);
        Assert.Equal(returnUrl, parsedReturnUrl);
        Assert.True(SmartCollectionNavigation.TryParseWorkspaceContext(
            parsedReturnUrl,
            out SmartCollectionWorkspaceContext? context));
        Assert.NotNull(context);
        Assert.Equal("saved", context.Mode);
        Assert.Equal(collectionId, context.CollectionId);
        Assert.Equal(40, context.Offset);
    }

    [Fact]
    public void Transient_workspace_context_round_trip_preserves_preview_and_offset()
    {
        const string previewKey = "0123456789abcdef0123456789abcdef";
        string returnUrl = SmartCollectionNavigation.BuildTransientWorkspaceUrl(previewKey, 80);

        Assert.True(SmartCollectionNavigation.TryParseWorkspaceContext(
            returnUrl,
            out SmartCollectionWorkspaceContext? context));
        Assert.NotNull(context);
        Assert.Equal("transient", context.Mode);
        Assert.Equal(previewKey, context.PreviewKey);
        Assert.Equal(80, context.Offset);
        Assert.Equal(returnUrl, SmartCollectionNavigation.BuildWorkspaceUrl(context, context.Offset));
    }

    [Fact]
    public void Direct_photo_details_without_smart_collection_return_has_no_smart_context()
    {
        string url = SmartCollectionNavigation.BuildPhotoUrl("revision-1", "/collections");

        Assert.True(SmartCollectionNavigation.TryParsePhotoUrl(
            url,
            out _,
            out string? returnUrl));
        Assert.False(SmartCollectionNavigation.TryParseWorkspaceContext(returnUrl, out _));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(39, 0)]
    [InlineData(40, 40)]
    [InlineData(79, 40)]
    [InlineData(80, 80)]
    public void Result_index_maps_back_to_the_containing_workspace_page(int index, int expectedOffset)
    {
        Assert.Equal(expectedOffset, SmartCollectionNavigation.PageOffsetForIndex(index));
    }

    [Fact]
    public void Photo_navigation_resolves_exact_order_and_first_last_boundaries()
    {
        SmartCollectionPageResponse page = Page(
            offset: 0,
            total: 3,
            Photo("revision-1"),
            Photo("revision-2"),
            Photo("revision-3"));

        Assert.True(SmartCollectionNavigation.TryResolvePhotoNavigation(
            page,
            "revision-1",
            0,
            out SmartCollectionPhotoNavigationResolution? first));
        Assert.NotNull(first);
        Assert.Null(first.PreviousRevisionId);
        Assert.Equal("revision-2", first.NextRevisionId);

        Assert.True(SmartCollectionNavigation.TryResolvePhotoNavigation(
            page,
            "revision-2",
            1,
            out SmartCollectionPhotoNavigationResolution? middle));
        Assert.NotNull(middle);
        Assert.Equal("revision-1", middle.PreviousRevisionId);
        Assert.Equal("revision-3", middle.NextRevisionId);

        Assert.True(SmartCollectionNavigation.TryResolvePhotoNavigation(
            page,
            "revision-3",
            2,
            out SmartCollectionPhotoNavigationResolution? last));
        Assert.NotNull(last);
        Assert.Equal("revision-2", last.PreviousRevisionId);
        Assert.Null(last.NextRevisionId);
    }

    [Fact]
    public void Photo_navigation_resolves_neighbors_across_workspace_page_boundary()
    {
        SmartCollectionPageResponse neighbors = Page(
            offset: 39,
            total: 82,
            Photo("revision-39"),
            Photo("revision-40"),
            Photo("revision-41"));

        Assert.True(SmartCollectionNavigation.TryResolvePhotoNavigation(
            neighbors,
            "revision-40",
            40,
            out SmartCollectionPhotoNavigationResolution? navigation));
        Assert.NotNull(navigation);
        Assert.Equal("revision-39", navigation.PreviousRevisionId);
        Assert.Equal("revision-41", navigation.NextRevisionId);
        Assert.Equal(40, SmartCollectionNavigation.PageOffsetForIndex(navigation.Index));
    }

    [Fact]
    public void Photo_navigation_rejects_stale_or_deleted_result_context_instead_of_jumping()
    {
        SmartCollectionPageResponse page = Page(
            offset: 39,
            total: 81,
            Photo("revision-39"),
            Photo("different-revision"),
            Photo("revision-41"));

        Assert.False(SmartCollectionNavigation.TryResolvePhotoNavigation(
            page,
            "revision-40",
            40,
            out SmartCollectionPhotoNavigationResolution? navigation));
        Assert.Null(navigation);
    }

    [Fact]
    public void Archive_workspace_url_preserves_filters_and_page_offset()
    {
        string url = ArchiveNavigation.BuildWorkspaceUrl(
            "1970/01",
            "online-only",
            "needs-source-verification",
            "pending",
            100);

        Assert.Equal(
            "/archive?folder=1970%2F01&availability=online-only&verification=needs-source-verification&analysis=pending&offset=100",
            url);
    }

    [Fact]
    public void Archive_photo_url_escapes_the_entire_nested_return_url()
    {
        string returnUrl = ArchiveNavigation.BuildWorkspaceUrl("1970/01", "local", "verified", "analysed", 50);

        string url = ArchiveNavigation.BuildPhotoUrl("revision-1", returnUrl);

        Assert.StartsWith("/photo/revision-1?returnUrl=", url);
        Assert.Contains("%2Farchive%3Ffolder%3D1970%252F01%26availability%3Dlocal", url);
        Assert.DoesNotContain("&offset=50", url);
    }

    [Theory]
    [InlineData("/smart-collections?mode=saved&collection=abc&offset=40")]
    [InlineData("/archive?folder=1970%2F01&offset=50")]
    [InlineData("/collections")]
    [InlineData("/photo/abc?view=details")]
    public void Local_return_context_accepts_rooted_application_routes(string candidate)
    {
        Assert.Equal(candidate, PhotoReturnContext.NormalizeLocalReturnUrl(candidate));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("smart-collections")]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    [InlineData("/\\example.com/")]
    [InlineData("/smart-collections\nhttps://example.com/")]
    public void Local_return_context_rejects_external_or_ambiguous_routes(string? candidate)
    {
        Assert.Null(PhotoReturnContext.NormalizeLocalReturnUrl(candidate));
    }

    [Theory]
    [InlineData("/smart-collections", true)]
    [InlineData("/smart-collections?mode=saved", true)]
    [InlineData("/smart-collections#results", true)]
    [InlineData("/smart-collections-evil", false)]
    [InlineData("/collections", false)]
    public void Smart_collection_return_label_requires_exact_route_boundary(string candidate, bool expected)
    {
        Assert.Equal(expected, PhotoReturnContext.IsSmartCollectionsReturn(candidate));
    }

    [Theory]
    [InlineData("/archive", true)]
    [InlineData("/archive?folder=1970%2F01", true)]
    [InlineData("/archive#items", true)]
    [InlineData("/archive-evil", false)]
    [InlineData("/collections", false)]
    public void Archive_return_label_requires_exact_route_boundary(string candidate, bool expected)
    {
        Assert.Equal(expected, PhotoReturnContext.IsArchiveReturn(candidate));
    }

    private static SmartCollectionTransientNavigationState BuildTransientState() => new(
        "saved-1",
        "Current preview",
        ["person-2", "person-1"],
        "any",
        ["Family", "Travel"],
        "all",
        "2025/05/01-2025/05/10",
        true,
        "59.0",
        "17.0",
        "60.0",
        "18.0",
        "Sweden/Stockholm region/Norrtälje",
        TakenMode: SmartCollectionDateModes.Range,
        TakenFrom: "2025-05-01",
        TakenTo: "2025-05-10",
        Places: ["Sweden/Stockholm region", "Sweden/Uppsala län"],
        Age: new SmartCollectionAgeRequest("person-1", 4, 7),
        Relationship: new SmartCollectionRelationshipRequest("person-2", ["child", "sibling"]));

    private static SmartCollectionPageResponse Page(
        int offset,
        int total,
        params SmartCollectionPhotoResponse[] items) => new(
        items,
        offset,
        Math.Max(1, items.Length),
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
