using Microsoft.AspNetCore.Components;
using PhotoIdentity.Web.Pages;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowWebRouteTests
{
    [Fact]
    public void Slideshow_is_exposed_as_a_saved_collection_route()
    {
        RouteAttribute[] routes = typeof(Slideshow)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .ToArray();

        Assert.Contains(routes, route => route.Template == "/slideshow/{CollectionId:guid}");
    }

    [Theory]
    [InlineData(null, "/smart-collections")]
    [InlineData("", "/smart-collections")]
    [InlineData("/smart-collections", "/smart-collections")]
    [InlineData("/smart-collections?mode=saved&collection=abc&offset=40", "/smart-collections?mode=saved&collection=abc&offset=40")]
    [InlineData("https://example.invalid/", "/smart-collections")]
    [InlineData("//example.invalid/smart-collections", "/smart-collections")]
    [InlineData("/review", "/smart-collections")]
    public void Return_navigation_is_restricted_to_the_Smart_Collections_workspace(
        string? input,
        string expected)
    {
        Assert.Equal(expected, Slideshow.NormalizeReturnUrl(input));
    }
}
