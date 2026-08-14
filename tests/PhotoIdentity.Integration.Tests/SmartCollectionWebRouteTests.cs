using Microsoft.AspNetCore.Components;
using PhotoIdentity.Web.Components;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionWebRouteTests
{
    [Fact]
    public void Smart_collection_workspace_is_exposed_as_a_routable_page()
    {
        RouteAttribute[] routes = typeof(SmartCollectionsWorkspace)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .ToArray();

        Assert.Contains(routes, route => route.Template == "/smart-collections");
    }
}
