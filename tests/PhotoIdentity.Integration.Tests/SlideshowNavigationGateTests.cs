using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowNavigationGateTests
{
    [Fact]
    public void Rapid_navigation_keeps_only_one_pending_direction()
    {
        SlideshowNavigationGate gate = new();

        Assert.True(gate.TryStart(
            SlideshowNavigationDirection.Next,
            out SlideshowNavigationDirection started));
        Assert.Equal(SlideshowNavigationDirection.Next, started);
        Assert.True(gate.IsActive);

        Assert.False(gate.TryStart(
            SlideshowNavigationDirection.Previous,
            out _));
        Assert.False(gate.TryStart(
            SlideshowNavigationDirection.Next,
            out _));
        Assert.False(gate.TryStart(
            SlideshowNavigationDirection.Previous,
            out _));

        Assert.True(gate.CompleteStep(out SlideshowNavigationDirection pending));
        Assert.Equal(SlideshowNavigationDirection.Previous, pending);

        Assert.False(gate.CompleteStep(out SlideshowNavigationDirection none));
        Assert.Equal(SlideshowNavigationDirection.None, none);
        Assert.False(gate.IsActive);
    }

    [Fact]
    public void Gate_can_be_reset_after_navigation_or_disposal()
    {
        SlideshowNavigationGate gate = new();

        _ = gate.TryStart(SlideshowNavigationDirection.Next, out _);
        _ = gate.TryStart(SlideshowNavigationDirection.Previous, out _);
        gate.Reset();

        Assert.False(gate.IsActive);
        Assert.True(gate.TryStart(
            SlideshowNavigationDirection.Next,
            out SlideshowNavigationDirection started));
        Assert.Equal(SlideshowNavigationDirection.Next, started);
    }
}
