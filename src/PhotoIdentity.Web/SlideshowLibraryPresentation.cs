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

    public static int ManualPhotoCount(PhotoListCollectionResponse collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return collection.RevisionIds?.Length ?? 0;
    }

    public static string PhotoCountLabel(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count == 1 ? "1 photo" : $"{count} photos";
    }

    public static string CreativeTargetLabel(int targetCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetCount);
        return $"Up to {PhotoCountLabel(targetCount)}";
    }

    public static bool ShowPreparedIndicator(string? preparationState, bool busy) =>
        !busy && string.Equals(preparationState, "ready", StringComparison.OrdinalIgnoreCase);
}
