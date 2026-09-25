using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionPlaceLabelTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_place_has_no_card_label(string? place)
    {
        Assert.Null(SmartCollectionPlaceLabel.FormatCompact(place));
    }

    [Fact]
    public void One_level_place_uses_the_only_meaningful_segment()
    {
        Assert.Equal("Stockholm", SmartCollectionPlaceLabel.FormatCompact("Stockholm"));
    }

    [Fact]
    public void Two_level_place_keeps_specific_place_and_country()
    {
        Assert.Equal("Stockholm · Sweden", SmartCollectionPlaceLabel.FormatCompact("Sweden/Stockholm"));
    }

    [Fact]
    public void Deep_place_keeps_leaf_and_country_without_repeating_the_full_hierarchy()
    {
        Assert.Equal(
            "Långbro · Sverige",
            SmartCollectionPlaceLabel.FormatCompact("Sverige/Stockholms län/Stockholms stad/Brännkyrka/Långbro"));
    }

    [Fact]
    public void Canonical_places_root_is_not_shown_to_the_user()
    {
        Assert.Equal(
            "SoHo · United States",
            SmartCollectionPlaceLabel.FormatCompact("Places/United States/New York/New York city/SoHo"));
    }
}
