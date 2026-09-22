using System.Text;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Collections;

public readonly record struct PhotoListCollectionId
{
    private PhotoListCollectionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Photo-list collection identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static PhotoListCollectionId New() => new(Guid.NewGuid());

    public static PhotoListCollectionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}

public sealed record PhotoListCollectionName
{
    public const int MaximumLength = 120;

    private PhotoListCollectionName(string displayValue, string normalizedValue)
    {
        DisplayValue = displayValue;
        NormalizedValue = normalizedValue;
    }

    public string DisplayValue { get; }

    public string NormalizedValue { get; }

    public static PhotoListCollectionName Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string compatibilityNormalized = value.Normalize(NormalizationForm.FormKC);
        StringBuilder display = new(compatibilityNormalized.Length);
        bool pendingSpace = false;

        foreach (char character in compatibilityNormalized.Trim())
        {
            if (char.IsControl(character) && !char.IsWhiteSpace(character))
            {
                throw new ArgumentException(
                    "Photo-list collection names cannot contain control characters.",
                    nameof(value));
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
            throw new ArgumentException("Photo-list collection names cannot be empty.", nameof(value));
        }

        if (displayValue.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"Photo-list collection names cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        return new PhotoListCollectionName(displayValue, displayValue.ToLowerInvariant());
    }

    public override string ToString() => DisplayValue;
}

public sealed record PhotoListCollectionDefinition(
    PhotoListCollectionId Id,
    string Name,
    IReadOnlyList<AssetRevisionId> RevisionIds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public const int MaximumItems = 5_000;

    public static AssetRevisionId[] ValidateRevisionIds(
        IEnumerable<AssetRevisionId>? revisionIds)
    {
        AssetRevisionId[] items = (revisionIds ?? []).ToArray();
        if (items.Length > MaximumItems)
        {
            throw new ArgumentException(
                $"Photo-list collections cannot contain more than {MaximumItems} revisions.",
                nameof(revisionIds));
        }

        HashSet<AssetRevisionId> seen = [];
        foreach (AssetRevisionId revisionId in items)
        {
            if (revisionId.IsEmpty)
            {
                throw new ArgumentException(
                    "Photo-list collections cannot contain an empty revision identifier.",
                    nameof(revisionIds));
            }

            if (!seen.Add(revisionId))
            {
                throw new ArgumentException(
                    $"Photo-list collections cannot contain duplicate revision '{revisionId}'.",
                    nameof(revisionIds));
            }
        }

        return items;
    }
}

public sealed record PhotoListCollectionSlideshowSnapshot(
    PhotoListCollectionId CollectionId,
    string CollectionName,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<AssetRevisionId> RevisionIds);
