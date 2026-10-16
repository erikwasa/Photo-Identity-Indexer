using System.Security.Cryptography;
using System.Text;
using PhotoIdentity.Core.Imaging;

namespace PhotoIdentity.PhotoQualityEvaluation;

internal sealed record PhotoQualityChallengeAnalyzedCandidate(
    PhotoQualityBenchmarkPhoto Photo,
    TechnicalPhotoQualityEvidence Evidence,
    IReadOnlyList<string> Reasons,
    string StableOrderKey);

internal static class PhotoQualityBenchmarkChallengeSelector
{
    public const string SelectionPolicyVersion =
        "m37-initial-challenge-extremes-rarity-v1";

    private static readonly string[] ReasonOrder =
    [
        "candidate-low-sharpness",
        "candidate-severe-underexposure",
        "candidate-severe-overexposure",
        "candidate-shadow-clipping",
        "candidate-highlight-clipping",
        "candidate-low-contrast",
        "candidate-low-effective-proxy-resolution",
        "extreme-sharpness-low",
        "extreme-sharpness-high",
        "extreme-luminance-low",
        "extreme-luminance-high",
        "extreme-shadow-clipping",
        "extreme-highlight-clipping",
        "extreme-contrast-low",
        "extreme-proxy-resolution-low",
        "rare-capture-era",
        "rare-media-family",
        "rare-resolution",
        "rare-device-family",
        "unknown-capture-era",
        "unknown-camera",
    ];

    public static IReadOnlyList<PhotoQualityChallengeAnalyzedCandidate> Select(
        IEnumerable<PhotoQualityChallengeAnalyzedCandidate> source,
        int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        PhotoQualityChallengeAnalyzedCandidate[] candidates = source.ToArray();
        if (candidates.Length == 0)
        {
            return [];
        }

        HashSet<string> selectedRevisionIds =
            new(StringComparer.OrdinalIgnoreCase);
        List<PhotoQualityChallengeAnalyzedCandidate> selected = [];
        Dictionary<string, Queue<PhotoQualityChallengeAnalyzedCandidate>> buckets =
            BuildBuckets(candidates);

        bool progress;
        do
        {
            progress = false;
            foreach (string reason in ReasonOrder)
            {
                if (!buckets.TryGetValue(
                        reason,
                        out Queue<PhotoQualityChallengeAnalyzedCandidate>? queue))
                {
                    continue;
                }

                while (queue.Count > 0 &&
                       selectedRevisionIds.Contains(queue.Peek().Photo.RevisionId))
                {
                    queue.Dequeue();
                }

                if (queue.Count == 0)
                {
                    continue;
                }

                PhotoQualityChallengeAnalyzedCandidate candidate = queue.Dequeue();
                selectedRevisionIds.Add(candidate.Photo.RevisionId);
                selected.Add(candidate);
                progress = true;
                if (selected.Count == count)
                {
                    return selected;
                }
            }
        }
        while (progress);

        return selected;
    }

    public static IReadOnlyList<string> BuildReasons(
        TechnicalPhotoQualityEvidence evidence,
        PhotoQualityBenchmarkStratum stratum,
        PhotoQualityChallengeThresholds thresholds,
        PhotoQualityChallengeRarity rarity)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(stratum);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(rarity);

        HashSet<string> reasons = new(StringComparer.Ordinal);
        foreach (string reason in evidence.Reasons)
        {
            if (!string.Equals(
                    reason,
                    "profile-not-calibrated",
                    StringComparison.Ordinal))
            {
                reasons.Add(reason);
            }
        }

        if (evidence.SharpnessLaplacianVariance <= thresholds.SharpnessLow)
        {
            reasons.Add("extreme-sharpness-low");
        }
        if (evidence.SharpnessLaplacianVariance >= thresholds.SharpnessHigh)
        {
            reasons.Add("extreme-sharpness-high");
        }
        if (evidence.MeanLuminance <= thresholds.LuminanceLow)
        {
            reasons.Add("extreme-luminance-low");
        }
        if (evidence.MeanLuminance >= thresholds.LuminanceHigh)
        {
            reasons.Add("extreme-luminance-high");
        }
        if (evidence.ShadowClippingFraction >= thresholds.ShadowClippingHigh)
        {
            reasons.Add("extreme-shadow-clipping");
        }
        if (evidence.HighlightClippingFraction >= thresholds.HighlightClippingHigh)
        {
            reasons.Add("extreme-highlight-clipping");
        }
        if (evidence.LuminanceContrastSpan <= thresholds.ContrastLow)
        {
            reasons.Add("extreme-contrast-low");
        }
        if (evidence.ProxyLongEdgeFraction <= thresholds.ProxyResolutionLow)
        {
            reasons.Add("extreme-proxy-resolution-low");
        }

