using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Recognition;

public sealed record FaceDetectionReconciliationOptions
{
    public double MinimumIntersectionOverUnion { get; init; } = 0.30;
    public double MaximumLandmarkDistanceRatio { get; init; } = 0.20;

    internal void Validate()
    {
        if (!double.IsFinite(MinimumIntersectionOverUnion) || MinimumIntersectionOverUnion is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumIntersectionOverUnion),
                "Minimum IoU must be between zero and one.");
        }

        if (!double.IsFinite(MaximumLandmarkDistanceRatio) || MaximumLandmarkDistanceRatio < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumLandmarkDistanceRatio),
                "Maximum landmark distance ratio must be finite and non-negative.");
        }
    }
}

public sealed record ExistingFaceDetectionAnchor(
    FaceOccurrenceId FaceOccurrenceId,
    NormalizedBoundingBox BoundingBox,
    NormalizedFaceLandmarks Landmarks);

public sealed record CandidateFaceDetectionAnchor(
    int CandidateIndex,
    NormalizedBoundingBox BoundingBox,
    NormalizedFaceLandmarks Landmarks)
{
    public CandidateFaceDetectionAnchor(
        int candidateIndex,
        DetectedFaceCandidate candidate)
        : this(
            candidateIndex,
            candidate?.BoundingBox ?? throw new ArgumentNullException(nameof(candidate)),
            candidate.Landmarks)
    {
    }
}

public enum FaceDetectionReconciliationDisposition
{
    ExistingOccurrence,
    NewOccurrence,
    Ambiguous,
}

public sealed record FaceDetectionReconciliationDecision(
    int CandidateIndex,
    FaceDetectionReconciliationDisposition Disposition,
    FaceOccurrenceId? ExistingFaceOccurrenceId,
    IReadOnlyList<FaceOccurrenceId> PossibleExistingFaceOccurrenceIds);

public sealed record FaceDetectionReconciliationPlan(
    IReadOnlyList<FaceDetectionReconciliationDecision> CandidateDecisions,
    IReadOnlyList<FaceOccurrenceId> ExistingOccurrencesWithoutCandidate)
{
    public bool HasAmbiguity =>
        CandidateDecisions.Any(decision => decision.Disposition == FaceDetectionReconciliationDisposition.Ambiguous);
}

