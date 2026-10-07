using System.Text;

namespace PhotoIdentity.Core.Collections;

public readonly record struct CreativeCollectionId
{
    private CreativeCollectionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Creative collection identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static CreativeCollectionId New() => new(Guid.NewGuid());

    public static CreativeCollectionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString("D");
}

public sealed record CreativeCollectionName
{
    public const int MaximumLength = 120;

    private CreativeCollectionName(string displayValue)
    {
        DisplayValue = displayValue;
    }

    public string DisplayValue { get; }

    public static CreativeCollectionName Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string compatibilityNormalized = value.Normalize(NormalizationForm.FormKC);
        StringBuilder display = new(compatibilityNormalized.Length);
        bool pendingSpace = false;

        foreach (char character in compatibilityNormalized.Trim())
        {
            if (char.IsControl(character) && !char.IsWhiteSpace(character))
            {
                throw new ArgumentException("Creative collection names cannot contain control characters.", nameof(value));
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
            throw new ArgumentException("Creative collection names cannot be empty.", nameof(value));
        }

        if (displayValue.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"Creative collection names cannot exceed {MaximumLength} characters.",
                nameof(value));
        }

        return new CreativeCollectionName(displayValue);
    }

    public override string ToString() => DisplayValue;
}

public static class CreativeCollectionOrderingPolicies
{
    public const string ChronologicalV1 = "m26-creative-order-chronological-v1";
}

public static class CreativeCollectionAnchorKinds
{
    public const string SmartCollection = "smart-collection";
    public const string Search = "search";
}

public static class CreativeCollectionAnchorPolicies
{
    public const string SmartCollectionV1 = "m26-smart-collection-anchor-v1";
    public const string SearchRankedTopNV1 = "m36-search-ranked-top-n-v1";
}

public sealed record CreativeCollectionSearchAnchor(
    string Query,
    string Mode,
    SmartCollectionId? ScopeCollectionId,
    int Limit,
    string PolicyVersion)
{
    public const int DefaultLimit = 80;
    public const int MaximumLimit = 250;

    public static CreativeCollectionSearchAnchor Create(
        string query,
        string? mode = null,
        SmartCollectionId? scopeCollectionId = null,
        int limit = DefaultLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        string normalizedQuery = query.Trim();
        if (normalizedQuery.Length > 300)
        {
            throw new ArgumentException(
                "Creative Collection search query cannot exceed 300 characters.",
                nameof(query));
        }

        if (limit is < 1 or > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"Creative Collection search anchor limit must be between 1 and {MaximumLimit}.");
        }

        return new CreativeCollectionSearchAnchor(
            normalizedQuery,
            PhotoSearchModes.Normalize(mode),
            scopeCollectionId,
            limit,
            CreativeCollectionAnchorPolicies.SearchRankedTopNV1);
    }

    public void ValidateSupported()
    {
        CreativeCollectionSearchAnchor normalized = Create(
            Query,
            Mode,
            ScopeCollectionId,
            Limit);
        if (!string.Equals(
                PolicyVersion,
                CreativeCollectionAnchorPolicies.SearchRankedTopNV1,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Creative Collection search anchor policy '{PolicyVersion}' is not supported.");
        }

        if (!string.Equals(Query, normalized.Query, StringComparison.Ordinal) ||
            !string.Equals(Mode, normalized.Mode, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Creative Collection search anchor query or mode is not normalized.");
        }
    }
}

public sealed record CreativeCollectionRecipe(
    CreativeCollectionId Id,
    string Name,
    SmartCollectionId? AnchorCollectionId,
    int TargetCount,
    int MomentGapMinutes,
    string MomentPolicyVersion,
    string ContextPolicyVersion,
    string SelectionPolicyVersion,
    string OrderingPolicyVersion,
    bool NoveltyEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    CreativeCollectionSearchAnchor? SearchAnchor = null)
{
    public string AnchorKind =>
        SearchAnchor is null
            ? CreativeCollectionAnchorKinds.SmartCollection
            : CreativeCollectionAnchorKinds.Search;

    public void ValidateAnchorSupported()
    {
        if ((AnchorCollectionId is null) == (SearchAnchor is null))
        {
            throw new InvalidDataException(
                "Creative Collection recipes must have exactly one anchor source.");
        }

        SearchAnchor?.ValidateSupported();
    }

    public const int DefaultTargetCount = 50;
    public const int DefaultMomentGapMinutes = 30;

    public static CreativeCollectionRecipeSettings DefaultSettings => new(
        DefaultTargetCount,
        DefaultMomentGapMinutes,
        PhotoMomentGapPolicy.CreateTimeGapEvaluation(DefaultMomentGapMinutes).Version,
        CreativeCollectionContextPolicy.BalancedV1.Version,
        CreativeCollectionSelectionPolicy.BalancedV1.Version,
        CreativeCollectionOrderingPolicies.ChronologicalV1,
        NoveltyEnabled: false);
}

public sealed record CreativeCollectionRecipeSettings(
    int TargetCount,
    int MomentGapMinutes,
    string MomentPolicyVersion,
    string ContextPolicyVersion,
    string SelectionPolicyVersion,
    string OrderingPolicyVersion,
    bool NoveltyEnabled)
{
    public static CreativeCollectionRecipeSettings Create(
        int targetCount,
        string contextPolicyVersion,
        bool noveltyEnabled = false) =>
        CreateForPreview(
            targetCount,
            CreativeCollectionRecipe.DefaultMomentGapMinutes,
            contextPolicyVersion,
            noveltyEnabled);

    public static CreativeCollectionRecipeSettings CreateForPreview(
        int targetCount,
        int momentGapMinutes,
        string contextPolicyVersion,
        bool noveltyEnabled = false)
    {
        CreativeCollectionSelectionPolicy.ValidateTargetCount(targetCount);
        CreativeCollectionContextPolicy contextPolicy =
            CreativeCollectionContextPolicy.FromVersion(contextPolicyVersion);
        PhotoMomentGapPolicy momentPolicy =
            PhotoMomentGapPolicy.CreateTimeGapEvaluation(momentGapMinutes);

        return new CreativeCollectionRecipeSettings(
            targetCount,
            momentGapMinutes,
            momentPolicy.Version,
            contextPolicy.Version,
            CreativeCollectionSelectionPolicy.BalancedV1.Version,
            CreativeCollectionOrderingPolicies.ChronologicalV1,
            noveltyEnabled);
    }

    public void ValidateSupported()
    {
        CreativeCollectionSelectionPolicy.ValidateTargetCount(TargetCount);

        PhotoMomentGapPolicy momentPolicy =
            PhotoMomentGapPolicy.CreateTimeGapEvaluation(MomentGapMinutes);
        if (!string.Equals(momentPolicy.Version, MomentPolicyVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Creative Collection moment policy '{MomentPolicyVersion}' does not match the stored gap.");
        }

        _ = CreativeCollectionContextPolicy.FromVersion(ContextPolicyVersion);

        if (!string.Equals(
                SelectionPolicyVersion,
                CreativeCollectionSelectionPolicy.BalancedV1.Version,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Creative Collection selection policy '{SelectionPolicyVersion}' is not supported.");
        }

        if (!string.Equals(
                OrderingPolicyVersion,
                CreativeCollectionOrderingPolicies.ChronologicalV1,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Creative Collection ordering policy '{OrderingPolicyVersion}' is not supported.");
        }
    }
}
