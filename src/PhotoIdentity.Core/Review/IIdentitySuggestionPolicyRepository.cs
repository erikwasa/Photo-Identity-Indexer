using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Core.Review;

public static class ReviewIdentitySuggestionConfidenceGroups
{
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
}

public sealed record ReviewIdentitySuggestionPolicy(
    int Version,
    bool AutoAssignEnabled,
    double HighScoreThreshold,
    double HighMarginThreshold,
    double MediumScoreThreshold,
    string UpdatedBy,
    DateTimeOffset UpdatedAtUtc)
{
    public const double DefaultHighScoreThreshold = 0.70;
    public const double DefaultHighMarginThreshold = 0.10;
    public const double DefaultMediumScoreThreshold = 0.50;

    public void Validate()
    {
        if (Version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Version), "Policy version must be positive.");
        }

        ValidateScore(HighScoreThreshold, nameof(HighScoreThreshold));
        ValidateScore(MediumScoreThreshold, nameof(MediumScoreThreshold));
        if (MediumScoreThreshold > HighScoreThreshold)
        {
            throw new ArgumentException(
                "The Medium score threshold cannot be greater than the High score threshold.");
        }

        if (!double.IsFinite(HighMarginThreshold)
            || HighMarginThreshold < 0
            || HighMarginThreshold > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HighMarginThreshold),
                "The High rank-1/rank-2 margin threshold must be between 0 and 2.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(UpdatedBy);
    }

    public string Classify(double score, double? scoreMargin)
    {
        if (!double.IsFinite(score))
        {
            throw new ArgumentOutOfRangeException(nameof(score), "Suggestion score must be finite.");
        }

        if (scoreMargin is double margin && (!double.IsFinite(margin) || margin < 0 || margin > 2))
        {
            throw new ArgumentOutOfRangeException(nameof(scoreMargin), "Suggestion score margin must be between 0 and 2.");
        }

        if (score >= HighScoreThreshold
            && scoreMargin is double highMargin
            && highMargin >= HighMarginThreshold)
        {
            return ReviewIdentitySuggestionConfidenceGroups.High;
        }

        return score >= MediumScoreThreshold
            ? ReviewIdentitySuggestionConfidenceGroups.Medium
            : ReviewIdentitySuggestionConfidenceGroups.Low;
    }

    private static void ValidateScore(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Score threshold must be between 0 and 1.");
        }
    }
}

/// <summary>
/// Stores one versioned suggestion-confidence policy per exact embedding-model revision.
/// </summary>
public interface IIdentitySuggestionPolicyRepository
{
    Task<ReviewIdentitySuggestionPolicy> GetAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default);

    Task<ReviewIdentitySuggestionPolicy> UpdateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        bool autoAssignEnabled,
        double highScoreThreshold,
        double highMarginThreshold,
        double mediumScoreThreshold,
        string actor,
        CancellationToken cancellationToken = default);
}
