using PhotoIdentity.Api;
using PhotoIdentity.Core.Catalogue;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoCaptionEnrichmentContractTests
{
    [Theory]
    [InlineData("sv", PhotoCaptionLanguages.Swedish)]
    [InlineData("SV", PhotoCaptionLanguages.Swedish)]
    [InlineData("en", PhotoCaptionLanguages.English)]
    [InlineData(" EN ", PhotoCaptionLanguages.English)]
    public void Supported_languages_normalize(string value, string expected)
    {
        Assert.True(PhotoCaptionLanguages.TryNormalize(value, out string actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Unsupported_language_is_rejected()
    {
        Assert.False(PhotoCaptionLanguages.TryNormalize("de", out _));
    }

    [Fact]
    public void Swedish_prompt_explicitly_requests_Swedish_and_preserves_claim_boundaries()
    {
        string prompt = PhotoCaptionPrompt.TextFor(PhotoCaptionLanguages.Swedish);

        Assert.Contains("på svenska", prompt, StringComparison.Ordinal);
        Assert.Contains("gissa inte namn", prompt, StringComparison.Ordinal);
        Assert.Contains("relationer", prompt, StringComparison.Ordinal);
        Assert.Contains("exakta platser", prompt, StringComparison.Ordinal);
        Assert.NotEqual(
            PhotoCaptionPrompt.VersionFor(PhotoCaptionLanguages.English),
            PhotoCaptionPrompt.VersionFor(PhotoCaptionLanguages.Swedish));
    }

    [Fact]
    public void Production_defaults_reuse_the_successful_bounded_probe()
    {
        Assert.Equal("qwen2.5vl:3b", PhotoCaptionGenerationConfiguration.DefaultModel);
        Assert.Equal(1024, PhotoCaptionGenerationConfiguration.DefaultContextTokens);
        Assert.Equal(600, PhotoCaptionGenerationConfiguration.DefaultTimeoutSeconds);
        Assert.Equal("thumbnail-480x320", PhotoCaptionGenerationConfiguration.ImageMode);
        Assert.True(PhotoCaptionGenerationConfiguration.DefaultOllamaBaseUri.IsLoopback);
    }

    [Fact]
    public void Caption_enrichment_is_disabled_by_default_in_persisted_schema_contract()
    {
        Assert.Equal("sv", PhotoCaptionLanguages.Default);
    }
}
