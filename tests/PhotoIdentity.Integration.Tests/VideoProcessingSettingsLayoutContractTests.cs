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
            "video-processing-one-step", "video-processing-auto-posters",
            "video-processing-quota-override",
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
    public void New_poster_toggle_requires_save_and_never_triggers_browse_work()
    {
        (string page, _) = Sources();
        Assert.Contains("Automatically create a poster for newly processed videos", page);
        Assert.Contains("@bind=\"AutomaticPostersForNewProxies\"", page);
        Assert.Contains("AutomaticPostersForNewProxies = Status.AutomaticPostersForNewProxies;", page);
        Assert.Contains("RetentionAgeHours, StopAfterOneVideoStepPerAdvance,", page);
        Assert.Contains("AutomaticPostersForNewProxies));", page);
        Assert.Contains("not the original video", page);
        Assert.Contains("This does not backfill older videos", page);

        string repo = ResolveRepositoryRoot();
        string worker = File.ReadAllText(Path.Combine(repo, "src",
            "PhotoIdentity.Api", "VideoArchiveProcessingService.cs"));
        string library = File.ReadAllText(Path.Combine(repo, "src",
            "PhotoIdentity.Web", "Pages", "Videos.razor"));
        Assert.Contains("ShouldCreateAutomaticPoster(cyclePreferences, result)", worker);
        Assert.Contains("TryCreateForPublishedProxyAsync(", worker);
        Assert.DoesNotContain("TryCreateForPublishedProxyAsync", library);
        Assert.DoesNotContain("VideoProxyPosterCommandRunner", library);
    }

    [Fact]
    public void Output_quality_is_independent_from_supported_inputs_and_online_consent()
    {
        (string page, _) = Sources();
        Assert.Contains("<option value=\"standard-720p\">", page);
        Assert.Contains("<option value=\"compact-540p\">", page);
        Assert.DoesNotContain("<option value=\"rec709-fullrange-720p\">", page);
        Assert.DoesNotContain("EncodingProfile == \"rec709-fullrange-720p\"", page);
        string root = ResolveRepositoryRoot();
        string worker = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Api", "VideoArchiveProcessingService.cs"));
        Assert.Contains("metadata.pixel_format IN ('yuv420p','yuv420p10le','yuvj420p')", worker);
        Assert.Contains("VideoProxyEncodingProfiles.ResolveForInput(", worker);
        Assert.Contains("WHEN metadata.pixel_format='yuvj420p' THEN @fullrange", worker);
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
