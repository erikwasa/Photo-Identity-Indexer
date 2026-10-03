using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

public enum PhotoListCollectionChronologicalOrder
{
    OldestFirst,
    NewestFirst,
}

public static class PhotoListCollectionChronologicalOrdering
{
    public static AssetRevisionId[] Apply(
        IReadOnlyList<AssetRevisionId> revisionIds,
        IReadOnlyList<PhotoListCollectionCaptureTime> captureTimes,
        PhotoListCollectionChronologicalOrder order)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        ArgumentNullException.ThrowIfNull(captureTimes);

        Dictionary<AssetRevisionId, DateTime?> effectiveTimes = [];
        foreach (PhotoListCollectionCaptureTime captureTime in captureTimes)
        {
            if (!effectiveTimes.TryAdd(captureTime.RevisionId, captureTime.EffectiveTakenAtLocal))
            {
                throw new ArgumentException(
                    $"Capture-time evidence contains duplicate revision '{captureTime.RevisionId}'.",
                    nameof(captureTimes));
            }
        }

        var items = revisionIds
            .Select((revisionId, index) => new OrderedItem(
                revisionId,
                index,
                effectiveTimes.GetValueOrDefault(revisionId)))
            .ToArray();

        IEnumerable<OrderedItem> dated = items.Where(item => item.EffectiveTakenAtLocal.HasValue);
        dated = order switch
        {
            PhotoListCollectionChronologicalOrder.OldestFirst => dated
                .OrderBy(item => item.EffectiveTakenAtLocal!.Value)
                .ThenBy(item => item.OriginalIndex),
            PhotoListCollectionChronologicalOrder.NewestFirst => dated
                .OrderByDescending(item => item.EffectiveTakenAtLocal!.Value)
                .ThenBy(item => item.OriginalIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
        };

        IEnumerable<OrderedItem> undated = items
            .Where(item => !item.EffectiveTakenAtLocal.HasValue)
            .OrderBy(item => item.OriginalIndex);

        return dated
            .Concat(undated)
            .Select(item => item.RevisionId)
            .ToArray();
    }

    private sealed record OrderedItem(
        AssetRevisionId RevisionId,
        int OriginalIndex,
        DateTime? EffectiveTakenAtLocal);
}
