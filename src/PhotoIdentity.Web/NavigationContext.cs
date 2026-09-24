using System.Globalization;
using System.Text.Json;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public sealed record SmartCollectionTransientNavigationState(
    string? EditingId,
    string Name,
    string[] People,
    string PeopleMatch,
    string[] Tags,
    string TagMatch,
    string Taken,
    bool UseLocation,
    string South,
    string West,
    string North,
    string East,
    string? Place = null,
    string? TakenMode = null,
    string? TakenYear = null,
    string? TakenMonth = null,
    string? TakenDate = null,
    string? TakenFrom = null,
    string? TakenTo = null,
    string[]? Places = null,
    SmartCollectionAgeRequest? Age = null,
    SmartCollectionRelationshipRequest? Relationship = null);

public sealed record SmartCollectionWorkspaceContext(
    string Mode,
    string? CollectionId,
    string? PreviewKey,
    int Offset);

public sealed record SmartCollectionPhotoNavigationResolution(
    int Index,
    int Total,
    string? PreviousRevisionId,
    string? NextRevisionId);

public static class SmartCollectionNavigation
{
    public const int ResultPageSize = 40;

    private const string WorkspaceRoot = "/smart-collections";
    private const string PreviewStoragePrefix = "photo-identity.smart-collections.preview.";
    private static readonly JsonSerializerOptions NavigationJsonOptions = new(JsonSerializerDefaults.Web);

    public static string BuildSavedWorkspaceUrl(string collectionId, int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        return $"{WorkspaceRoot}?mode=saved&collection={Uri.EscapeDataString(collectionId)}&offset={Math.Max(0, offset)}";
    }

    public static string BuildTransientWorkspaceUrl(string previewKey, int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewKey);
        return $"{WorkspaceRoot}?mode=transient&preview={Uri.EscapeDataString(previewKey)}&offset={Math.Max(0, offset)}";
    }

    public static string BuildWorkspaceUrl(SmartCollectionWorkspaceContext context, int offset)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Mode switch
        {
            "saved" when !string.IsNullOrWhiteSpace(context.CollectionId) =>
                BuildSavedWorkspaceUrl(context.CollectionId, offset),
            "transient" when !string.IsNullOrWhiteSpace(context.PreviewKey) =>
                BuildTransientWorkspaceUrl(context.PreviewKey, offset),
            _ => throw new ArgumentException("Smart Collection workspace context is incomplete.", nameof(context)),
        };
    }

    public static string BuildPhotoUrl(string revisionId, string returnUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(returnUrl);
        return $"/photo/{Uri.EscapeDataString(revisionId)}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    public static int PageOffsetForIndex(int index, int pageSize = ResultPageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        return index / pageSize * pageSize;
    }

    public static bool TryParsePhotoUrl(
        string url,
        out string? revisionId,
        out string? returnUrl)
    {
        revisionId = null;
        returnUrl = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        Uri? parsed;
        if (!Uri.TryCreate(url, UriKind.Absolute, out parsed))
        {
            if (!Uri.TryCreate(new Uri("https://photoidentity.local"), url, out parsed))
            {
                return false;
            }
        }

        const string photoPrefix = "/photo/";
        if (!parsed.AbsolutePath.StartsWith(photoPrefix, StringComparison.Ordinal) ||
            parsed.AbsolutePath.Length <= photoPrefix.Length)
        {
            return false;
        }

        string rawRevisionId = parsed.AbsolutePath[photoPrefix.Length..];
        if (rawRevisionId.Contains('/'))
        {
            return false;
        }

        revisionId = Uri.UnescapeDataString(rawRevisionId);
        Dictionary<string, string> query = ParseQuery(parsed.PathAndQuery);
        query.TryGetValue("returnUrl", out returnUrl);
        return !string.IsNullOrWhiteSpace(revisionId) && !string.IsNullOrWhiteSpace(returnUrl);
    }

    public static bool TryParseWorkspaceContext(
        string? normalizedReturnUrl,
        out SmartCollectionWorkspaceContext? context)
    {
        context = null;
        if (!PhotoReturnContext.IsSmartCollectionsReturn(normalizedReturnUrl))
        {
            return false;
        }

        Dictionary<string, string> query = ParseQuery(normalizedReturnUrl!);
        query.TryGetValue("mode", out string? mode);
        query.TryGetValue("offset", out string? offsetText);
        if (!int.TryParse(offsetText, NumberStyles.None, CultureInfo.InvariantCulture, out int offset) || offset < 0)
        {
            return false;
        }

        if (string.Equals(mode, "saved", StringComparison.OrdinalIgnoreCase) &&
            query.TryGetValue("collection", out string? collectionId) &&
            Guid.TryParse(collectionId, out Guid parsedCollectionId) &&
            parsedCollectionId != Guid.Empty)
        {
            context = new SmartCollectionWorkspaceContext(
                "saved",
                collectionId,
                null,
                offset);
            return true;
        }

        if (string.Equals(mode, "transient", StringComparison.OrdinalIgnoreCase) &&
            query.TryGetValue("preview", out string? previewKey) &&
            previewKey.Length == 32 &&
            Guid.TryParseExact(previewKey, "N", out _))
        {
            context = new SmartCollectionWorkspaceContext(
                "transient",
                null,
                previewKey,
                offset);
            return true;
        }

        return false;
    }

    public static bool TryBuildTransientQuery(
        SmartCollectionTransientNavigationState state,
        int offset,
        int limit,
        out SmartCollectionQueryRequest? request)
    {
        ArgumentNullException.ThrowIfNull(state);
        request = null;
        if (offset < 0 || limit is < 1 or > 200)
        {
            return false;
        }

        string[] places = state.Places ??
            (string.IsNullOrWhiteSpace(state.Place) ? [] : [state.Place!]);
        SmartCollectionLocationRequest? location;
        if (state.UseLocation)
        {
            if (!TryCoordinate(state.South, -90, 90, out double south) ||
                !TryCoordinate(state.West, -180, 180, out double west) ||
                !TryCoordinate(state.North, -90, 90, out double north) ||
                !TryCoordinate(state.East, -180, 180, out double east) ||
                south > north || west > east)
            {
                return false;
            }

            location = new SmartCollectionLocationRequest(
                south,
                west,
                north,
                east,
                Place: null,
                Places: places);
        }
        else
        {
            location = places.Length == 0
                ? null
                : new SmartCollectionLocationRequest(Places: places);
        }

        SmartCollectionDateEditorState takenState = string.IsNullOrWhiteSpace(state.TakenMode)
            ? SmartCollectionDateEditorModel.FromLegacyExpression(state.Taken)
            : new SmartCollectionDateEditorState(
                state.TakenMode!,
                state.TakenYear ?? string.Empty,
                state.TakenMonth ?? string.Empty,
                state.TakenDate ?? string.Empty,
                state.TakenFrom ?? string.Empty,
                state.TakenTo ?? string.Empty);
        if (!SmartCollectionDateEditorModel.TryBuildRange(
                takenState.Mode,
                takenState.Year,
                takenState.Month,
                takenState.Date,
                takenState.From,
                takenState.To,
                out SmartCollectionDateRangeRequest? takenRange,
                out _))
        {
            return false;
        }

        request = new SmartCollectionQueryRequest(
            state.People,
            state.PeopleMatch,
            state.Tags,
            state.TagMatch,
            location,
            Taken: null,
            Offset: offset,
            Limit: limit,
            TakenRange: takenRange,
            Age: state.Age,
            Relationship: state.Relationship);
        return true;
    }

    public static bool TryResolvePhotoNavigation(
        SmartCollectionPageResponse page,
        string revisionId,
        int expectedIndex,
        out SmartCollectionPhotoNavigationResolution? navigation)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        navigation = null;
        if (expectedIndex < 0 || expectedIndex >= page.Total)
        {
            return false;
        }

        int localIndex = expectedIndex - page.Offset;
        if (localIndex < 0 || localIndex >= page.Items.Length ||
            !string.Equals(page.Items[localIndex].RevisionId, revisionId, StringComparison.Ordinal))
        {
            return false;
        }

        string? previous = null;
        if (expectedIndex > 0)
        {
            if (localIndex == 0)
            {
                return false;
            }

            previous = page.Items[localIndex - 1].RevisionId;
        }

        string? next = null;
        if (expectedIndex + 1 < page.Total)
        {
            if (localIndex + 1 >= page.Items.Length)
            {
                return false;
            }

            next = page.Items[localIndex + 1].RevisionId;
        }

        navigation = new SmartCollectionPhotoNavigationResolution(
            expectedIndex,
            page.Total,
            previous,
            next);
        return true;
    }

    public static string PreviewStorageKey(string previewKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previewKey);
        return $"{PreviewStoragePrefix}{previewKey}";
    }

    public static string SerializeTransientState(SmartCollectionTransientNavigationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, NavigationJsonOptions);
    }

    public static SmartCollectionTransientNavigationState? DeserializeTransientState(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<SmartCollectionTransientNavigationState>(json, NavigationJsonOptions);
    }

    private static bool TryCoordinate(string value, double minimum, double maximum, out double parsed) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
        double.IsFinite(parsed) &&
        parsed >= minimum &&
        parsed <= maximum;

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

