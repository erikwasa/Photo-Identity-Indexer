using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowProtectionStateTests
{
    [Fact]
    public void Protected_navigation_is_guarded_until_deliberate_exit()
    {
        SlideshowProtectionState state = new();

        Assert.True(state.Enabled);
        Assert.True(state.ShouldPreventNavigation(deliberateExit: false));
        Assert.False(state.ShouldPreventNavigation(deliberateExit: true));

        state.Configure(enabled: false);
        Assert.False(state.ShouldPreventNavigation(deliberateExit: false));
    }

    [Fact]
    public void Two_corner_unlock_requires_both_pointers_for_the_full_threshold()
    {
        SlideshowProtectionState state = new();
        DateTimeOffset start = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.False(state.PointerDown(SlideshowParentZone.Left, 10, start));
        Assert.True(state.PointerDown(
            SlideshowParentZone.Right,
            11,
            start.AddMilliseconds(100)));

        Assert.False(state.TryCompleteParentUnlock(
            start.AddMilliseconds(100)
                .Add(SlideshowProtectionTuning.ParentUnlockHold)
                .AddMilliseconds(-1)));

        state.PointerUp(SlideshowParentZone.Left, 10);
        Assert.False(state.TryCompleteParentUnlock(start.AddSeconds(5)));
        Assert.False(state.ParentControlsOpen);

        Assert.False(state.PointerDown(SlideshowParentZone.Left, 20, start.AddSeconds(6)));
        Assert.True(state.PointerDown(SlideshowParentZone.Right, 21, start.AddSeconds(6)));

        Assert.True(state.TryCompleteParentUnlock(
            start.AddSeconds(6).Add(SlideshowProtectionTuning.ParentUnlockHold)));
        Assert.True(state.ParentControlsOpen);
        Assert.Equal(SlideshowRecoveryReason.ParentUnlocked, state.RecoveryReason);
    }

    [Fact]
    public void Browser_back_and_fullscreen_loss_enter_recovery_without_disabling_protection()
    {
        SlideshowProtectionState state = new();

        state.OpenParentControls(SlideshowRecoveryReason.BrowserBack);
        Assert.True(state.ParentControlsOpen);
        Assert.Equal(SlideshowRecoveryReason.BrowserBack, state.RecoveryReason);

        state.CloseParentControls();
        Assert.False(state.ParentControlsOpen);

        state.ShowRecovery(SlideshowRecoveryReason.FullscreenLost);
        Assert.True(state.Enabled);
        Assert.Equal(SlideshowRecoveryReason.FullscreenLost, state.RecoveryReason);

        state.ClearRecovery();
        Assert.Equal(SlideshowRecoveryReason.None, state.RecoveryReason);
    }

    [Fact]
    public void Parent_gesture_tuning_matches_the_V1_contract()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), SlideshowProtectionTuning.ParentUnlockHold);
        Assert.Equal(TimeSpan.FromSeconds(1.5), SlideshowProtectionTuning.ExitHold);
        Assert.False(string.IsNullOrWhiteSpace(SlideshowProtectionTuning.ParentZoneSizeCss));
        Assert.False(string.IsNullOrWhiteSpace(SlideshowProtectionTuning.ParentZoneInsetCss));
    }

    [Fact]
    public void Capability_status_distinguishes_support_from_successful_acquisition()
    {
        SlideshowBrowserProtectionStatus healthy = new(
            new(true, true, false),
            new(true, true, false, Mode: "exact"),
            new(true, true, false),
            SecureContext: true,
            StartingOrientation: "portrait-primary");

        Assert.Empty(healthy.ParentWarnings());

        SlideshowBrowserProtectionStatus degraded = new(
            new(true, true, false),
            new(true, false, true, "orientation rejected"),
            new(false, false, false),
            SecureContext: false,
            StartingOrientation: "portrait-primary");

        IReadOnlyList<string> warnings = degraded.ParentWarnings();

        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, warning => warning.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("rotation lock", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, warning => warning.Contains("wake lock", StringComparison.OrdinalIgnoreCase));
    }
}
