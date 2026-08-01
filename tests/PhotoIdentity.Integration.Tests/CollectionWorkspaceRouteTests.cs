using System.Reflection;
using Microsoft.AspNetCore.Components;
using PhotoIdentity.Web.Pages;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CollectionWorkspaceRouteTests
{
    [Fact]
    public void Collection_workspace_exposes_the_expected_route()
    {
        RouteAttribute route = Assert.Single(
            typeof(Collections).GetCustomAttributes<RouteAttribute>());

        Assert.Equal("/collections", route.Template);
    }
}
