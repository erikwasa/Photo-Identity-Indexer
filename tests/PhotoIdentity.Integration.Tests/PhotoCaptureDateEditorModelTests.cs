using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PhotoCaptureDateEditorModelTests
{
    [Theory]
    [InlineData("1987", "1987")]
    [InlineData(" 2000-02 ", "2000-02")]
    [InlineData("2026-09-20", "2026-09-20")]
    public void Normalize_accepts_all_supported_precisions(string input, string expected)
    {
        Assert.True(PhotoCaptureDateEditorModel.TryNormalize(input, out string normalized, out string? error));
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("2026-02-30")]
    [InlineData("2026/09/20")]
    [InlineData("20")]
    public void Normalize_rejects_invalid_mobile_input(string input)
    {
        Assert.False(PhotoCaptureDateEditorModel.TryNormalize(input, out string normalized, out string? error));
        Assert.Empty(normalized);
        Assert.NotNull(error);
    }

    [Fact]
    public void Labels_make_manual_precision_and_extracted_provenance_explicit()
    {
        PhotoCaptureDateResponse manual = new(
            "1987-01-01",
            "1987-12-31",
            "manual",
            "year",
            "1987",
            new DateTime(2025, 5, 6, 7, 8, 9),
            true);

        Assert.Equal("1987", PhotoCaptureDateEditorModel.EffectiveLabel(manual));
        Assert.Equal("Manual override", PhotoCaptureDateEditorModel.SourceLabel(manual.Source));
        Assert.Equal("Year only", PhotoCaptureDateEditorModel.PrecisionLabel(manual.Precision));

        PhotoCaptureDateResponse extracted = manual with
        {
            Source = "extracted",
            Precision = "timestamp",
            ManualValue = null,
        };
        Assert.Equal("Extracted metadata", PhotoCaptureDateEditorModel.SourceLabel(extracted.Source));
        Assert.Equal("Exact timestamp", PhotoCaptureDateEditorModel.PrecisionLabel(extracted.Precision));
        Assert.Equal("2025-05-06 07:08:09", PhotoCaptureDateEditorModel.EffectiveLabel(extracted));
    }
}
