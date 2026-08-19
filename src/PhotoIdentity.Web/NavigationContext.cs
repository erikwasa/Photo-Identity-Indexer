using System.Text.Json;

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
    string? Place = null);

public static class SmartCollectionNavigation
{
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

    public static string BuildPhotoUrl(string revisionId, string returnUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(returnUrl);
        return $"/photo/{Uri.EscapeDataString(revisionId)}?returnUrl={Uri.EscapeDataString(returnUrl)}";
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
