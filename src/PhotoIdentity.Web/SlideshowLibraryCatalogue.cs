using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public enum SlideshowLibraryItemKind
{
    Smart,
    Manual,
    Creative,
}

public sealed record SlideshowLibraryItem(
    string Id,
    string Name,
    SlideshowLibraryItemKind Kind,
    string CoverCollectionId,
    string? CoverRevisionId,
    string[] RevisionIds,
    int? PhotoCount,
    int? CreativeTargetCount,
    string? SourceCollectionName)
{
    public string Key => $"{Kind}:{Id}";
    public bool Manual => Kind == SlideshowLibraryItemKind.Manual;
    public bool Creative => Kind == SlideshowLibraryItemKind.Creative;
    public bool SupportsPreparation => Kind != SlideshowLibraryItemKind.Creative;
}

public static class SlideshowLibraryCatalogue
{
    public static IReadOnlyList<SlideshowLibraryItem> Combine(
        IEnumerable<SlideshowLibraryCollectionResponse> smartCollections,
        IEnumerable<PhotoListCollectionResponse> manualCollections,
        IEnumerable<CreativeCollectionRecipeResponse> creativeCollections)
    {
        ArgumentNullException.ThrowIfNull(smartCollections);
        ArgumentNullException.ThrowIfNull(manualCollections);
        ArgumentNullException.ThrowIfNull(creativeCollections);

        IEnumerable<SlideshowLibraryItem> smart = smartCollections.Select(collection =>
            new SlideshowLibraryItem(
                collection.Id,
                collection.Name,
                SlideshowLibraryItemKind.Smart,
                collection.Id,
                CoverRevisionId: null,
                RevisionIds: [],
                PhotoCount: null,
                CreativeTargetCount: null,
                SourceCollectionName: null));

        IEnumerable<SlideshowLibraryItem> manual = manualCollections.Select(collection =>
            new SlideshowLibraryItem(
                collection.Id,
                collection.Name,
                SlideshowLibraryItemKind.Manual,
                collection.Id,
                collection.RevisionIds.FirstOrDefault(),
                collection.RevisionIds,
                SlideshowLibraryPresentation.ManualPhotoCount(collection),
                CreativeTargetCount: null,
                SourceCollectionName: null));

        IEnumerable<SlideshowLibraryItem> creative = creativeCollections.Select(collection =>
        {
            string coverCollectionId = collection.AnchorCollectionId
                ?? collection.SearchAnchor?.SmartCollectionId
                ?? string.Empty;
            string sourceName = collection.SearchAnchor is CreativeCollectionSearchAnchorResponse search
                ? string.IsNullOrWhiteSpace(search.SmartCollectionName)
                    ? $"Search: {search.Query}"
                    : $"Search: {search.Query} · {search.SmartCollectionName}"
                : collection.AnchorCollectionName ?? "Smart Collection";

            return new SlideshowLibraryItem(
                collection.Id,
                collection.Name,
                SlideshowLibraryItemKind.Creative,
                coverCollectionId,
                CoverRevisionId: null,
                RevisionIds: [],
                PhotoCount: null,
                CreativeTargetCount: collection.TargetCount,
                SourceCollectionName: sourceName);
        });

        return smart
            .Concat(manual)
            .Concat(creative)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ThenBy(item => item.Kind)
            .ToArray();
    }
}
