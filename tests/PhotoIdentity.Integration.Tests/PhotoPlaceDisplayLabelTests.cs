using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoPlaceDisplayLabelTests
{
    [Fact]
    public void Automatic_swedish_hierarchy_uses_city_and_most_specific_locality()
    {
        PhotoPlaceResponse place = Place(
            name: "Långbro",
            value: "Sverige/Stockholms län/Stockholms stad/Brännkyrka/Långbro",
            source: "automatic");

        Assert.Equal("Stockholm · Långbro", PhotoPlaceDisplayLabel.Format(place));
        Assert.Equal("Sverige/Stockholms län/Stockholms stad/Brännkyrka/Långbro", place.Value);
    }

    [Fact]
    public void Automatic_english_city_semantics_are_not_position_dependent()
    {
        PhotoPlaceResponse place = Place(
            name: "SoHo",
            value: "United States/New York/New York City/Manhattan/SoHo",
            source: "automatic");

        Assert.Equal("New York · SoHo", PhotoPlaceDisplayLabel.Format(place));
    }

    [Fact]
    public void Manual_hierarchy_uses_deterministic_parent_and_specific_fallback()
    {
        PhotoPlaceResponse place = Place(
            name: "Långbro",
            value: "Sverige/Stockholms län/Stockholms stad/Brännkyrka/Långbro",
            source: "manual");

        Assert.Equal("Brännkyrka · Långbro", PhotoPlaceDisplayLabel.Format(place));
    }

    private static PhotoPlaceResponse Place(string name, string value, string source) => new(
        Id: "place-1",
        Name: name,
        Value: value,
        Source: source,
        AssignedBy: "test",
        AssignedAtUtc: new DateTimeOffset(2026, 8, 21, 0, 0, 0, TimeSpan.Zero));
}
