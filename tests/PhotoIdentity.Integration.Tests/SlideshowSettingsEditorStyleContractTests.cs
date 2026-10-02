using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowSettingsEditorStyleContractTests
{
    [Fact]
    public void Shared_editor_is_used_by_library_and_in_player_settings()
    {
        string root = ResolveRepositoryRoot();
        string library = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Pages", "Slideshows.razor"));
        string player = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Pages", "Slideshow.razor"));

        Assert.Contains("<SlideshowSettingsEditor", library, StringComparison.Ordinal);
        Assert.Contains("<SlideshowSettingsEditor", player, StringComparison.Ordinal);
    }

    [Fact]
    public void Boolean_preferences_use_compact_tappable_checkbox_rows()
    {
        string root = ResolveRepositoryRoot();
        string markup = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Components", "SlideshowSettingsEditor.razor"));
        string css = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Components", "SlideshowSettingsEditor.razor.css"));

        Assert.Equal(6, Count(markup, "class=\"slideshow-settings-check\""));
        Assert.Contains(".slideshow-settings-check {", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1.25rem minmax(0, 1fr);", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", css, StringComparison.Ordinal);
        Assert.Contains(".slideshow-settings-check > input[type=\"checkbox\"]", css, StringComparison.Ordinal);
        Assert.Contains("width: 1.2rem;", css, StringComparison.Ordinal);
        Assert.Contains("width: 1.35rem;", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 48px;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_duration_keeps_the_compact_value_and_unit_together()
    {
        string root = ResolveRepositoryRoot();
        string markup = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Components", "SlideshowSettingsEditor.razor"));
        string css = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Components", "SlideshowSettingsEditor.razor.css"));

        Assert.Contains("class=\"slideshow-settings-duration-control\"", markup, StringComparison.Ordinal);
        Assert.Contains("class=\"slideshow-settings-duration\"", markup, StringComparison.Ordinal);
        Assert.Contains("inputmode=\"numeric\"", markup, StringComparison.Ordinal);
        Assert.Contains("<span>seconds</span>", markup, StringComparison.Ordinal);
        Assert.Contains("white-space: nowrap;", css, StringComparison.Ordinal);
        Assert.Contains("width: 4.25rem;", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("flex-wrap: wrap;", css, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
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

        throw new DirectoryNotFoundException("Could not resolve the Photo Identity repository root for slideshow settings tests.");
    }
}
