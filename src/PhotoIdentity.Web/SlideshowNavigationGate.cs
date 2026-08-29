namespace PhotoIdentity.Web;

public enum SlideshowNavigationDirection
{
    None,
    Next,
    Previous,
}

public sealed class SlideshowNavigationGate
{
    private SlideshowNavigationDirection _pending;

    public bool IsActive { get; private set; }

    public bool TryStart(
        SlideshowNavigationDirection direction,
        out SlideshowNavigationDirection started)
    {
        started = SlideshowNavigationDirection.None;
        if (direction == SlideshowNavigationDirection.None)
        {
            return false;
        }

        if (IsActive)
        {
            _pending = direction;
            return false;
        }

        IsActive = true;
        started = direction;
        return true;
    }

    public bool CompleteStep(out SlideshowNavigationDirection next)
    {
        next = _pending;
        _pending = SlideshowNavigationDirection.None;

        if (next != SlideshowNavigationDirection.None)
        {
            return true;
        }

        IsActive = false;
        return false;
    }

    public void Reset()
    {
        IsActive = false;
        _pending = SlideshowNavigationDirection.None;
    }
}
