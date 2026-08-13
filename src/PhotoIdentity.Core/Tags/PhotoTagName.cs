using System.Text;

namespace PhotoIdentity.Core.Tags;

/// <summary>
/// Canonical photo-tag path-segment identity. Display spelling is preserved separately from
/// the case-insensitive normalized identity used for persistence and matching.
/// </summary>
public readonly record struct PhotoTagName
{
    public const int MaximumLength = 80;

    private PhotoTagName(string normalizedName, string displayName)
    {
        NormalizedName = normalizedName;
        DisplayName = displayName;
    }

    public string NormalizedName { get; }

    public string DisplayName { get; }

    public static PhotoTagName Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string compatibilityNormalized = value.Normalize(NormalizationForm.FormKC);
        StringBuilder display = new(compatibilityNormalized.Length);
        bool pendingSpace = false;

        foreach (char character in compatibilityNormalized.Trim())
        {
            if (character == PhotoTagPath.Separator)
            {
                throw new ArgumentException(
                    $"Photo tag names cannot contain '{PhotoTagPath.Separator}'; use it only as the hierarchy separator.",
                    nameof(value));
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = display.Length > 0;
                continue;
            }

            if (char.IsControl(character))
            {
                throw new ArgumentException("Photo tags cannot contain control characters.", nameof(value));
            }

            if (pendingSpace)
            {
                display.Append(' ');
                pendingSpace = false;
            }

            display.Append(character);
        }

        string displayName = display.ToString();
        if (displayName.Length == 0)
        {
            throw new ArgumentException("Photo tags cannot be empty.", nameof(value));
        }

        if (displayName.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"Photo tag names cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        return new PhotoTagName(displayName.ToLowerInvariant(), displayName);
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Canonical hierarchical tag value using Immich-compatible slash-separated path semantics.
/// </summary>
public sealed record PhotoTagPath
{
    public const char Separator = '/';
    public const int MaximumDepth = 32;
    public const int MaximumValueLength = 512;

    private PhotoTagPath(PhotoTagName[] segments)
    {
        Segments = segments;
        DisplayValue = string.Join(Separator, segments.Select(segment => segment.DisplayName));
        NormalizedValue = string.Join(Separator, segments.Select(segment => segment.NormalizedName));
        Name = segments[^1];
        ParentDisplayValue = segments.Length == 1
            ? null
            : string.Join(Separator, segments[..^1].Select(segment => segment.DisplayName));
        ParentNormalizedValue = segments.Length == 1
            ? null
            : string.Join(Separator, segments[..^1].Select(segment => segment.NormalizedName));
    }

    public IReadOnlyList<PhotoTagName> Segments { get; }

    public PhotoTagName Name { get; }

    public string DisplayValue { get; }

    public string NormalizedValue { get; }

    public string? ParentDisplayValue { get; }

    public string? ParentNormalizedValue { get; }

    public static PhotoTagPath Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string[] rawSegments = value.Split(Separator, StringSplitOptions.None);
        if (rawSegments.Length > MaximumDepth)
        {
            throw new ArgumentException(
                $"Photo tag paths cannot exceed {MaximumDepth} levels.",
                nameof(value));
        }

        PhotoTagName[] segments = new PhotoTagName[rawSegments.Length];
        for (int index = 0; index < rawSegments.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(rawSegments[index]))
            {
                throw new ArgumentException(
                    "Photo tag paths cannot contain empty hierarchy segments.",
                    nameof(value));
            }

            segments[index] = PhotoTagName.Parse(rawSegments[index]);
        }

        PhotoTagPath path = new(segments);
        if (path.DisplayValue.Length > MaximumValueLength)
        {
            throw new ArgumentException(
                $"Photo tag paths cannot exceed {MaximumValueLength} characters.",
                nameof(value));
        }

        return path;
    }

    public override string ToString() => DisplayValue;
}
