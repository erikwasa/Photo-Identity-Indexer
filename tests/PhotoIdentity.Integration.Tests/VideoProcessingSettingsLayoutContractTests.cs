using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Source-level contract for settings usability regressions: UI behaviour
/// still requires one maintained-browser review after merge. Do not touch
/// real media, video jobs, cache retention or PostgreSQL in this test.
/// </summary>
public sealed class VideoProcessingSettingsLayoutContractTests
{
    [Fact]
    public void Checkbox_rules_override_global_text_input_dimensions()
    {
        (string page, string css) = Sources();

        foreach (string id in new[]
        {
            "video-processing-enabled", "video-processing-online",
            "video-processing-one-step", "video-processing-quota-override",
            "video-retention-confirm"
        })
        {
            Assert.Contains(
                $"class=\"field-label video-settings-check-row\" for=\"{id}\"",
                page, StringComparison.Ordinal);
        }

        Assert.Contains(".video-settings-check-row input[type=\"checkbox\"]", css);
        Assert.Contains("width: 1.05rem;", css);
        Assert.Contains("min-height: 1.05rem;", css);
        Assert.Contains("padding: 0;", css);
        Assert.Contains("margin: 0;", css);
        Assert.Contains("align-items: center;", css);
    }

    [Fact]
    public void Shared_input_rules_do_not_style_checkboxes_like_textfields()
    {
        string repo = ResolveRepositoryRoot();
        string globals = File.ReadAllText(Path.Combine(
            repo, "src", "PhotoIdentity.Web", "wwwroot", "css", "app.css"));
        string settings = File.ReadAllText(Path.Combine(
            repo, "src", "PhotoIdentity.Web", "Pages", "Settings.razor.css"));

        Assert.Contains(
            "input:not([type=\"checkbox\"]):not([type=\"radio\"]),",
            globals, StringComparison.Ordinal);
        Assert.Contains("input[type=\"checkbox\"],", globals,
            StringComparison.Ordinal);
        Assert.Contains("input[type=\"radio\"] {", globals,
            StringComparison.Ordinal);
        Assert.Contains("width: 1.1rem;", globals, StringComparison.Ordinal);
        Assert.Contains("min-height: 1.1rem;", globals, StringComparison.Ordinal);
        Assert.Contains(
            "input:not([type=\"checkbox\"]):not([type=\"radio\"]),",
            settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_save_and_status_refresh_remain_in_view_without_form_collapse()
    {
        (string page, string css) = Sources();

        Assert.Contains("video-settings-action-strip", page);
        Assert.Contains("position: sticky;", css);
        Assert.Contains("Save video settings", page);
        Assert.Contains("video-settings-feedback--success", page);
        Assert.Contains("video-settings-feedback--error", page);
        Assert.Contains("role=\"status\">@Message", page);

        int queue = page.IndexOf("video-queue-heading", StringComparison.Ordinal);
        int form = page.IndexOf("video-settings-form", StringComparison.Ordinal);
        Assert.True(queue > 0 && form > queue,
            "Queue status and its Refresh action must appear before the form.");

        Assert.Contains("Loading && Status is null", page);
        Assert.Contains("RefreshStatusAsync() => LoadAsync(synchronizeForm: false)", page);
        Assert.Contains("if (synchronizeForm)", page);
        Assert.Contains("Status = refreshed;", page);
        Assert.DoesNotContain("@onclick=\"LoadAsync\"", page);
    }

    [Fact]
    public void Destructive_cleanup_is_advanced_only_and_still_requires_confirmation()
    {
        (string page, _) = Sources();
        Assert.Contains("<details class=\"video-settings-maintenance\">", page);
        Assert.Contains("Previewing never deletes files", page);
        Assert.Contains("Preview proxy cleanup", page);
        Assert.Contains("Confirm removal of @plan.Planned generated copies", page);
        Assert.Contains("ConfirmRetention", page);
        Assert.Contains("Status.Processing", page);
        Assert.Contains("api/video-processing/retention/preview", page);
        Assert.Contains("api/video-processing/retention/apply", page);
    }

    private static (string Page, string Css) Sources()
    {
        string repo = ResolveRepositoryRoot();
        string root = Path.Combine(repo, "src", "PhotoIdentity.Web",
            "Components", "VideoProcessingSettings");
        return (File.ReadAllText(root + ".razor"),
            File.ReadAllText(root + ".razor.css"));
    }

    private static string ResolveRepositoryRoot()
    {
        foreach (string location in new[]
            { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? dir = new(Path.GetFullPath(location));
            for (int depth = 0; dir is not null && depth < 12;
                 depth++, dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "PhotoIdentity.slnx")))
                    return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("Photo Identity repository root missing.");
    }
}
