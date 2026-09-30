using PhotoIdentity.Core.Catalogue;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class CaptionInferenceEndpointTests
{
    [Theory]
    [InlineData(null, CaptionInferenceMode.Local)]
    [InlineData("Local", CaptionInferenceMode.Local)]
    [InlineData(" remote ", CaptionInferenceMode.Remote)]
    public void Mode_requires_explicit_remote_opt_in(string? value, CaptionInferenceMode expected) =>
        Assert.Equal(expected, CaptionInferenceEndpoint.ParseMode(value));

    [Theory]
    [InlineData("")]
    [InlineData("Auto")]
    [InlineData("1")]
    public void Unknown_modes_are_rejected(string value) =>
        Assert.Throws<ArgumentException>(() => CaptionInferenceEndpoint.ParseMode(value));

    [Theory]
    [InlineData("http://127.0.0.1:11434", CaptionInferenceMode.Local)]
    [InlineData("https://localhost:11434/", CaptionInferenceMode.Local)]
    [InlineData("http://[::1]:11434/", CaptionInferenceMode.Local)]
    [InlineData("https://caption.example.test/ollama", CaptionInferenceMode.Remote)]
    public void Valid_endpoints_preserve_host_and_normalize_base_path(string value, CaptionInferenceMode mode)
    {
        Uri uri = CaptionInferenceEndpoint.Parse(value, mode);
        Assert.Equal(new Uri(value).Host, uri.Host);
        Assert.EndsWith("/", uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://caption.example.test/", CaptionInferenceMode.Local)]
    [InlineData("http://caption.example.test/", CaptionInferenceMode.Remote)]
    [InlineData("http://localhost:11434/", CaptionInferenceMode.Remote)]
    [InlineData("relative/path", CaptionInferenceMode.Remote)]
    [InlineData("file:///private/photo.jpg", CaptionInferenceMode.Remote)]
    [InlineData("https://user:secret@caption.example.test/", CaptionInferenceMode.Remote)]
    [InlineData("https://caption.example.test/?token=secret", CaptionInferenceMode.Remote)]
    [InlineData("https://caption.example.test/#secret", CaptionInferenceMode.Remote)]
    [InlineData("http://localhost:11434/?token=secret", CaptionInferenceMode.Local)]
    public void Invalid_endpoints_fail_without_echoing_private_input(string value, CaptionInferenceMode mode)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => CaptionInferenceEndpoint.Parse(value, mode));
        Assert.DoesNotContain("secret", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private/photo", error.ToString(), StringComparison.Ordinal);
    }
}
