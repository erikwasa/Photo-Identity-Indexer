using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public static class SlideshowLibraryPresentation
{
    public static string PlayAriaLabel(string collectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        return $"Play {collectionName.Trim()} slideshow";
    }

    public static string? SelectCoverThumbnailUrl(SmartCollectionPageResponse? page) =>
        page?.Items.FirstOrDefault()?.ThumbnailUrl;
}
