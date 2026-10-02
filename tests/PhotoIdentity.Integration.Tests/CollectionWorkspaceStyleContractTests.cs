using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CollectionWorkspaceStyleContractTests
{
    [Fact]
    public void Shared_collection_workspace_styles_are_loaded_globally_before_isolated_component_styles()
    {
        string root = ResolveRepositoryRoot();
        string index = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "wwwroot", "index.html"));

        int shared = index.IndexOf("css/collection-workspace.css", StringComparison.Ordinal);
        int isolated = index.IndexOf("PhotoIdentity.Web.styles.css", StringComparison.Ordinal);

        Assert.True(shared >= 0, "The shared collection workspace stylesheet must be loaded by the Web shell.");
        Assert.True(isolated > shared, "Shared collection workspace styles must load before component-isolated overrides.");
    }

    [Fact]
    public void Creative_novelty_toggle_uses_the_shared_accessible_touch_target_contract()
    {
        string root = ResolveRepositoryRoot();
        string page = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Pages", "CreativeCollections.razor"));
        string sharedCss = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "wwwroot", "css", "collection-workspace.css"));

        Assert.Contains("<label class=\"smart-location-toggle\">", page, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", page, StringComparison.Ordinal);
        Assert.Contains("Favor photos not shown recently", page, StringComparison.Ordinal);

        Assert.Contains(".smart-location-toggle {", sharedCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: auto minmax(0, 1fr);", sharedCss, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", sharedCss, StringComparison.Ordinal);
        Assert.Contains(".smart-location-toggle > input[type=\"checkbox\"]", sharedCss, StringComparison.Ordinal);
        Assert.Contains("width: 1.25rem;", sharedCss, StringComparison.Ordinal);
        Assert.Contains(".creative-controls > .smart-location-toggle", sharedCss, StringComparison.Ordinal);
        Assert.Contains("grid-column: 1 / -1;", sharedCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_collection_rules_are_not_duplicated_in_the_smart_collection_isolated_stylesheet()
    {
        string root = ResolveRepositoryRoot();
        string isolatedCss = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PhotoIdentity.Web",
            "Components",
            "SmartCollectionsWorkspace.razor.css"));

        Assert.DoesNotContain(".smart-workspace {", isolatedCss, StringComparison.Ordinal);
        Assert.DoesNotContain(".smart-location-toggle", isolatedCss, StringComparison.Ordinal);
        Assert.DoesNotContain(".creative-controls", isolatedCss, StringComparison.Ordinal);
        Assert.Contains(".smart-filter-card", isolatedCss, StringComparison.Ordinal);
    }

    private static string ResolveRepositoryRoot()
    {
        foreach (string candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(candidate));
            for (int depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PhotoIdentity.slnx")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Could not resolve the Photo Identity repository root for collection workspace tests.");
    }
}
