using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Private human-review comparisons only. Neither the comparisons nor their aliases
/// are an identity decision, an accurate people count or production tracking.
/// </summary>
public sealed record VideoFaceCoverageObservation(
    int FrameNumber,
    long TimestampMs,
    int FaceNumber,
    EmbeddingVector Embedding);

public sealed record VideoFaceCoverageComparison(
    string Strategy,
    double RepeatSimilarityThreshold,
    int PossibleAppearanceGroups,
    int SingleObservationGroups,
    IReadOnlyList<VideoFaceCoverageGroup> Groups,
    IReadOnlyList<VideoFaceCoverageAssignment> Assignments);

public sealed record VideoFaceSameFramePair(
    int FrameNumber,
    int FirstFaceNumber,
    int SecondFaceNumber,
    double BoundingBoxIntersectionOverUnion,
    double EmbeddingCosineSimilarity,
    string ReviewStatus);

public static class VideoFaceCoverageSensitivity
{
    // Include 0.60 specifically to test whether a similarity around 0.62
    // bridges an obvious split; it is not an approved identity threshold.
    public static readonly IReadOnlyList<double> Thresholds =
        Array.AsReadOnly(new[] { 0.60, 0.70, 0.80 });

    public static IReadOnlyList<VideoFaceCoverageComparison> Compare(
        IReadOnlyList<VideoFaceCoverageObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count > 256)
        {
            throw new ArgumentException("Coverage sensitivity is limited to 256 detected faces.");
        }

        List<VideoFaceCoverageComparison> comparisons = [];
        foreach (double threshold in Thresholds)
        {
            VideoFaceCoverageGrouper grouper = new(
                repeatSimilarityThreshold: threshold,
                useMultipleObservations: true);
            foreach (VideoFaceCoverageObservation observation in observations)
            {
                grouper.Observe(
                    observation.FrameNumber,
                    observation.TimestampMs,
                    observation.FaceNumber,
                    observation.Embedding);
            }

            IReadOnlyList<VideoFaceCoverageGroup> groups = grouper.Groups;
            comparisons.Add(new VideoFaceCoverageComparison(
                "multi-observation-max",
                threshold,
                groups.Count,
                groups.Count(g => g.Observations == 1),
                groups,
                grouper.Assignments));
        }

        return comparisons;
    }

    public static IReadOnlyList<VideoFaceSameFramePair> DescribeSameFrame(
        int frameNumber,
        IReadOnlyList<DetectedFaceCandidate> detections,
        IReadOnlyList<EmbeddingVector> embeddings)
    {
        ArgumentNullException.ThrowIfNull(detections);
        ArgumentNullException.ThrowIfNull(embeddings);
        if (frameNumber < 1 || detections.Count != embeddings.Count)
        {
            throw new ArgumentException("Each same-frame detection needs exactly one embedding.");
        }

        List<VideoFaceSameFramePair> pairs = [];
        for (int first = 0; first < detections.Count; first++)
        {
            for (int second = first + 1; second < detections.Count; second++)
            {
                double overlap = detections[first].BoundingBox.IntersectionOverUnion(
                    detections[second].BoundingBox);
                double similarity = embeddings[first].CosineSimilarity(embeddings[second]);
                pairs.Add(new VideoFaceSameFramePair(
                    frameNumber, first + 1, second + 1,
                    Math.Round(overlap, 4),
                    Math.Round(similarity, 4),
                    overlap >= 0.40 || similarity >= 0.85
                        ? "possible-duplicate-review"
                        : "multiple-faces-review"));
            }
        }

        return pairs;
    }
}
