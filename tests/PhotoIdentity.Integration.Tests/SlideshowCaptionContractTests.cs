using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowCaptionContractTests
{
    [Theory]
    [InlineData("sv", SlideshowCaptionLanguage.Swedish)]
    [InlineData("SV", SlideshowCaptionLanguage.Swedish)]
    [InlineData("en", SlideshowCaptionLanguage.English)]
    [InlineData(" EN ", SlideshowCaptionLanguage.English)]
    public void Supported_languages_normalize(string value, string expected)
    {
        Assert.True(SlideshowCaptionLanguage.TryNormalize(value, out string actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Unsupported_language_is_rejected()
    {
        Assert.False(SlideshowCaptionLanguage.TryNormalize("de", out _));
    }

    [Fact]
    public void Swedish_prompt_explicitly_requests_Swedish_and_preserves_claim_boundaries()
    {
        string prompt = SlideshowCaptionPrompt.TextFor(SlideshowCaptionLanguage.Swedish);

        Assert.Contains("på svenska", prompt, StringComparison.Ordinal);
        Assert.Contains("gissa inte namn", prompt, StringComparison.Ordinal);
        Assert.Contains("relationer", prompt, StringComparison.Ordinal);
        Assert.Contains("exakta platser", prompt, StringComparison.Ordinal);
        Assert.NotEqual(
            SlideshowCaptionPrompt.VersionFor(SlideshowCaptionLanguage.English),
            SlideshowCaptionPrompt.VersionFor(SlideshowCaptionLanguage.Swedish));
    }

    [Fact]
    public void Production_defaults_reuse_the_successful_bounded_probe()
    {
        Assert.Equal("qwen2.5vl:3b", SlideshowCaptionGenerationConfiguration.DefaultModel);
        Assert.Equal(1024, SlideshowCaptionGenerationConfiguration.DefaultContextTokens);
        Assert.Equal(600, SlideshowCaptionGenerationConfiguration.DefaultTimeoutSeconds);
        Assert.Equal(8, SlideshowCaptionGenerationConfiguration.DefaultQueueCapacity);
        Assert.True(SlideshowCaptionGenerationConfiguration.DefaultOllamaBaseUri.IsLoopback);
    }
}
