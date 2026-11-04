using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// UI source contract only. Disposable PostgreSQL suites verify the actual
/// filtered rows and aggregate counts; maintained-browser review remains separate.
/// No original or derivative media is read in this test.
/// </summary>
public sealed class VideoLibraryBrowsingLayoutContractTests
{
    [Fact]
    public void Video_only_filter_is_server_paged_and_refresh_remains_read_only()
    {
        string root = RepositoryRoot();
        string page = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Web", "Pages", "Videos.razor"));
        string api = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Api", "VideoLibraryEndpoints.cs"));
        string repository = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Persistence.Postgres", "PostgresVideoLibraryRepository.cs"));

        Assert.Contains("Playable videos only", page);
        Assert.Contains("@bind:after=\"RefreshAsync\"", page);
        Assert.Contains("playableOnly={PlayableOnly.ToString().ToLowerInvariant()}", page);
        Assert.Contains("No playable video proxies", page);
        Assert.Contains("No verified poster", page);
        Assert.Contains("video-library-player\" controls playsinline preload=\"metadata\"", page);
        Assert.Contains("/api/videos/coverage", page);
        Assert.Contains("GetCoverageAsync", api);
        Assert.Contains("NOT @playable_only OR has_playable_proxy", repository);
        Assert.Contains("COUNT(*) FILTER (WHERE has_playable_proxy AND NOT has_poster)", repository);
        Assert.Contains("source_copy_exclusions", repository);
        Assert.Contains("video_published_derivatives", repository);
        Assert.DoesNotContain("ffmpeg", page, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        foreach (string initial in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? current = new(Path.GetFullPath(initial));
            for (int depth = 0; current is not null && depth < 12;
                 depth++, current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "PhotoIdentity.slnx")))
                    return current.FullName;
        }
        throw new DirectoryNotFoundException("Missing repository root.");
    }
}
