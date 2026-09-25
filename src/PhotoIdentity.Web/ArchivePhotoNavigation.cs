using System.Globalization;

namespace PhotoIdentity.Web;

public sealed record ArchivePhotoWorkspaceContext(
    string Folder,
    string Availability,
    string Verification,
    string Analysis,
    int Offset);

public sealed record ArchivePhotoNeighbor(
    int Index,
    string RevisionId);

public sealed record ArchivePhotoNavigationResolution(
    int Index,
    int Total,
    ArchivePhotoNeighbor? Previous,
    ArchivePhotoNeighbor? Next);

public static class ArchivePhotoNavigation
{
    public const int ResultPageSize = 50;

    public static bool TryParseWorkspaceContext(
        string? normalizedReturnUrl,
        out ArchivePhotoWorkspaceContext? context)
    {
        context = null;
        if (!PhotoReturnContext.IsArchiveReturn(normalizedReturnUrl))
        {
            return false;
        }

        Dictionary<string, string> query = ParseQuery(normalizedReturnUrl!);
        query.TryGetValue("folder", out string? folder);
        if (!query.TryGetValue("availability", out string? availability) ||
            string.IsNullOrWhiteSpace(availability) ||
            !query.TryGetValue("verification", out string? verification) ||
            string.IsNullOrWhiteSpace(verification) ||
            !query.TryGetValue("analysis", out string? analysis) ||
            string.IsNullOrWhiteSpace(analysis) ||
            !query.TryGetValue("offset", out string? offsetText) ||
            !int.TryParse(offsetText, NumberStyles.None, CultureInfo.InvariantCulture, out int offset) ||
            offset < 0)
        {
            return false;
        }

        context = new ArchivePhotoWorkspaceContext(
            folder?.Trim() ?? string.Empty,
            availability.Trim(),
            verification.Trim(),
            analysis.Trim(),
            offset);
        return true;
    }

    public static string BuildItemsUrl(
        ArchivePhotoWorkspaceContext context,
        int offset,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 200);

        return "api/archive/items/filter?" +
               $"availability={Uri.EscapeDataString(context.Availability)}" +
               $"&verification={Uri.EscapeDataString(context.Verification)}" +
               $"&analysis={Uri.EscapeDataString(context.Analysis)}" +
               $"&folder={Uri.EscapeDataString(context.Folder)}" +
               $"&offset={offset.ToString(CultureInfo.InvariantCulture)}" +
               $"&limit={limit.ToString(CultureInfo.InvariantCulture)}";
    }

    public static int PageOffsetForIndex(int index, int pageSize = ResultPageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        return index / pageSize * pageSize;
    }

    public static bool TryFindCurrent(
        ArchiveItemPageResponse page,
        string revisionId,
        out int localIndex,
        out int globalIndex)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);

        for (int index = 0; index < page.Items.Count; index++)
        {
            if (string.Equals(page.Items[index].RevisionId, revisionId, StringComparison.Ordinal))
            {
                localIndex = index;
                globalIndex = page.Offset + index;
                return true;
            }
        }

        localIndex = -1;
        globalIndex = -1;
        return false;
    }

    public static ArchivePhotoNeighbor? FindPreviousNeighbor(
        ArchiveItemPageResponse page,
        int localIndex)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (localIndex < 0 || localIndex >= page.Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(localIndex));
        }

        for (int index = localIndex - 1; index >= 0; index--)
        {
            string? revisionId = page.Items[index].RevisionId;
            if (!string.IsNullOrWhiteSpace(revisionId))
            {
                return new ArchivePhotoNeighbor(page.Offset + index, revisionId);
            }
        }

        return null;
    }

    public static ArchivePhotoNeighbor? FindNextNeighbor(
        ArchiveItemPageResponse page,
        int localIndex)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (localIndex < 0 || localIndex >= page.Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(localIndex));
        }

        for (int index = localIndex + 1; index < page.Items.Count; index++)
        {
            string? revisionId = page.Items[index].RevisionId;
            if (!string.IsNullOrWhiteSpace(revisionId))
            {
                return new ArchivePhotoNeighbor(page.Offset + index, revisionId);
            }
        }

        return null;
    }

    private static Dictionary<string, string> ParseQuery(string url)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        int question = url.IndexOf('?');
        if (question < 0 || question == url.Length - 1)
        {
            return values;
        }

        int fragment = url.IndexOf('#', question + 1);
        string query = fragment < 0
            ? url[(question + 1)..]
            : url[(question + 1)..fragment];
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string rawKey = equals < 0 ? pair : pair[..equals];
            string rawValue = equals < 0 ? string.Empty : pair[(equals + 1)..];
            string key = Uri.UnescapeDataString(rawKey.Replace("+", " ", StringComparison.Ordinal));
            string value = Uri.UnescapeDataString(rawValue.Replace("+", " ", StringComparison.Ordinal));
            values[key] = value;
        }

        return values;
    }
}