        if (rarity.CaptureEras.Contains(stratum.CaptureEra))
        {
            reasons.Add("rare-capture-era");
        }
        if (rarity.MediaFamilies.Contains(stratum.MediaFamily))
        {
            reasons.Add("rare-media-family");
        }
        if (rarity.Resolutions.Contains(stratum.Resolution))
        {
            reasons.Add("rare-resolution");
        }
        if (rarity.DeviceFamilies.Contains(stratum.DeviceFamily))
        {
            reasons.Add("rare-device-family");
        }
        if (string.Equals(stratum.CaptureEra, "unknown", StringComparison.Ordinal))
        {
            reasons.Add("unknown-capture-era");
        }
        if (string.Equals(stratum.DeviceFamily, "unknown-camera", StringComparison.Ordinal))
        {
            reasons.Add("unknown-camera");
        }

        return ReasonOrder
            .Where(reasons.Contains)
            .ToArray();
    }

    public static PhotoQualityChallengeThresholds CalculateThresholds(
        IEnumerable<TechnicalPhotoQualityEvidence> source)
    {
        TechnicalPhotoQualityEvidence[] evidence = source.ToArray();
        if (evidence.Length == 0)
        {
            throw new ArgumentException(
                "Challenge threshold calculation requires evidence.",
                nameof(source));
        }

        return new PhotoQualityChallengeThresholds(
            Percentile(
                evidence.Select(item => item.SharpnessLaplacianVariance),
                0.02d),
            Percentile(
                evidence.Select(item => item.SharpnessLaplacianVariance),
                0.98d),
            Percentile(
                evidence.Select(item => item.MeanLuminance),
                0.02d),
            Percentile(
                evidence.Select(item => item.MeanLuminance),
                0.98d),
            Percentile(
                evidence.Select(item => item.ShadowClippingFraction),
                0.98d),
            Percentile(
                evidence.Select(item => item.HighlightClippingFraction),
                0.98d),
            Percentile(
                evidence.Select(item => (double)item.LuminanceContrastSpan),
                0.02d),
            Percentile(
                evidence.Select(item => item.ProxyLongEdgeFraction),
                0.02d));
    }

    public static PhotoQualityChallengeRarity CalculateRarity(
        IEnumerable<PhotoQualityBenchmarkStratum> source)
    {
        PhotoQualityBenchmarkStratum[] strata = source.ToArray();
        if (strata.Length == 0)
        {
            throw new ArgumentException(
                "Challenge rarity calculation requires strata.",
                nameof(source));
        }

        int cutoff = Math.Max(10, (int)Math.Ceiling(strata.Length * 0.01d));
        return new PhotoQualityChallengeRarity(
            RareValues(strata.Select(item => item.CaptureEra), cutoff),
            RareValues(strata.Select(item => item.MediaFamily), cutoff),
            RareValues(strata.Select(item => item.Resolution), cutoff),
            RareValues(strata.Select(item => item.DeviceFamily), cutoff));
    }

    public static string StableOrderKey(string benchmarkId, string revisionId)
    {
        byte[] digest = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{benchmarkId}\nchallenge\n{revisionId}"));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static Dictionary<string, Queue<PhotoQualityChallengeAnalyzedCandidate>>
        BuildBuckets(
            IEnumerable<PhotoQualityChallengeAnalyzedCandidate> source)
    {
        Dictionary<string, Queue<PhotoQualityChallengeAnalyzedCandidate>> result =
            new(StringComparer.Ordinal);

        foreach (string reason in ReasonOrder)
        {
            PhotoQualityChallengeAnalyzedCandidate[] candidates = source
                .Where(candidate => candidate.Reasons.Contains(
                    reason,
                    StringComparer.Ordinal))
                .OrderBy(candidate => candidate.StableOrderKey, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length > 0)
            {
                result[reason] = new Queue<PhotoQualityChallengeAnalyzedCandidate>(
                    candidates);
            }
        }

        return result;
    }

    private static HashSet<string> RareValues(
        IEnumerable<string> source,
        int cutoff) =>
        source
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() <= cutoff)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

    private static double Percentile(
        IEnumerable<double> source,
        double percentile)
    {
        double[] sorted = source
            .OrderBy(value => value)
            .ToArray();
        if (sorted.Length == 0)
        {
            return 0d;
        }

        int index = Math.Clamp(
            (int)Math.Ceiling(sorted.Length * percentile) - 1,
            0,
            sorted.Length - 1);
        return sorted[index];
    }
}

internal sealed record PhotoQualityChallengeThresholds(
    double SharpnessLow,
    double SharpnessHigh,
    double LuminanceLow,
    double LuminanceHigh,
    double ShadowClippingHigh,
    double HighlightClippingHigh,
    double ContrastLow,
    double ProxyResolutionLow);

internal sealed record PhotoQualityChallengeRarity(
    IReadOnlySet<string> CaptureEras,
    IReadOnlySet<string> MediaFamilies,
    IReadOnlySet<string> Resolutions,
    IReadOnlySet<string> DeviceFamilies);
