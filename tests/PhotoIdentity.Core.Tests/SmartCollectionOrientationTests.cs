using PhotoIdentity.Core.Collections;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class SmartCollectionOrientationTests
{
    [Fact]
    public void Orientation_defaults_to_any_and_normalizes_supported_values()
    {
        Assert.Equal(
            SmartCollectionOrientations.Any,
            new SmartCollectionFilter().Orientation);
        Assert.Equal(
            SmartCollectionOrientations.Landscape,
            new SmartCollectionFilter(orientation: "  LANDSCAPE ").Orientation);
        Assert.Equal(
            SmartCollectionOrientations.Portrait,
            new SmartCollectionFilter(orientation: "Portrait").Orientation);
    }

    [Fact]
    public void Unsupported_orientation_is_rejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new SmartCollectionFilter(orientation: "square"));

        Assert.Contains("any", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("landscape", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("portrait", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
