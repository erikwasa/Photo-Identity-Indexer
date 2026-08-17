using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class BrowserImageContentTypesTests
{
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    [InlineData("image/bmp")]
    [InlineData("image/avif")]
    public void Common_browser_image_types_are_renderable(string contentType)
    {
        Assert.True(BrowserImageContentTypes.CanRender(contentType));
    }

    [Theory]
    [InlineData("image/heic")]
    [InlineData("image/heif")]
    [InlineData("image/tiff")]
    [InlineData("application/octet-stream")]
    [InlineData(null)]
    public void Unsupported_original_types_require_a_browser_safe_preview(string? contentType)
    {
        Assert.False(BrowserImageContentTypes.CanRender(contentType));
    }
}
