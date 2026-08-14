using System.Text;

namespace PhotoIdentity.Core.Collections;

public readonly record struct SmartCollectionId
{
    private SmartCollectionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Smart collection identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static SmartCollectionId New() => new(Guid.NewGuid());

    public static SmartCollectionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}

public sealed record SmartCollectionName
{
    public const int MaximumLength = 120;

    private SmartCollectionName(string displayValue, string normalizedValue)
    {
        DisplayValue = displayValue;
        NormalizedValue = normalizedValue;
    }

    public string DisplayValue { get; }

    public string NormalizedValue { get; }

    public static SmartCollectionName Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string compatibilityNormalized = value.Normalize(NormalizationForm.FormKC);
        StringBuilder display = new(compatibilityNormalized.Length);
        bool pendingSpace = false;

        foreach (char character in compatibilityNormalized.Trim())
        {
            if (char.IsControl(character) && !char.IsWhiteSpace(character))
            {
                throw new ArgumentException("Smart collection names cannot contain control characters.", nameof(value));
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

        string displayValue = display.ToString();
        if (displayValue.Length == 0)
        {
            throw new ArgumentException("Smart collection names cannot be empty.", nameof(value));
        }

        if (displayValue.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"Smart collection names cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        return new SmartCollectionName(displayValue, displayValue.ToLowerInvariant());
    }

    public override string ToString() => DisplayValue;
}

public sealed record SmartCollectionDefinition(
    SmartCollectionId Id,
    string Name,
    SmartCollectionFilter Filter,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
