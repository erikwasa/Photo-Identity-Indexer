namespace PhotoIdentity.Web;

public sealed record SlideshowPresentationEvidence(
    bool ReliableFaceGeometry,
    int FaceCount)
{
    public static SlideshowPresentationEvidence Unavailable { get; } = new(false, 0);
}

public sealed record SlideshowTimingDecision(
    TimeSpan EffectiveDuration,
    double Multiplier,
    string Reason)
{
    public static SlideshowTimingDecision Fallback(int configuredSeconds) =>
        new(TimeSpan.FromSeconds(configuredSeconds), 1d, "configured");
}

public static class SlideshowTimingPolicy
{
    private const double SingleFaceMultiplier = 0.98d;
    private const double TwoFaceMultiplier = 1.04d;
    private const double GroupMultiplier = 1.08d;

    public static SlideshowTimingDecision Create(
        int configuredDurationSeconds,
        SlideshowPresentationEvidence? evidence)
    {
        int configuredSeconds = Math.Max(1, configuredDurationSeconds);
        if (evidence is null || !evidence.ReliableFaceGeometry || evidence.FaceCount < 0)
        {
            return SlideshowTimingDecision.Fallback(configuredSeconds);
        }

        return evidence.FaceCount switch
        {
            0 => SlideshowTimingDecision.Fallback(configuredSeconds),
            1 => Create(configuredSeconds, SingleFaceMultiplier, "single-face"),
            2 => Create(configuredSeconds, TwoFaceMultiplier, "two-faces"),
            _ => Create(configuredSeconds, GroupMultiplier, "group"),
        };
    }

    private static SlideshowTimingDecision Create(
        int configuredSeconds,
        double multiplier,
        string reason) =>
        new(
            TimeSpan.FromSeconds(configuredSeconds * multiplier),
            multiplier,
            reason);
}


public enum SlideshowMomentTransitionKind
{
    Standard,
    ChapterBoundary,
}

public sealed record SlideshowMomentTransitionDecision(
    SlideshowMomentTransitionKind Kind,
    int TransitionMilliseconds)
{
    public const int StandardMilliseconds = 600;
    public const int ChapterBoundaryMilliseconds = 850;
}

public static class SlideshowMomentTransitionPolicy
{
    public static SlideshowMomentTransitionDecision Create(
        string? outgoingMomentId,
        string? incomingMomentId)
    {
        bool chapterBoundary =
            !string.IsNullOrWhiteSpace(outgoingMomentId) &&
            !string.IsNullOrWhiteSpace(incomingMomentId) &&
            !string.Equals(outgoingMomentId, incomingMomentId, StringComparison.Ordinal);

        return chapterBoundary
            ? new(
                SlideshowMomentTransitionKind.ChapterBoundary,
                SlideshowMomentTransitionDecision.ChapterBoundaryMilliseconds)
            : new(
                SlideshowMomentTransitionKind.Standard,
                SlideshowMomentTransitionDecision.StandardMilliseconds);
    }
}
