using PhotoIdentity.Core.Collections;

namespace PhotoIdentity.Core.Tests;

public sealed class CreativeCollectionSearchAnchorTests
{
    [Theory]
    [InlineData(null, PhotoSearchModes.Combined)]
    [InlineData("semantic", PhotoSearchModes.Semantic)]
    [InlineData("CAPTION", PhotoSearchModes.Caption)]
    public void Create_normalizes_supported_modes(string? mode, string expected)
    {
        CreativeCollectionSearchAnchor anchor = CreativeCollectionSearchAnchor.Create(
            "  children swimming  ",
            mode,
            limit: 40);

        Assert.Equal("children swimming", anchor.Query);
        Assert.Equal(expected, anchor.Mode);
        Assert.Equal(40, anchor.Limit);
        Assert.Equal(CreativeCollectionAnchorPolicies.SearchRankedTopNV1, anchor.PolicyVersion);
    }

    [Fact]
    public void Recipe_requires_exactly_one_anchor_source()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CreativeCollectionRecipeSettings settings = CreativeCollectionRecipe.DefaultSettings;
        CreativeCollectionSearchAnchor search =
            CreativeCollectionSearchAnchor.Create("birthday cake");

        CreativeCollectionRecipe searchRecipe = new(
            CreativeCollectionId.New(),
            "Search",
            AnchorCollectionId: null,
            settings.TargetCount,
            settings.MomentGapMinutes,
            settings.MomentPolicyVersion,
            settings.ContextPolicyVersion,
            settings.SelectionPolicyVersion,
            settings.OrderingPolicyVersion,
            settings.NoveltyEnabled,
            now,
            now,
            search);
        searchRecipe.ValidateAnchorSupported();
        Assert.Equal(CreativeCollectionAnchorKinds.Search, searchRecipe.AnchorKind);

        CreativeCollectionRecipe invalid = searchRecipe with
        {
            AnchorCollectionId = SmartCollectionId.New(),
        };
        Assert.Throws<InvalidDataException>(invalid.ValidateAnchorSupported);
    }

    [Fact]
    public void Search_anchor_limit_is_bounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreativeCollectionSearchAnchor.Create("beach", limit: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreativeCollectionSearchAnchor.Create(
                "beach",
                limit: CreativeCollectionSearchAnchor.MaximumLimit + 1));
    }
}
