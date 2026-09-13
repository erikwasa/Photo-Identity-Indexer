using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public static class SimilarFaceBulkSelection
{
    public const string AssignAction = "assign";
    public const string UnknownAction = "unknown";
    public const string RejectAction = "reject";

    public static bool IncludesSource(string action, ReviewFaceResponse? sourceFace) =>
        string.Equals(action, AssignAction, StringComparison.Ordinal) &&
        sourceFace is { State: "unreviewed" };

    public static string[] BuildFaceIds(
        IEnumerable<string> selectedFaceIds,
        string action,
        ReviewFaceResponse? sourceFace)
    {
        ArgumentNullException.ThrowIfNull(selectedFaceIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        HashSet<string> ids = selectedFaceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (IncludesSource(action, sourceFace) && sourceFace is not null)
        {
            ids.Add(sourceFace.Id);
        }

        return ids
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
