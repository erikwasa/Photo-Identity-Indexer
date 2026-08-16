using PhotoIdentity.Web;
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
    public void Photo_url_escapes_the_entire_nested_return_url()
    {
        string returnUrl = "/smart-collections?mode=saved&collection=collection-1&offset=40";

        string url = SmartCollectionNavigation.BuildPhotoUrl("revision-1", returnUrl);

        Assert.StartsWith("/photo/revision-1?returnUrl=", url, StringComparison.Ordinal);
        Assert.Contains("%2Fsmart-collections%3Fmode%3Dsaved%26collection%3Dcollection-1%26offset%3D40", url, StringComparison.Ordinal);
        Assert.DoesNotContain("&offset=40", url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/smart-collections?mode=saved&collection=abc&offset=40")]
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
}
