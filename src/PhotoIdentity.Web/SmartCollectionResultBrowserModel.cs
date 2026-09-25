using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public static class SmartCollectionResultBrowserModel
{
    public static SmartCollectionPageResponse Append(
        SmartCollectionPageResponse current,
        SmartCollectionPageResponse next)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);

        int currentEnd = checked(current.Offset + current.Items.Length);
        if (next.Offset > currentEnd)
        {
            throw new InvalidOperationException(
                $"Smart Collection append would skip results {currentEnd}–{next.Offset - 1}.");
        }

        int overlap = Math.Max(0, currentEnd - next.Offset);
        int comparable = Math.Min(overlap, next.Items.Length);
        for (int index = 0; index < comparable; index++)
        {
            int currentIndex = next.Offset - current.Offset + index;
            if (currentIndex < 0 || currentIndex >= current.Items.Length)
            {
                continue;
            }

            if (!string.Equals(
                    current.Items[currentIndex].RevisionId,
                    next.Items[index].RevisionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Smart Collection ordering changed while additional results were loading.");
            }
        }

        List<SmartCollectionPhotoResponse> merged = current.Items.ToList();
        HashSet<string> existingIds = merged
            .Select(photo => photo.RevisionId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (SmartCollectionPhotoResponse photo in next.Items.Skip(comparable))
        {
            if (!existingIds.Add(photo.RevisionId))
            {
                throw new InvalidOperationException(
                    $"Smart Collection result '{photo.RevisionId}' appeared twice outside a retry overlap.");
            }

            merged.Add(photo);
        }

        return new SmartCollectionPageResponse(
            merged.ToArray(),
            current.Offset,
            current.Limit,
            next.Total,
            next.Filter,
            next.CollectionId ?? current.CollectionId,
            next.CollectionName ?? current.CollectionName);
    }

    public static SmartCollectionQueryRequest BuildTransientRequest(
        SmartCollectionFilterResponse filter,
        int offset,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        return new SmartCollectionQueryRequest(
            filter.People,
            filter.PeopleMatch,
            filter.Tags,
            filter.TagMatch,
            filter.Location,
            Taken: null,
            Offset: offset,
            Limit: limit,
            TakenRange: filter.Taken is null
                ? null
                : new SmartCollectionDateRangeRequest(filter.Taken.From, filter.Taken.To),
            Age: filter.Age,
            Relationship: filter.Relationship);
    }

    public static string? ParseFocusRevisionId(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
        {
            return null;
        }

        string query = parsed.Query;
        if (query.Length <= 1)
        {
            return null;
        }

        foreach (string pair in query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string rawKey = equals < 0 ? pair : pair[..equals];
            if (!string.Equals(
                    Uri.UnescapeDataString(rawKey.Replace("+", " ", StringComparison.Ordinal)),
                    "focus",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rawValue = equals < 0 ? string.Empty : pair[(equals + 1)..];
            string value = Uri.UnescapeDataString(rawValue.Replace("+", " ", StringComparison.Ordinal));
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}
