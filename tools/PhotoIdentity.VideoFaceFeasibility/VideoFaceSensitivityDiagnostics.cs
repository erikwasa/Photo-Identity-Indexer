using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Exploratory, strictly private diagnostic evidence. A ranked alias is not an identity
/// decision and threshold comparisons never modify the normal review result.
/// </summary>
public sealed record VideoFaceThresholdCheck(
    double SimilarityThreshold,
    string Outcome);

public sealed record VideoFaceFrameDetectionDiagnostic(
    int FrameNumber,
    long TimestampMs,
    int FacesDetected,
    double? LowestConfidence,
    double? HighestConfidence);

public sealed record VideoFaceRankedDiagnostic(
    int FrameNumber,
    long TimestampMs,
    int FaceNumber,
    string TopReferenceAlias,
    double TopSimilarity,
    double? RunnerUpSimilarity,
    double? ScoreGap,
    IReadOnlyList<VideoFaceThresholdCheck> ThresholdChecks);

public sealed record VideoFaceSensitivityDiagnostics(
    double RequestedDetectorConfidenceThreshold,
    IReadOnlyList<VideoFaceFrameDetectionDiagnostic> FrameDetections,
    IReadOnlyList<VideoFaceRankedDiagnostic> RankedFaces)
{
    // Values selected for a sensitivity *comparison*, not verified cutoffs.
    public static readonly IReadOnlyList<double> SimilarityThresholds =
        Array.AsReadOnly(new[] { 0.363, 0.45, 0.60, 0.80 });

    public static IReadOnlyList<VideoFaceThresholdCheck> Compare(
        double topSimilarity,
        double? runnerUpSimilarity)
    {
        if (!double.IsFinite(topSimilarity) ||
            (runnerUpSimilarity is double next && !double.IsFinite(next)))
        {
            throw new ArgumentException("Similarities must be finite.");
        }

        return SimilarityThresholds.Select(threshold =>
            new VideoFaceThresholdCheck(
                threshold,
                topSimilarity < threshold
                    ? "below-threshold"
                    : runnerUpSimilarity is null
                        ? "single-reference-uncertain"
                        : topSimilarity - runnerUpSimilarity.Value <
                          VideoFaceReferenceMatcher.ExploratoryGap
                            ? "ambiguous"
                            : "would-suggest-for-review")).ToArray();
    }

    public static VideoFaceFrameDetectionDiagnostic DescribeFrame(
        int frameNumber,
        long timestampMs,
        IReadOnlyList<DetectedFaceCandidate> detections)
    {
        ArgumentNullException.ThrowIfNull(detections);
        if (frameNumber < 1 || timestampMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameNumber));
        }

        return new VideoFaceFrameDetectionDiagnostic(
            frameNumber,
            timestampMs,
            detections.Count,
            detections.Count == 0 ? null : Math.Round(detections.Min(d => d.Confidence), 4),
            detections.Count == 0 ? null : Math.Round(detections.Max(d => d.Confidence), 4));
    }
}
