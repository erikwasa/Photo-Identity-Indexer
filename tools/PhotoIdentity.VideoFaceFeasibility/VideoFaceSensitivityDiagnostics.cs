using System.Text.Json.Serialization;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Exploratory, strictly private diagnostic evidence. A ranked alias is not an identity
/// decision and threshold comparisons never modify the normal review result.
/// </summary>
public sealed record VideoFaceThresholdCheck(
    double SimilarityThreshold,
    string Outcome);

public sealed record VideoFaceDetectorProfile(
    string PipelineMode,
    int ModelInputWidth,
    int ModelInputHeight,
    int? TileSize);

public sealed record VideoFaceFrameDetectionDiagnostic(
    int FrameNumber,
    long TimestampMs,
    int FacesDetected,
    double? LowestConfidence,
    double? HighestConfidence)
{
    // Dimensions describe the decoded image BEFORE YuNet preprocessing.
    // Approximate face widths are in decoded source pixels, never coordinates.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DecodedWidth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DecodedHeight { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SmallestFaceWidthPixels { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? SmallestFaceHeightPixels { get; init; }

    // Raw means post-YuNet merge/NMS, before the opt-in 32-face selection.
    // Discarded candidates may be real faces: these are not false-positive counts.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RawCandidatesDetected { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CandidatesDiscarded { get; init; }
}

public sealed record VideoFaceRankedDiagnostic(
    int FrameNumber,
    long TimestampMs,
    int FaceNumber,
    string TopReferenceAlias,
    double TopSimilarity,
    double? RunnerUpSimilarity,
    double? ScoreGap,
    IReadOnlyList<VideoFaceThresholdCheck> ThresholdChecks)
{
    // The actual top-three catalogue aliases/scores. Only present when the
    // opt-in catalogue matcher is used; not a verified identity or assignment.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VideoFaceCatalogueRank>? TopCandidates { get; init; }
}

public sealed record VideoFaceSensitivityDiagnostics(
    double RequestedDetectorConfidenceThreshold,
    IReadOnlyList<VideoFaceFrameDetectionDiagnostic> FrameDetections,
    IReadOnlyList<VideoFaceRankedDiagnostic> RankedFaces)
{
    // Absent in the normal private diagnostic run (only explicit --coverage enables it).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoFaceCoverageReview? Coverage { get; init; }

    // Only present for explicit --provisional-groups catalogue experiments.
    // Each group contains a few private source-frame boxes, NOT a canonical person.
    // Never retain embeddings or treat a group alias as a recognized identity.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoFaceProvisionalGroupReview? ProvisionalGroups { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<VideoFaceCatalogueGroupSuggestion>? GroupSuggestions { get; init; }

    // Experimental M25 DBSCAN replay against the same in-memory video vectors.
    // Never overwrites or reinterprets the chronological provisional groups.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoFaceShadowDbscanReview? ShadowDbscan { get; init; }

    // Model tensor size is *not* the decoded frame resolution. In multi-scale
    // mode every source region is separately projected onto that tensor.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoFaceDetectorProfile? DetectorProfile { get; init; }

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
        IReadOnlyList<DetectedFaceCandidate> detections,
        ImageSize? decodedSize = null)
    {
        ArgumentNullException.ThrowIfNull(detections);
        if (frameNumber < 1 || timestampMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameNumber));
        }

        VideoFaceFrameDetectionDiagnostic diagnostic = new(
            frameNumber,
            timestampMs,
            detections.Count,
            detections.Count == 0 ? null : Math.Round(detections.Min(d => d.Confidence), 4),
            detections.Count == 0 ? null : Math.Round(detections.Max(d => d.Confidence), 4));
        return decodedSize is ImageSize size
            ? diagnostic with
            {
                DecodedWidth = size.Width,
                DecodedHeight = size.Height,
                SmallestFaceWidthPixels = detections.Count == 0 ? null : Math.Round(
                    detections.Min(d => d.BoundingBox.Width) * size.Width, 1),
                SmallestFaceHeightPixels = detections.Count == 0 ? null : Math.Round(
                    detections.Min(d => d.BoundingBox.Height) * size.Height, 1),
            }
            : diagnostic;
    }
}
