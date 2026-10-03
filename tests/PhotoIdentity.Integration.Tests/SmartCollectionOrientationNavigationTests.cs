using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionOrientationNavigationTests
{
    [Fact]
    public void Transient_navigation_round_trips_orientation_into_query()
    {
        SmartCollectionTransientNavigationState state = new(
            EditingId: null,
            Name: "Portrait preview",
            People: [],
            PeopleMatch: "all",
            Tags: [],
            TagMatch: "all",
            Taken: "",
            UseLocation: false,
            South: "",
            West: "",
            North: "",
            East: "",
            Orientation: "portrait");

        string json = SmartCollectionNavigation.SerializeTransientState(state);
        SmartCollectionTransientNavigationState restored =
            SmartCollectionNavigation.DeserializeTransientState(json)
            ?? throw new InvalidOperationException();

        Assert.Equal("portrait", restored.Orientation);
        Assert.True(SmartCollectionNavigation.TryBuildTransientQuery(
            restored,
            offset: 40,
            limit: 40,
            out SmartCollectionQueryRequest? request));
        Assert.NotNull(request);
        Assert.Equal("portrait", request.Orientation);
    }

    [Fact]
    public void Smart_collection_editor_exposes_and_carries_orientation_without_creative_duplicate()
    {
        string markup = ReadRepositoryFile(
            "src", "PhotoIdentity.Web", "Components", "SmartCollectionsWorkspace.razor");
        string codeBehind = ReadRepositoryFile(
            "src", "PhotoIdentity.Web", "Components", "SmartCollectionsWorkspace.razor.cs");
        string creativePage = ReadRepositoryFile(
            "src", "PhotoIdentity.Web", "Pages", "CreativeCollections.razor");

        Assert.Contains("<span class=\"field-label\">Photo orientation</span>", markup, StringComparison.Ordinal);
        Assert.Contains("<option value=\"landscape\">Landscape</option>", markup, StringComparison.Ordinal);
        Assert.Contains("<option value=\"portrait\">Portrait</option>", markup, StringComparison.Ordinal);
        Assert.Contains("@OrientationSummary(definition.Filter)", markup, StringComparison.Ordinal);
        Assert.Contains("Orientation: Orientation", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Orientation = NormalizeOrientation(definition.Filter.Orientation)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Orientation = NormalizeOrientation(state.Orientation)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Photo orientation", creativePage, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_proxy_generation_uses_exif_auto_oriented_pixels()
    {
        string decoder = ReadRepositoryFile(
            "src", "PhotoIdentity.Imaging.OpenCv", "OpenCvImageDecoder.cs");

        Assert.Contains("image.AutoOrient();", decoder, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), Path.Combine(segments)));

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

        throw new DirectoryNotFoundException("Could not resolve the Photo Identity repository root for Smart Collection orientation tests.");
    }
}
