using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionDateEditorModelTests
{
    [Theory]
    [InlineData("year", "1987", "", "", "", "", "1987-01-01", "1987-12-31")]
    [InlineData("month", "", "2000-02", "", "", "", "2000-02-01", "2000-02-29")]
    [InlineData("date", "", "", "2026-09-20", "", "", "2026-09-20", "2026-09-20")]
    [InlineData("range", "", "", "", "2020-01-02", "2021-03-04", "2020-01-02", "2021-03-04")]
    public void Structured_modes_build_explicit_inclusive_ranges(
        string mode,
        string year,
        string month,
        string date,
        string from,
        string to,
        string expectedFrom,
        string expectedTo)
    {
        Assert.True(SmartCollectionDateEditorModel.TryBuildRange(
            mode, year, month, date, from, to, out SmartCollectionDateRangeRequest? range, out string? error));

        Assert.NotNull(range);
        Assert.Equal(expectedFrom, range!.From);
        Assert.Equal(expectedTo, range.To);
        Assert.Null(error);
    }

    [Fact]
    public void Any_date_builds_no_range()
    {
        Assert.True(SmartCollectionDateEditorModel.TryBuildRange(
            "any", "", "", "", "", "", out SmartCollectionDateRangeRequest? range, out string? error));
        Assert.Null(range);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("year", "", "", "", "", "")]
    [InlineData("month", "", "2026-13", "", "", "")]
    [InlineData("date", "", "", "2026-02-30", "", "")]
    [InlineData("range", "", "", "", "2026-02-01", "")]
    [InlineData("range", "", "", "", "2026-03-01", "2026-02-01")]
    public void Partial_or_invalid_states_cannot_build(
        string mode,
        string year,
        string month,
        string date,
        string from,
        string to)
    {
        Assert.False(SmartCollectionDateEditorModel.TryBuildRange(
            mode, year, month, date, from, to, out SmartCollectionDateRangeRequest? range, out string? error));
        Assert.Null(range);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("1987-01-01", "1987-12-31", "year", "1987")]
    [InlineData("2000-02-01", "2000-02-29", "month", "2000-02")]
    [InlineData("2026-09-20", "2026-09-20", "date", "2026-09-20")]
    [InlineData("2020-01-02", "2021-03-04", "range", "2020-01-02")]
    public void Saved_ranges_reopen_as_equivalent_structured_controls(
        string from,
        string to,
        string expectedMode,
        string expectedPrimaryValue)
    {
        SmartCollectionDateEditorState state = SmartCollectionDateEditorModel.FromRange(
            new SmartCollectionDateRangeResponse(from, to));

        Assert.Equal(expectedMode, state.Mode);
        string actual = state.Mode switch
        {
            SmartCollectionDateModes.Year => state.Year,
            SmartCollectionDateModes.Month => state.Month,
            SmartCollectionDateModes.Date => state.Date,
            _ => state.From,
        };
        Assert.Equal(expectedPrimaryValue, actual);
    }

    [Theory]
    [InlineData("2016", "year", "2016")]
    [InlineData("2020-2021", "range", "2020-01-01")]
    [InlineData("2025/05/01-2025/05/10", "range", "2025-05-01")]
    public void Legacy_transient_date_syntax_can_be_restored_without_showing_syntax(
        string legacy,
        string expectedMode,
        string expectedPrimaryValue)
    {
        SmartCollectionDateEditorState state = SmartCollectionDateEditorModel.FromLegacyExpression(legacy);
        Assert.Equal(expectedMode, state.Mode);

        string actual = state.Mode switch
        {
            SmartCollectionDateModes.Year => state.Year,
            SmartCollectionDateModes.Month => state.Month,
            SmartCollectionDateModes.Date => state.Date,
            _ => state.From,
        };
        Assert.Equal(expectedPrimaryValue, actual);
    }
}