public static class ArchiveNavigation
{
    private const string WorkspaceRoot = "/archive";

    public static string BuildWorkspaceUrl(
        string? folder,
        string availability,
        string verification,
        string analysis,
        int offset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(availability);
        ArgumentException.ThrowIfNullOrWhiteSpace(verification);
        ArgumentException.ThrowIfNullOrWhiteSpace(analysis);

        return $"{WorkspaceRoot}?folder={Uri.EscapeDataString(folder?.Trim() ?? string.Empty)}" +
               $"&availability={Uri.EscapeDataString(availability.Trim())}" +
               $"&verification={Uri.EscapeDataString(verification.Trim())}" +
               $"&analysis={Uri.EscapeDataString(analysis.Trim())}" +
               $"&offset={Math.Max(0, offset)}";
    }

    public static string BuildPhotoUrl(string revisionId, string returnUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(returnUrl);
        return $"/photo/{Uri.EscapeDataString(revisionId)}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }
}

public static class PhotoReturnContext
{
    public static string? NormalizeLocalReturnUrl(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        string value = candidate.Trim();
        if (value.Length > 2048 ||
            !value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.Contains('\\') ||
            value.Any(char.IsControl))
        {
            return null;
        }

        return value;
    }

    public static bool IsSmartCollectionsReturn(string? normalizedReturnUrl) =>
        HasRouteBoundary(normalizedReturnUrl, "/smart-collections");

    public static bool IsArchiveReturn(string? normalizedReturnUrl) =>
        HasRouteBoundary(normalizedReturnUrl, "/archive");

    private static bool HasRouteBoundary(string? normalizedReturnUrl, string route)
    {
        if (normalizedReturnUrl is null ||
            !normalizedReturnUrl.StartsWith(route, StringComparison.Ordinal))
        {
            return false;
        }

        return normalizedReturnUrl.Length == route.Length ||
               normalizedReturnUrl[route.Length] is '?' or '#';
    }
}
