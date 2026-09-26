using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public static class SlideshowLibraryPresentation
{
    public static string PlayAriaLabel(
        string collectionName,
        string? quantityLabel = null,
        bool prepared = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        string label = $"Play {collectionName.Trim()} slideshow";
        if (!string.IsNullOrWhiteSpace(quantityLabel))
        {
            label += $". {quantityLabel.Trim()}";
        }

        if (prepared)
        {
            label += ". Prepared";
        }

        return label;
    }

    public static string? SelectCoverThumbnailUrl(SmartCollectionPageResponse? page) =>
        page?.Items.FirstOrDefault()?.ThumbnailUrl;

    public static int? SmartPhotoCount(SmartCollectionPageResponse? page) => page?.Total;

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
