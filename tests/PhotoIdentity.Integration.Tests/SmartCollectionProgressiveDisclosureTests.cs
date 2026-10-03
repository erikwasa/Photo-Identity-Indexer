using System.Text.Json;
using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionProgressiveDisclosureTests
{
    [Fact]
    public void Optional_filter_details_are_rendered_only_after_their_toggle_is_enabled()
    {
        string markup = ReadRepositoryFile(
            "src",
            "PhotoIdentity.Web",
            "Components",
            "SmartCollectionsWorkspace.razor");

        Assert.Contains(
            "@if (UseLocation)\n                        {\n                            <div class=\"smart-coordinate-grid\">",
            markup,
            StringComparison.Ordinal);
        Assert.Contains(
            "@if (UseAgeFilter)\n                        {\n                            <label class=\"smart-field\">\n                                <span class=\"field-label\">Person</span>",
            markup,
            StringComparison.Ordinal);
        Assert.Contains(
            "@if (UseRelationshipFilter)\n                        {\n                            <label class=\"smart-field\">\n                                <span class=\"field-label\">Related to</span>",
            markup,
            StringComparison.Ordinal);

        Assert.DoesNotContain("disabled=\"@(!UseLocation || Busy)\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled=\"@(!UseAgeFilter || Busy)\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled=\"@(!UseRelationshipFilter || Busy)\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Generic_tag_authoring_is_hidden_while_existing_tag_criteria_remain_visible_and_preserved()
    {
        string markup = ReadRepositoryFile(
            "src",
            "PhotoIdentity.Web",
            "Components",
            "SmartCollectionsWorkspace.razor");
        string codeBehind = ReadRepositoryFile(
            "src",
            "PhotoIdentity.Web",
            "Components",
            "SmartCollectionsWorkspace.razor.cs");

        Assert.DoesNotContain("<legend>Tags</legend>", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Tag filter\"", markup, StringComparison.Ordinal);
        Assert.Contains("@if (SelectedTags.Count > 0)", markup, StringComparison.Ordinal);
        Assert.Contains("saving or previewing preserves the existing tag criteria", markup, StringComparison.Ordinal);
        Assert.Contains("@if (definition.Filter.Tags.Length > 0)", markup, StringComparison.Ordinal);

        Assert.True(
            CountOccurrences(codeBehind, "SelectedTags.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()") >= 3,
            "Saved definitions, transient queries and browser navigation state must all continue carrying hidden tag criteria.");
        Assert.Contains("TagMatch,", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Transient_navigation_contract_round_trips_hidden_tags_and_match_mode()
    {
        SmartCollectionTransientNavigationState state = new(
            EditingId: "collection-1",
            Name: "Legacy tagged collection",
            People: [],
            PeopleMatch: "all",
            Tags: ["family", "travel"],
            TagMatch: "any",
            Taken: "",
            UseLocation: false,
            South: "",
            West: "",
            North: "",
            East: "");

        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        string json = JsonSerializer.Serialize(state, options);
        SmartCollectionTransientNavigationState? restored =
            JsonSerializer.Deserialize<SmartCollectionTransientNavigationState>(json, options);

        Assert.NotNull(restored);
        Assert.Equal(new[] { "family", "travel" }, restored.Tags);
        Assert.Equal("any", restored.TagMatch);
    }

    private static int CountOccurrences(string text, string value)
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

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), Path.Combine(segments)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

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

        throw new DirectoryNotFoundException("Could not resolve the Photo Identity repository root for Smart Collection UI tests.");
    }
}
