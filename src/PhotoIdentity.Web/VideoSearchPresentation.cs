using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

/// <summary>
/// Safe, minimal display policy for stored video frame evidence.
/// A search hit is not proof that poster bytes still exist on disk.
/// </summary>
public static class VideoSearchPresentation
{
    public static string? PosterPath(VideoSearchResultResponse result)
    {
        if (!Guid.TryParseExact(result.RevisionId, "D", out Guid revision) ||
            revision == Guid.Empty)
            return null;

        string expected = $"/api/videos/{revision:D}/poster";
        return string.Equals(expected, result.PosterUrl, StringComparison.Ordinal)
            ? expected : null;
    }

    public static string FrameTime(long milliseconds)
    {
        if (milliseconds < 0 || milliseconds > TimeSpan.MaxValue.TotalMilliseconds)
            return "Time unknown";

        TimeSpan value = TimeSpan.FromMilliseconds(milliseconds);
        return value.TotalHours >= 1
            ? $"{(long)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(long)value.TotalMinutes}:{value.Seconds:00}";
    }
}
