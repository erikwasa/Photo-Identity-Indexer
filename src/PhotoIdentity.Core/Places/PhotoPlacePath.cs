using PhotoIdentity.Core.Tags;

namespace PhotoIdentity.Core.Places;

/// <summary>
/// Canonical first-class place identity. Places reuse the hierarchical photo-tag vocabulary for
/// stable Immich-compatible paths, but the reserved Places namespace is never an ordinary tag.
/// </summary>
public sealed record PhotoPlacePath
{
    public const string RootDisplayName = "Places";
    public const string RootNormalizedName = "places";
    public const int MaximumCanonicalValueLength = PhotoTagPath.MaximumReservedHierarchyValueLength;

    private PhotoPlacePath(PhotoTagPath canonicalTagPath)
    {
        CanonicalTagPath = canonicalTagPath;
        DisplayValue = string.Join(
            PhotoTagPath.Separator,
            canonicalTagPath.Segments.Skip(1).Select(segment => segment.DisplayName));
        NormalizedValue = string.Join(
            PhotoTagPath.Separator,
            canonicalTagPath.Segments.Skip(1).Select(segment => segment.NormalizedName));
        Name = canonicalTagPath.Name.DisplayName;
        ParentDisplayValue = canonicalTagPath.Segments.Count <= 2
            ? null
            : string.Join(
                PhotoTagPath.Separator,
                canonicalTagPath.Segments.Skip(1).SkipLast(1).Select(segment => segment.DisplayName));
    }

    public PhotoTagPath CanonicalTagPath { get; }

    public string CanonicalDisplayValue => CanonicalTagPath.DisplayValue;

    public string CanonicalNormalizedValue => CanonicalTagPath.NormalizedValue;

    /// <summary>
    /// Normal UI value with the reserved Places/ prefix removed.
    /// </summary>
    public string DisplayValue { get; }

    public string NormalizedValue { get; }

    public string Name { get; }

    public string? ParentDisplayValue { get; }

    public static bool IsReservedTagPath(PhotoTagPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return string.Equals(
            path.Segments[0].NormalizedName,
            RootNormalizedName,
            StringComparison.Ordinal);
    }

    public static bool IsReservedNormalizedTagValue(string normalizedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedValue);
        return string.Equals(normalizedValue, RootNormalizedName, StringComparison.Ordinal) ||
            normalizedValue.StartsWith($"{RootNormalizedName}{PhotoTagPath.Separator}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Accepts either a normal UI path (Sweden/Stockholm) or a canonical stored path
    /// (Places/Sweden/Stockholm). The reserved root alone is vocabulary, not an assignable place.
    /// Normal UI paths are canonicalized before the wider Places hierarchy limit is evaluated.
    /// </summary>
    public static PhotoPlacePath Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string trimmed = value.Trim();
        string firstSegment = trimmed.Split(PhotoTagPath.Separator, 2, StringSplitOptions.None)[0];
        bool alreadyCanonical = string.Equals(
            PhotoTagName.Parse(firstSegment).NormalizedName,
            RootNormalizedName,
            StringComparison.Ordinal);
        string canonicalValue = alreadyCanonical
            ? trimmed
            : $"{RootDisplayName}{PhotoTagPath.Separator}{trimmed}";
        return FromCanonicalTagPath(PhotoTagPath.Parse(canonicalValue));
    }

    public static PhotoPlacePath FromCanonicalTagPath(PhotoTagPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!IsReservedTagPath(path))
        {
            throw new ArgumentException(
                $"Place paths must be inside the reserved '{RootDisplayName}/' hierarchy.",
                nameof(path));
        }

        if (path.Segments.Count < 2)
        {
            throw new ArgumentException(
                $"The reserved '{RootDisplayName}' root is vocabulary and cannot be assigned as a place.",
                nameof(path));
        }

        return new PhotoPlacePath(path);
    }

    public override string ToString() => DisplayValue;
}
