using System.Text;

namespace PhotoIdentity.Core.Tags;

/// <summary>
/// Canonical flat photo-tag identity. Display spelling is preserved separately from
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
            if (char.IsControl(character))
            {
                throw new ArgumentException("Photo tags cannot contain control characters.", nameof(value));
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = display.Length > 0;
                continue;
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
                $"Photo tags cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        return new PhotoTagName(displayName.ToLowerInvariant(), displayName);
    }

    public override string ToString() => DisplayName;
}
