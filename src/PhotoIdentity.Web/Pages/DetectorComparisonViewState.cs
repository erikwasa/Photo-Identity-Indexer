namespace PhotoIdentity.Web.Pages;

public sealed class DetectorComparisonViewState
{
    public double? ZoomScale { get; private set; }
    public string? ActiveReviewKey { get; private set; }

    public bool CanZoomOut => ZoomScale is not null;
    public bool CanZoomIn => ZoomScale is null || ZoomScale < 4;
    public string ZoomLabel => ZoomScale is null
        ? "Fit to workspace"
        : $"{ZoomScale.Value * 100:0}% of source pixels";

    public void SetZoom(double? scale)
    {
        ZoomScale = scale switch
        {
            null => null,
            <= 1 => 1,
            <= 2 => 2,
            _ => 4,
        };
    }

    public void ZoomIn()
    {
        ZoomScale = ZoomScale switch
        {
            null => 1,
            < 2 => 2,
            _ => 4,
        };
    }

    public void ZoomOut()
    {
        ZoomScale = ZoomScale switch
        {
            null => null,
            > 2 => 2,
            > 1 => 1,
            _ => null,
        };
    }

    public void Activate(string? reviewKey) => ActiveReviewKey = reviewKey;

    public void Clear(string reviewKey)
    {
        if (string.Equals(ActiveReviewKey, reviewKey, StringComparison.Ordinal))
        {
            ActiveReviewKey = null;
        }
    }

    public bool IsActive(string reviewKey) =>
        string.Equals(ActiveReviewKey, reviewKey, StringComparison.Ordinal);

    public void Reset()
    {
        ZoomScale = null;
        ActiveReviewKey = null;
    }
}
