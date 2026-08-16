using PhotoIdentity.Core.Collections;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionFilterTests
{
    [Theory]
    [InlineData("2016", "2016-01-01", "2016-12-31")]
    [InlineData("2020-2021", "2020-01-01", "2021-12-31")]
    [InlineData("2025/05/01-2025/05/10", "2025-05-01", "2025-05-10")]
    public void Documented_taken_date_forms_normalize_to_explicit_inclusive_bounds(
        string input,
        string expectedFrom,
        string expectedTo)
    {
        SmartCollectionDateRange range = SmartCollectionDateRange.Parse(input);

        Assert.Equal(expectedFrom, range.From.ToString("yyyy-MM-dd"));
        Assert.Equal(expectedTo, range.To.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void Legacy_places_tag_migrates_to_named_location_while_generic_tags_remain_separate()
    {
        SmartCollectionFilter filter = new(tags: [" Places / Sweden / Stockholm ", "Trips / Family"]);

        Assert.Equal(["trips/family"], filter.Tags);
        Assert.Equal("places/sweden/stockholm", filter.LocationPlace);
    }

    [Fact]
    public void Invalid_location_bounds_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SmartCollectionGeoBounds(60, 20, 50, 30));
    }
}
