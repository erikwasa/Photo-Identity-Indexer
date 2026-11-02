using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

public sealed class VideoSearchPresentationTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(6171, "0:06")]
    [InlineData(65000, "1:05")]
    [InlineData(3661000, "1:01:01")]
    [InlineData(-1, "Time unknown")]
    public void Frame_time_displays_stored_timestamp(long milliseconds, string expected)
    {
        Assert.Equal(expected, VideoSearchPresentation.FrameTime(milliseconds));
    }

    [Fact]
    public void Poster_path_must_match_the_revision_bound_local_route()
    {
        Guid revision = Guid.NewGuid();
        string route = $"/api/videos/{revision:D}/poster";

        Assert.Equal(route, VideoSearchPresentation.PosterPath(Result(revision.ToString("D"), route)));
        Assert.Null(VideoSearchPresentation.PosterPath(Result(revision.ToString("D"), "https://example.invalid/photo")));
        Assert.Null(VideoSearchPresentation.PosterPath(Result(revision.ToString("D"), "/api/videos/other/poster")));
        Assert.Null(VideoSearchPresentation.PosterPath(Result("invalid", route)));
    }

    private static VideoSearchResultResponse Result(string id, string posterUrl) => new(
        id, posterUrl, 1000, "Sample caption", 0.1, 0.1, 0.1,
        ["semantic", "caption"], "generated-frame-evidence",
        "sampling-v1", "clip-model", "caption-model", "prompt-v1");
}
