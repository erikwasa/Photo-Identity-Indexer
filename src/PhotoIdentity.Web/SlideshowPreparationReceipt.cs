using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public sealed record SlideshowPreparationReceipt(string[] RevisionIds)
{
    public static SlideshowPreparationReceipt FromSnapshot(
        SmartCollectionSlideshowSnapshotResponse snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new SlideshowPreparationReceipt(
            Normalize(snapshot.Items.Select(item => item.RevisionId)));
    }

    public bool MatchesSnapshot(SmartCollectionSlideshowSnapshotResponse snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string[] current = Normalize(snapshot.Items.Select(item => item.RevisionId));
        string[] prepared = Normalize(RevisionIds ?? []);
        return prepared.SequenceEqual(current, StringComparer.OrdinalIgnoreCase);
    }

    public string[] GetRevisionIds() => Normalize(RevisionIds ?? []);

    private static string[] Normalize(IEnumerable<string> revisionIds) =>
        revisionIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed record SlideshowPreparationBookmark(
    string SessionId,
    string[] RevisionIds);
