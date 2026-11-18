using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Exploratory overflow behavior for manually approved catalogue experiments.
/// The ordinary 32-face limit stays fail-closed. Opt-in retention bounds
/// embedding and ranking work but cannot determine whether discarded candidates
/// are genuine people; every discarded detection is an explicit review caveat.
/// </summary>
public enum VideoFaceOverflowPolicy
{
    Fail,
    RetainTop32,
}

public sealed record VideoFaceCandidateSelection(
    IReadOnlyList<DetectedFaceCandidate> Candidates,
    int RawCandidateCount)
{
    public int DiscardedCandidates => RawCandidateCount - Candidates.Count;
}

public static class VideoFaceCandidateLimiter
{
    public const int MaximumRetainedFaces = 32;

    public static VideoFaceCandidateSelection Apply(
        IReadOnlyList<DetectedFaceCandidate> candidates,
        VideoFaceOverflowPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(policy));
        }

        if (candidates.Count <= MaximumRetainedFaces)
        {
            return new VideoFaceCandidateSelection(candidates, candidates.Count);
        }

        if (policy == VideoFaceOverflowPolicy.Fail)
        {
            throw new InvalidOperationException(
                "The frame exceeded the 32-face exploratory limit.");
        }

        // Stable, deterministic selection after the governed YuNet merge/NMS.
        // This selects model confidence, NOT evidence of actual person identity.
        DetectedFaceCandidate[] selected = candidates
            .Select((candidate, index) => new { candidate, index })
            .OrderByDescending(entry => entry.candidate.Confidence)
            .ThenBy(entry => entry.candidate.BoundingBox.X)
            .ThenBy(entry => entry.candidate.BoundingBox.Y)
            .ThenBy(entry => entry.candidate.BoundingBox.Width)
            .ThenBy(entry => entry.candidate.BoundingBox.Height)
            .ThenBy(entry => entry.index)
            .Take(MaximumRetainedFaces)
            .Select(entry => entry.candidate)
            .ToArray();

        return new VideoFaceCandidateSelection(selected, candidates.Count);
    }
}
