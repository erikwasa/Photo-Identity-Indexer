namespace PhotoIdentity.Web;

public sealed record SmartCollectionTransientNavigationState(
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
    string East);

public static class SmartCollectionNavigation
{
    private const string WorkspaceRoot = "/smart-collections";
    private const string PreviewStoragePrefix = "photo-identity.smart-collections.preview.";

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
            !value.StartsWith('/', StringComparison.Ordinal) ||
            value.StartsWith("//", StringComparison.Ordinal) ||
            value.Contains('\\') ||
            value.Any(char.IsControl))
        {
            return null;
        }

        return value;
    }

    public static bool IsSmartCollectionsReturn(string? normalizedReturnUrl)
    {
        if (normalizedReturnUrl is null ||
            !normalizedReturnUrl.StartsWith("/smart-collections", StringComparison.Ordinal))
        {
            return false;
        }

        return normalizedReturnUrl.Length == "/smart-collections".Length ||
               normalizedReturnUrl["/smart-collections".Length] is '?' or '#';
    }
}
