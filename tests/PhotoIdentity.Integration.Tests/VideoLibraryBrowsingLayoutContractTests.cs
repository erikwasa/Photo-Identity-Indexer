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
        Assert.Contains("PosterFailed(video.RevisionId)", page);
        Assert.Contains("FailedPosters.Clear()", page);
        Assert.Contains("video-library-player\" controls playsinline preload=\"metadata\"", page);
        Assert.Contains("/api/videos/coverage", page);
        Assert.Contains("GetCoverageAsync", api);
        Assert.Contains("NOT @playable_only OR has_playable_proxy", repository);
        Assert.Contains("COUNT(*) FILTER (WHERE has_playable_proxy AND NOT has_poster)", repository);
        Assert.Contains("source_copy_exclusions", repository);
        Assert.Contains("video_published_derivatives", repository);
        Assert.DoesNotContain("ffmpeg", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gallery_posters_are_fitted_inside_a_fixed_stage_without_cropping_or_player_regression()
    {
        string root = RepositoryRoot();
        string css = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Web", "Pages", "Videos.razor.css"));
        string page = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Web", "Pages", "Videos.razor"));

        // The aspect-ratio stage must size the image, not the image's
        // intrinsic height. Otherwise overflow:hidden clips the bottom.
        string poster = css.Split(".video-library-poster img {", 2)[1].Split('}', 2)[0];
        Assert.Contains("position: absolute;", poster);
        Assert.Contains("inset: 0;", poster);
        Assert.Contains("width: 100%;", poster);
        Assert.Contains("height: 100%;", poster);
        Assert.Contains("object-fit: contain;", poster);
        Assert.Contains("object-position: center;", poster);
        Assert.DoesNotContain("object-fit: cover;", poster);
        Assert.Contains("aspect-ratio: 16 / 10;", css);
        Assert.Contains("overflow: hidden;", css);

        // The expanded video player has a separately responsive stage and
        // stays uncropped with the native controls in place.
        string player = css.Split(".video-library-player {", 2)[1].Split('}', 2)[0];
        Assert.Contains("height: auto;", player);
        Assert.Contains("max-height: 75dvh;", player);
        Assert.Contains("object-fit: contain;", player);
        Assert.Contains("video-library-poster--playing", css);
        Assert.Contains("video-library-player\" controls playsinline preload=\"metadata\"", page);
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
