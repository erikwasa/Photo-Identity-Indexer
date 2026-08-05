using PhotoIdentity.Web.Pages;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class DetectorComparisonViewStateTests
{
    [Fact]
    public void Moving_to_another_photo_resets_zoom_and_active_decision()
    {
        DetectorComparisonViewState state = new();
        state.SetZoom(2);
        state.Activate("candidate:3");

        state.Reset();

        Assert.Null(state.ZoomScale);
        Assert.Null(state.ActiveReviewKey);
        Assert.Equal("Fit to workspace", state.ZoomLabel);
        Assert.False(state.CanZoomOut);
        Assert.True(state.CanZoomIn);
    }

    [Fact]
    public void Zoom_steps_follow_fit_100_200_400_and_back()
    {
        DetectorComparisonViewState state = new();

        state.ZoomIn();
        Assert.Equal(1d, state.ZoomScale);
        state.ZoomIn();
        Assert.Equal(2d, state.ZoomScale);
        state.ZoomIn();
        Assert.Equal(4d, state.ZoomScale);
        Assert.False(state.CanZoomIn);

        state.ZoomOut();
        Assert.Equal(2d, state.ZoomScale);
        state.ZoomOut();
        Assert.Equal(1d, state.ZoomScale);
        state.ZoomOut();
        Assert.Null(state.ZoomScale);
    }
}
