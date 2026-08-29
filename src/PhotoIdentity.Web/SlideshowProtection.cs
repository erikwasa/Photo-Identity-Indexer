namespace PhotoIdentity.Web;

public static class SlideshowProtectionTuning
{
    public static TimeSpan ParentUnlockHold { get; } = TimeSpan.FromSeconds(2);
    public static TimeSpan ExitHold { get; } = TimeSpan.FromSeconds(1.5);
    public const string ParentZoneSizeCss = "4.75rem";
    public const string ParentZoneInsetCss = "0.75rem";
}

public enum SlideshowParentZone
{
    Left,
    Right,
}

public enum SlideshowRecoveryReason
{
    None,
    ParentUnlocked,
    BrowserBack,
    FullscreenLost,
    ProtectionWarning,
}

public sealed class SlideshowProtectionState
{
    private long? _leftPointerId;
    private long? _rightPointerId;
    private DateTimeOffset? _holdStartedAt;

    public bool Enabled { get; private set; } = true;
    public bool ParentControlsOpen { get; private set; }
    public SlideshowRecoveryReason RecoveryReason { get; private set; }

    public void Configure(bool enabled)
    {
        Enabled = enabled;
        CancelParentHold();

        if (!enabled)
        {
            ParentControlsOpen = false;
            RecoveryReason = SlideshowRecoveryReason.None;
        }
    }

    public bool ShouldPreventNavigation(bool deliberateExit) =>
        Enabled && !deliberateExit;

    public bool PointerDown(
        SlideshowParentZone zone,
        long pointerId,
        DateTimeOffset now)
    {
        if (!Enabled || ParentControlsOpen)
        {
            return false;
        }

        switch (zone)
        {
            case SlideshowParentZone.Left:
                _leftPointerId ??= pointerId;
                break;
            case SlideshowParentZone.Right:
                _rightPointerId ??= pointerId;
                break;
        }

        if (_leftPointerId.HasValue &&
            _rightPointerId.HasValue &&
            !_holdStartedAt.HasValue)
        {
            _holdStartedAt = now;
            return true;
        }

        return false;
    }

    public void PointerUp(SlideshowParentZone zone, long pointerId)
    {
        bool releasedActivePointer = zone switch
        {
            SlideshowParentZone.Left => _leftPointerId == pointerId,
            SlideshowParentZone.Right => _rightPointerId == pointerId,
            _ => false,
        };

        if (releasedActivePointer)
        {
            CancelParentHold();
        }
    }

    public bool TryCompleteParentUnlock(DateTimeOffset now)
    {
        if (!Enabled ||
            ParentControlsOpen ||
            !_leftPointerId.HasValue ||
            !_rightPointerId.HasValue ||
            !_holdStartedAt.HasValue ||
            now - _holdStartedAt.Value < SlideshowProtectionTuning.ParentUnlockHold)
        {
            return false;
        }

        ParentControlsOpen = true;
        RecoveryReason = SlideshowRecoveryReason.ParentUnlocked;
        CancelParentHold();
        return true;
    }

    public void OpenParentControls(SlideshowRecoveryReason reason)
    {
        if (!Enabled)
        {
            return;
        }

        ParentControlsOpen = true;
        RecoveryReason = reason;
        CancelParentHold();
    }

    public void ShowRecovery(SlideshowRecoveryReason reason)
    {
        if (!Enabled)
        {
            return;
        }

        RecoveryReason = reason;
        CancelParentHold();
    }

    public void ClearRecovery()
    {
        if (!ParentControlsOpen)
        {
            RecoveryReason = SlideshowRecoveryReason.None;
        }
    }

    public void CloseParentControls()
    {
        ParentControlsOpen = false;
        RecoveryReason = SlideshowRecoveryReason.None;
        CancelParentHold();
    }

    public void CancelParentHold()
    {
        _leftPointerId = null;
        _rightPointerId = null;
        _holdStartedAt = null;
    }
}

public sealed record SlideshowBrowserFeatureStatus(
    bool Supported,
    bool Active,
    bool Failed,
    string? Message = null,
    string? Mode = null);

public sealed record SlideshowBrowserProtectionStatus(
    SlideshowBrowserFeatureStatus Fullscreen,
    SlideshowBrowserFeatureStatus OrientationLock,
    SlideshowBrowserFeatureStatus WakeLock,
    bool SecureContext,
    string? StartingOrientation)
{
    public static SlideshowBrowserProtectionStatus Unknown { get; } = new(
        new(false, false, false),
        new(false, false, false),
        new(false, false, false),
        false,
        null);

    public IReadOnlyList<string> ParentWarnings()
    {
        List<string> warnings = [];

        if (!SecureContext)
        {
            warnings.Add("This slideshow is not running in a secure browser context. Use the supported HTTPS phone path before toddler handoff.");
        }

        if (!OrientationLock.Active)
        {
            warnings.Add(OrientationLock.Supported
                ? "Screen orientation could not be locked. Enable the phone's system rotation lock before handoff."
                : "This browser does not support slideshow orientation lock. Enable the phone's system rotation lock before handoff.");
        }

        if (!WakeLock.Active)
        {
            warnings.Add(WakeLock.Supported
                ? "Screen wake lock is not active. The display may turn off during the slideshow."
                : "This browser does not support screen wake lock. Keep the device awake using system settings.");
        }

        return warnings;
    }
}
