using System.Globalization;
using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoDetailsPresentationTests
{
    [Theory]
    [InlineData("2024-05-06 14:30")]
    [InlineData(" 2024-05-06 14:30 ")]
    public void Local_clock_text_is_converted_to_an_explicit_offset(string input)
    {
        TimeZoneInfo local = TimeZoneInfo.CreateCustomTimeZone(
            "wi0212-test-utc-plus-two", TimeSpan.FromHours(2),
            "Test UTC+2", "Test UTC+2");

        Assert.True(VideoDetailsPresentation.TryParseLocalRecordingTime(
            input, local, out DateTimeOffset instant, out string? error));
        Assert.Null(error);
        Assert.Equal(TimeSpan.FromHours(2), instant.Offset);
        Assert.Equal(new DateTimeOffset(2024, 5, 6, 12, 30, 0, TimeSpan.Zero),
            instant.ToUniversalTime());
    }

    [Theory]
    [InlineData("24/02/2012 00:00")]
    [InlineData("2012-02-24T00:00")]
    [InlineData("2012-02-24 00:00:00")]
    [InlineData("2024-02-30 12:00")]
    [InlineData("2024-05-06 25:30")]
    [InlineData("")]
    public void Local_clock_input_requires_valid_invariant_wall_time(string input)
    {
        Assert.False(VideoDetailsPresentation.TryParseLocalRecordingTime(
            input, TimeZoneInfo.Utc, out _, out string? error));
        Assert.Contains("YYYY-MM-DD HH:MM", error);
    }

    [Fact]
    public void Local_clock_input_rejects_dst_gaps_and_ambiguous_times()
    {
        TimeZoneInfo sweden = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

        Assert.False(VideoDetailsPresentation.TryParseLocalRecordingTime(
            "2024-03-31 02:30", sweden, out _, out string? gapError));
        Assert.Contains("daylight-saving", gapError);
        Assert.False(VideoDetailsPresentation.TryParseLocalRecordingTime(
            "2024-10-27 02:30", sweden, out _, out string? overlapError));
        Assert.Contains("daylight-saving", overlapError);

        Assert.True(VideoDetailsPresentation.TryParseLocalRecordingTime(
            "2024-05-06 14:30", sweden, out DateTimeOffset valid, out _));
        Assert.Equal(TimeSpan.FromHours(2), valid.Offset);
    }

    [Theory]
    [InlineData("496000/16577", "30 fps")]
    [InlineData("30000/1001", "30 fps")]
    [InlineData("24000/1001", "24 fps")]
    [InlineData("60000/1001", "60 fps")]
    [InlineData("25/1", "25 fps")]
    [InlineData("5/2", "3 fps")]
    [InlineData("0/1", "Unknown")]
    [InlineData("30/0", "Unknown")]
    [InlineData("n/a", "Unknown")]
    [InlineData(null, "Unknown")]
    public void Frame_rate_is_nearest_integer_or_unknown(string? rational, string expected)
    {
        Assert.Equal(expected, VideoDetailsPresentation.FormatFrameRate(rational));
    }

    [Theory]
    [InlineData(18_500_000L, "18.5 Mb/s")]
    [InlineData(12_000_000L, "12 Mb/s")]
    [InlineData(null, "Unknown")]
    public void Technical_bitrate_formats_without_claiming_perceptual_quality(
        long? bitrate, string expected)
    {
        Assert.Equal(expected, VideoDetailsPresentation.FormatVideoBitrate(bitrate));
    }

    [Theory]
    [InlineData("aac", 1, "mono", 44100, "AAC · Mono · 44.1 kHz")]
    [InlineData("aac", 2, null, 48000, "AAC · Stereo · 48 kHz")]
    [InlineData(null, 6, null, null, "6 channels")]
    [InlineData(null, null, null, null, "Unknown")]
    public void Audio_display_combines_codec_layout_and_sampling(
        string? codec, int? channels, string? layout, int? rate, string expected)
    {
        Assert.Equal(expected,
            VideoDetailsPresentation.FormatAudioDetails(codec, channels, layout, rate));
    }

    [Fact]
    public void Recording_time_input_is_formatted_with_a_space_not_a_browser_date_separator()
    {
        DateTimeOffset instant = new(2024, 5, 6, 12, 30, 0, TimeSpan.Zero);
        string formatted = VideoDetailsPresentation.FormatLocalRecordingTimeInput(instant);
        Assert.Equal(instant.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), formatted);
    }
}
