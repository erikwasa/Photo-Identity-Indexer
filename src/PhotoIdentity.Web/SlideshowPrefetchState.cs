namespace PhotoIdentity.Web;

public sealed record SlideshowPrefetchUpdate(
    IReadOnlyList<string> AddedUrls,
    IReadOnlyList<string> RemovedUrls)
{
    public bool HasChanges => AddedUrls.Count > 0 || RemovedUrls.Count > 0;
}

/// <summary>
/// Tracks the browser prefetch objects that should remain alive across slideshow renders.
/// The revision becoming current is retained when it was already prefetched so navigation
/// does not cancel the request immediately before the displayed image can reuse it.
/// </summary>
public sealed class SlideshowPrefetchState
{
    private readonly HashSet<string> _activeUrls = new(StringComparer.Ordinal);

    public SlideshowPrefetchUpdate Update(
        string? currentUrl,
        IEnumerable<string> desiredUrls)
    {
        ArgumentNullException.ThrowIfNull(desiredUrls);

        HashSet<string> target = desiredUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToHashSet(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(currentUrl) && _activeUrls.Contains(currentUrl))
        {
            target.Add(currentUrl);
        }

        string[] added = target
            .Where(url => !_activeUrls.Contains(url))
            .OrderBy(url => url, StringComparer.Ordinal)
            .ToArray();
        string[] removed = _activeUrls
            .Where(url => !target.Contains(url))
            .OrderBy(url => url, StringComparer.Ordinal)
            .ToArray();

        _activeUrls.Clear();
        _activeUrls.UnionWith(target);
        return new SlideshowPrefetchUpdate(added, removed);
    }

    public SlideshowPrefetchUpdate ReleaseDisplayed(string? currentUrl)
    {
        if (string.IsNullOrWhiteSpace(currentUrl) || !_activeUrls.Remove(currentUrl))
        {
            return new SlideshowPrefetchUpdate([], []);
        }

        return new SlideshowPrefetchUpdate([], [currentUrl]);
    }

    public SlideshowPrefetchUpdate Clear()
    {
        string[] removed = _activeUrls
            .OrderBy(url => url, StringComparer.Ordinal)
            .ToArray();
        _activeUrls.Clear();
        return new SlideshowPrefetchUpdate([], removed);
    }
}
