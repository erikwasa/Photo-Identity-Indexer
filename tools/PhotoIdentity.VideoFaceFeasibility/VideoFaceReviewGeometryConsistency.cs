using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Private review-only geometry. The selected detection order must match the
/// original private post-NMS, post-retention face ordinal contract.
/// </summary>
public sealed record VideoFaceReviewBox(
    int FaceNumber, double X, double Y, double Width, double Height);

public static class VideoFaceReviewGeometryConsistency
{
    public static IReadOnlyList<VideoFaceReviewBox> Reconstruct(
        IReadOnlyList<DetectedFaceCandidate> raw,
        VideoFaceFrameDetectionDiagnostic expected)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(expected);
        VideoFaceCandidateSelection selected =
            VideoFaceCandidateLimiter.Apply(raw, VideoFaceOverflowPolicy.RetainTop32);

        int expectedRaw = expected.RawCandidatesDetected ?? expected.FacesDetected;
        int expectedDiscarded = expected.CandidatesDiscarded ?? 0;
        if (selected.RawCandidateCount != expectedRaw ||
            selected.Candidates.Count != expected.FacesDetected ||
            selected.DiscardedCandidates != expectedDiscarded)
        {
            throw new InvalidOperationException(
                "Reconstructed face counts disagree with the private report.");
        }

        if (selected.Candidates.Count > 0 &&
            (expected.LowestConfidence is not double minimum ||
             expected.HighestConfidence is not double maximum ||
             !double.IsFinite(minimum) || !double.IsFinite(maximum) ||
             Math.Abs(selected.Candidates.Min(d => d.Confidence) - minimum) > 0.001 ||
             Math.Abs(selected.Candidates.Max(d => d.Confidence) - maximum) > 0.001))
        {
            throw new InvalidOperationException(
                "Reconstructed detector confidence disagrees with the private report.");
        }

        return selected.Candidates
            .Select((face, index) => new VideoFaceReviewBox(index + 1,
                face.BoundingBox.X,
                face.BoundingBox.Y,
                face.BoundingBox.Width,
                face.BoundingBox.Height))
            .ToArray();
    }
}
