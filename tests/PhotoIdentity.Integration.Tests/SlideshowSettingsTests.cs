using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowSettingsTests
{
    [Fact]
    public void Defaults_match_the_M22_contract()
    {
        SlideshowSettings settings = SlideshowSettings.Defaults;

        Assert.True(settings.Autoplay);
        Assert.Equal(5, settings.ImageDurationSeconds);
        Assert.True(settings.ShowTimerProgress);
        Assert.Equal(SlideshowSettings.Loop, settings.AfterLastPhoto);
        Assert.True(settings.ProtectedSlideshow);
        Assert.False(settings.PrepareOriginals);
    }

    [Fact]
    public void Settings_round_trip_through_browser_storage_json()
    {
        SlideshowSettings expected = new(
            Autoplay: false,
            ImageDurationSeconds: 17,
            ShowTimerProgress: false,
            AfterLastPhoto: SlideshowSettings.Stop,
            ProtectedSlideshow: false,
            PrepareOriginals: true);

        SlideshowSettings actual = SlideshowSettings.FromJson(expected.ToJson());

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"autoplay\":\"not-a-boolean\"}")]
    [InlineData("[]")]
    public void Corrupt_settings_fall_back_to_documented_defaults(string json)
    {
        Assert.Equal(SlideshowSettings.Defaults, SlideshowSettings.FromJson(json));
    }

    [Fact]
    public void Unknown_or_out_of_range_values_fall_back_per_field()
    {
        SlideshowSettings settings = SlideshowSettings.FromJson(
            """
            {
              "autoplay": false,
              "imageDurationSeconds": 999,
              "showTimerProgress": false,
              "afterLastPhoto": "surprise",
              "protectedSlideshow": false,
              "prepareOriginals": true
            }
            """);

        Assert.False(settings.Autoplay);
        Assert.Equal(5, settings.ImageDurationSeconds);
        Assert.False(settings.ShowTimerProgress);
        Assert.Equal(SlideshowSettings.Loop, settings.AfterLastPhoto);
        Assert.False(settings.ProtectedSlideshow);
        Assert.True(settings.PrepareOriginals);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void Duration_accepts_full_supported_boundary(int seconds)
    {
        SlideshowSettings settings = (SlideshowSettings.Defaults with
        {
            ImageDurationSeconds = seconds,
        }).Normalize();

        Assert.Equal(seconds, settings.ImageDurationSeconds);
    }
}
