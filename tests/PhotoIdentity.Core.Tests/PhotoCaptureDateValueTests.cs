using PhotoIdentity.Core.Sources;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class PhotoCaptureDateValueTests
{
    [Theory]
    [InlineData("1987", "year", "1987-01-01", "1987-12-31")]
    [InlineData("2000-02", "month", "2000-02-01", "2000-02-29")]
    [InlineData("2026-09-20", "day", "2026-09-20", "2026-09-20")]
    public void Parses_supported_precision_without_storing_invented_components(
        string input,
        string precision,
        string from,
        string to)
    {
        PhotoCaptureDateValue value = PhotoCaptureDateValue.Parse(input);

        Assert.Equal(precision, value.Precision);
        Assert.Equal(input, value.ToString());
        Assert.Equal(DateOnly.Parse(from), value.InclusiveRange.From);
        Assert.Equal(DateOnly.Parse(to), value.InclusiveRange.To);

        if (precision == PhotoCaptureDatePrecisions.Year)
        {
            Assert.Null(value.Month);
            Assert.Null(value.Day);
        }
        else if (precision == PhotoCaptureDatePrecisions.Month)
        {
            Assert.NotNull(value.Month);
            Assert.Null(value.Day);
        }
    }

    [Theory]
    [InlineData("2026/09/20")]
    [InlineData("not-a-date")]
    public void Rejects_unsupported_formats(string input)
    {
        Assert.Throws<FormatException>(() => PhotoCaptureDateValue.Parse(input));
    }

    [Fact]
    public void Rejects_invalid_calendar_components()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PhotoCaptureDateValue.Parse("2026-13"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PhotoCaptureDateValue.Parse("2026-02-30"));
    }

    [Fact]
    public void Rejects_blank_values()
    {
        Assert.Throws<ArgumentException>(() => PhotoCaptureDateValue.Parse(""));
    }

    [Fact]
    public void Day_requires_month()
    {
        Assert.Throws<ArgumentException>(() => new PhotoCaptureDateValue(2026, day: 20));
    }
}
