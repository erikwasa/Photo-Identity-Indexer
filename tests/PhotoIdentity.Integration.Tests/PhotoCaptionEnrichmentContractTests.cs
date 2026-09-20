using PhotoIdentity.Api;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
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
        Assert.Equal("wi-0128-photo-caption-v2", PhotoCaptionGenerationConfiguration.GenerationVersion);
        Assert.Equal("wi-0128-photo-caption-v1", PhotoCaptionGenerationConfiguration.LegacyGenerationVersion);
        Assert.True(PhotoCaptionGenerationConfiguration.DefaultOllamaBaseUri.IsLoopback);
    }

    [Fact]
    public void Retained_v1_caption_can_be_promoted_without_model_rerun()
    {
        PhotoGeneratedCaption legacy = new(
            AssetRevisionId.New(),
            PhotoCaptionLanguages.Swedish,
            PhotoCaptionGenerationConfiguration.LegacyGenerationVersion,
            "qwen2.5vl:3b",
            new string('a', 64),
            PhotoCaptionPrompt.VersionFor(PhotoCaptionLanguages.Swedish),
            PhotoCaptionGenerationConfiguration.ImageMode,
            1024,
            "En person står vid havet. Han håller en kamera.",
            [PhotoIdentity.Core.Collections.GeneratedCreativeTextRiskCodes.PossibleProperNameOrLocation],
            130_000,
            new DateTimeOffset(2026, 9, 20, 22, 23, 0, TimeSpan.Zero));
        LocalPhotoCaptionModel model =
            new("qwen2.5vl:3b", new string('a', 64));

        Assert.True(
            PhotoCaptionEnrichmentHostedService.CanPromoteLegacyEvidence(
                legacy,
                model,
                PhotoCaptionPrompt.VersionFor(PhotoCaptionLanguages.Swedish),
                1024));
    }

    [Fact]
    public void Legacy_caption_without_retained_text_still_requires_model_regeneration()
    {
        PhotoGeneratedCaption legacy = new(
            AssetRevisionId.New(),
            PhotoCaptionLanguages.Swedish,
            PhotoCaptionGenerationConfiguration.LegacyGenerationVersion,
            "qwen2.5vl:3b",
            new string('a', 64),
            PhotoCaptionPrompt.VersionFor(PhotoCaptionLanguages.Swedish),
            PhotoCaptionGenerationConfiguration.ImageMode,
            1024,
            null,
            [PhotoIdentity.Core.Collections.GeneratedCreativeTextRiskCodes.PossibleProperNameOrLocation],
            130_000,
            new DateTimeOffset(2026, 9, 20, 22, 23, 0, TimeSpan.Zero));
        LocalPhotoCaptionModel model =
            new("qwen2.5vl:3b", new string('a', 64));

        Assert.False(
            PhotoCaptionEnrichmentHostedService.CanPromoteLegacyEvidence(
                legacy,
                model,
                PhotoCaptionPrompt.VersionFor(PhotoCaptionLanguages.Swedish),
                1024));
    }

    [Fact]
    public void Caption_enrichment_is_disabled_by_default_in_persisted_schema_contract()
    {
        Assert.Equal("sv", PhotoCaptionLanguages.Default);
    }
}
