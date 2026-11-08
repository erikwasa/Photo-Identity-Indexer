using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Source-level guard for the opt-in UI/API boundary. No source media,
/// recognition model, OneDrive, provider or catalogue is opened here.
/// </summary>
public sealed class VideoDetailsLayoutContractTests
{
    [Fact]
    public void Video_details_remain_explicit_and_distinct_from_photo_or_recognition()
    {
        string root = Root();
        string listing = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Pages", "Videos.razor"));
        string details = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Web", "Pages", "VideoDetails.razor"));
        string api = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Api", "VideoDetailsEndpoints.cs"));
        string repository = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Persistence.Postgres", "PostgresVideoDetailsRepository.cs"));
        string schema = File.ReadAllText(Path.Combine(root, "src", "PhotoIdentity.Persistence.Postgres", "PostgresCatalogueDatabase.VideoDetailsSchema.cs"));

        Assert.Contains("Video details &amp; people", listing);
        Assert.Contains("@page \"/videos/{RevisionId}\"", details);
        Assert.Contains("Manual presence means", details);
        Assert.Contains("Look up Place from GPS", details);
        Assert.Contains("Review", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/videos/{revisionId}/resolve-place", api);
        Assert.Contains("GetCachedAsync", api);
        Assert.Contains("ReverseGeocodeAsync", api);
        Assert.Contains("ManualPlaceOverride", api);
        Assert.Contains("CurrentVideoPredicate", repository);
        Assert.Contains("FOR UPDATE OF source", repository);
        Assert.Contains("source_copy_exclusions", repository);
        Assert.Contains("video_person_actions", repository);
        Assert.Contains("video_capture_date_actions", schema);
        Assert.Contains("video_place_actions", schema);
        Assert.Contains("trg_source_exclusion_purge_video_details", schema);
        Assert.DoesNotContain("face_occurrences", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identity_suggestions", repository, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffmpeg", details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OneDrive", repository, StringComparison.OrdinalIgnoreCase);
    }

    private static string Root()
    {
        foreach (string initial in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? current = new(Path.GetFullPath(initial));
            for (int depth = 0; current is not null && depth < 12; depth++, current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "PhotoIdentity.slnx")))
                    return current.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
