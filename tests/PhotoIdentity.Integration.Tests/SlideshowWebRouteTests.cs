using Microsoft.AspNetCore.Components;
using PhotoIdentity.Web.Contracts;
using PhotoIdentity.Web.Layout;
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

    [Fact]
    public void Slideshow_library_is_exposed_with_the_consumer_layout()
    {
        RouteAttribute[] routes = typeof(Slideshows)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .ToArray();
        LayoutAttribute? layout = typeof(Slideshows)
            .GetCustomAttributes(typeof(LayoutAttribute), inherit: true)
            .Cast<LayoutAttribute>()
            .SingleOrDefault();

        Assert.Contains(routes, route => route.Template == "/slideshows");
        Assert.NotNull(layout);
        Assert.Equal(typeof(ConsumerLayout), layout!.LayoutType);
    }

    [Fact]
    public void Slideshow_library_collection_contract_exposes_only_identity_and_name()
    {
        string[] properties = typeof(SlideshowLibraryCollectionResponse)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Id", "Name"], properties);
    }

    [Theory]
    [InlineData(null, "/smart-collections")]
    [InlineData("", "/smart-collections")]
    [InlineData("/smart-collections", "/smart-collections")]
    [InlineData("/smart-collections?mode=saved&collection=abc&offset=40", "/smart-collections?mode=saved&collection=abc&offset=40")]
    [InlineData("/slideshows", "/slideshows")]
    [InlineData("/slideshows?source=home", "/slideshows?source=home")]
    [InlineData("https://example.invalid/", "/smart-collections")]
    [InlineData("//example.invalid/smart-collections", "/smart-collections")]
    [InlineData("/review", "/smart-collections")]
    public void Return_navigation_is_restricted_to_supported_collection_surfaces(
        string? input,
        string expected)
    {
        Assert.Equal(expected, Slideshow.NormalizeReturnUrl(input));
    }
}