/// <summary>
/// Conservatively reconciles a new detector result against persisted occurrences without relying on detection order.
/// </summary>
public static class FaceDetectionReconciliationPlanner
{
    public static FaceDetectionReconciliationPlan Plan(
        IReadOnlyList<ExistingFaceDetectionAnchor> existingFaces,
        IReadOnlyList<CandidateFaceDetectionAnchor> candidateFaces,
        FaceDetectionReconciliationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(existingFaces);
        ArgumentNullException.ThrowIfNull(candidateFaces);
        options ??= new FaceDetectionReconciliationOptions();
        options.Validate();

        if (existingFaces.Select(face => face.FaceOccurrenceId).Distinct().Count() != existingFaces.Count)
        {
            throw new ArgumentException("Existing face occurrence identifiers must be unique.", nameof(existingFaces));
        }

        if (candidateFaces.Select(face => face.CandidateIndex).Distinct().Count() != candidateFaces.Count ||
            candidateFaces.Any(face => face.CandidateIndex < 0))
        {
            throw new ArgumentException("Candidate indices must be unique and non-negative.", nameof(candidateFaces));
        }

        Dictionary<int, List<FaceOccurrenceId>> candidateEligible = candidateFaces.ToDictionary(
            face => face.CandidateIndex,
            _ => new List<FaceOccurrenceId>());
        Dictionary<FaceOccurrenceId, int> existingEligibleCounts = existingFaces.ToDictionary(
            face => face.FaceOccurrenceId,
            _ => 0);

        foreach (CandidateFaceDetectionAnchor candidate in candidateFaces)
        {
            foreach (ExistingFaceDetectionAnchor existing in existingFaces)
            {
                if (!IsEligible(existing, candidate, options))
                {
                    continue;
                }

                candidateEligible[candidate.CandidateIndex].Add(existing.FaceOccurrenceId);
                existingEligibleCounts[existing.FaceOccurrenceId]++;
            }
        }

        List<FaceDetectionReconciliationDecision> decisions = [];
        HashSet<FaceOccurrenceId> matchedExisting = [];
        foreach (CandidateFaceDetectionAnchor candidate in candidateFaces.OrderBy(face => face.CandidateIndex))
        {
            FaceOccurrenceId[] eligible = candidateEligible[candidate.CandidateIndex]
                .OrderBy(id => id.ToString(), StringComparer.Ordinal)
                .ToArray();

            if (eligible.Length == 0)
            {
                decisions.Add(new FaceDetectionReconciliationDecision(
                    candidate.CandidateIndex,
                    FaceDetectionReconciliationDisposition.NewOccurrence,
                    null,
                    []));
                continue;
            }

            if (eligible.Length == 1 && existingEligibleCounts[eligible[0]] == 1)
            {
                matchedExisting.Add(eligible[0]);
                decisions.Add(new FaceDetectionReconciliationDecision(
                    candidate.CandidateIndex,
                    FaceDetectionReconciliationDisposition.ExistingOccurrence,
                    eligible[0],
                    eligible));
                continue;
            }

            decisions.Add(new FaceDetectionReconciliationDecision(
                candidate.CandidateIndex,
                FaceDetectionReconciliationDisposition.Ambiguous,
                null,
                eligible));
        }

        FaceOccurrenceId[] unmatchedExisting = existingFaces
            .Select(face => face.FaceOccurrenceId)
            .Where(id => !matchedExisting.Contains(id))
            .OrderBy(id => id.ToString(), StringComparer.Ordinal)
            .ToArray();

        return new FaceDetectionReconciliationPlan(decisions, unmatchedExisting);
    }

    private static bool IsEligible(
        ExistingFaceDetectionAnchor existing,
        CandidateFaceDetectionAnchor candidate,
        FaceDetectionReconciliationOptions options)
    {
        double iou = existing.BoundingBox.IntersectionOverUnion(candidate.BoundingBox);
        if (iou + 1e-12 < options.MinimumIntersectionOverUnion)
        {
            return false;
        }

        double landmarkDistanceRatio = LandmarkDistanceRatio(
            existing.BoundingBox,
            existing.Landmarks,
            candidate.BoundingBox,
            candidate.Landmarks);
        return landmarkDistanceRatio <= options.MaximumLandmarkDistanceRatio + 1e-12;
    }

    private static double LandmarkDistanceRatio(
        NormalizedBoundingBox existingBox,
        NormalizedFaceLandmarks existing,
        NormalizedBoundingBox candidateBox,
        NormalizedFaceLandmarks candidate)
    {
        (NormalizedPoint Existing, NormalizedPoint Candidate)[] pairs =
        [
            (existing.LeftEye, candidate.LeftEye),
            (existing.RightEye, candidate.RightEye),
            (existing.Nose, candidate.Nose),
            (existing.MouthLeft, candidate.MouthLeft),
            (existing.MouthRight, candidate.MouthRight),
        ];

        double meanDistance = pairs.Average(pair => Distance(pair.Existing, pair.Candidate));
        double existingDiagonal = Math.Sqrt(
            (existingBox.Width * existingBox.Width) + (existingBox.Height * existingBox.Height));
        double candidateDiagonal = Math.Sqrt(
            (candidateBox.Width * candidateBox.Width) + (candidateBox.Height * candidateBox.Height));
        double scale = (existingDiagonal + candidateDiagonal) / 2;
        return scale <= 0 ? double.PositiveInfinity : meanDistance / scale;
    }

    private static double Distance(NormalizedPoint left, NormalizedPoint right)
    {
        double dx = left.X - right.X;
        double dy = left.Y - right.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
