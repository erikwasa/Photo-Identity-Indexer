namespace PhotoIdentity.Core.Collections;

public static class CreativeCollectionOrderingPolicies
{
    public const string ChronologicalV1 = "m26-creative-order-chronological-v1";
}

public sealed record CreativeCollectionRecipe(
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
