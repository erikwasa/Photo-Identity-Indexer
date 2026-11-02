using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PhotoIdentity.Integration.Tests;

/// <summary>
/// The playback endpoint deliberately delegates RFC byte-range handling to
/// ASP.NET's range-enabled FileStream result. Keep its 206/416 contract pinned
/// without a media decoder, real photos or a PostgreSQL fixture.
/// </summary>
public sealed class VideoPlaybackRangeResultTests
{
    [Fact]
    public async Task Single_range_returns_206_with_only_requested_bytes()
    {
        byte[] mp4 = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
        using ServiceProvider services = new ServiceCollection()
            .AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.Headers.Range = "bytes=16-31";
        using var response = new MemoryStream();
        context.Response.Body = response;

        await Results.File(new MemoryStream(mp4), "video/mp4",
            enableRangeProcessing: true).ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status206PartialContent, context.Response.StatusCode);
        Assert.Equal("bytes 16-31/128", context.Response.Headers.ContentRange.ToString());
        Assert.Equal("bytes", context.Response.Headers.AcceptRanges.ToString());
        Assert.Equal(mp4.Skip(16).Take(16).ToArray(), response.ToArray());
    }

    [Fact]
    public async Task Unsatisfiable_range_returns_416_without_media_bytes()
    {
        byte[] mp4 = new byte[128];
        using ServiceProvider services = new ServiceCollection()
            .AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.Headers.Range = "bytes=1000-2000";
        using var response = new MemoryStream();
        context.Response.Body = response;

        await Results.File(new MemoryStream(mp4), "video/mp4",
            enableRangeProcessing: true).ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status416RangeNotSatisfiable, context.Response.StatusCode);
        Assert.Equal("bytes */128", context.Response.Headers.ContentRange.ToString());
        Assert.Empty(response.ToArray());
    }
}
