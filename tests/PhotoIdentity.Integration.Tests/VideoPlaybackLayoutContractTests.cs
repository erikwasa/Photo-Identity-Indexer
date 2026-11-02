using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Regression contract for WI-0152: a fixed 16:10 poster preview must never
/// clip the active native video or hide its bottom play/pause controls.
/// Browser geometry still requires maintained desktop and phone verification.
/// </summary>
public sealed class VideoPlaybackLayoutContractTests
{
    [Fact]
    public void Active_player_uses_natural_video_height_not_a_clipped_poster_frame()
    {
        string root = ResolveRepositoryRoot();
        string page = File.ReadAllText(Path.Combine(
            root, "src", "PhotoIdentity.Web", "Pages", "Videos.razor"));
        string css = File.ReadAllText(Path.Combine(
            root, "src", "PhotoIdentity.Web", "Pages", "Videos.razor.css"));

        Assert.Contains("video-library-card--playing", page, StringComparison.Ordinal);
        Assert.Contains("video-library-poster--playing", page, StringComparison.Ordinal);
        Assert.Contains("<video class=\"video-library-player\" controls playsinline", page,
            StringComparison.Ordinal);

        string poster = Rule(css, ".video-library-poster");
        Assert.Contains("aspect-ratio: 16 / 10;", poster, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", poster, StringComparison.Ordinal);

        string playing = Rule(css, ".video-library-poster--playing");
        Assert.Contains("aspect-ratio: auto;", playing, StringComparison.Ordinal);
        Assert.Contains("overflow: visible;", playing, StringComparison.Ordinal);
        Assert.Contains("display: block;", playing, StringComparison.Ordinal);

        string player = Rule(css, ".video-library-player");
        Assert.Contains("width: 100%;", player, StringComparison.Ordinal);
        Assert.Contains("height: auto;", player, StringComparison.Ordinal);
        Assert.Contains("object-fit: contain;", player, StringComparison.Ordinal);
        Assert.Contains("max-height: 75dvh;", player, StringComparison.Ordinal);
        Assert.DoesNotContain("height: 100%;", player, StringComparison.Ordinal);
        Assert.DoesNotContain("overflow: hidden;", player, StringComparison.Ordinal);

        string expanded = Rule(css, ".video-library-card--playing");
        Assert.Contains("grid-column: 1 / -1;", expanded, StringComparison.Ordinal);
    }

    private static string Rule(string css, string selector)
    {
        int start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing CSS rule {selector}.");
        int open = css.IndexOf('{', start);
        int close = css.IndexOf('}', open);
        Assert.True(close > open, $"Unclosed CSS rule {selector}.");
        return css[(open + 1)..close];
    }

    private static string ResolveRepositoryRoot()
    {
        foreach (string candidate in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(candidate));
            for (int depth = 0; directory is not null && depth < 12;
                 depth++, directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PhotoIdentity.slnx")))
                    return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException(
            "Could not locate Photo Identity repository root for video player layout tests.");
    }
}
