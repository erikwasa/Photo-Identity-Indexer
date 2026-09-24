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

public sealed record CreativeCollectionRecipe(
    CreativeCollectionId Id,
    string Name,
    SmartCollectionId AnchorCollectionId,
    int TargetCount,
    int MomentGapMinutes,
    string MomentPolicyVersion,
    string ContextPolicyVersion,
    string SelectionPolicyVersion,
    string OrderingPolicyVersion,
    bool NoveltyEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
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
