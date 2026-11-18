using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Private, heuristic grouping of face observations across explicitly selected frames.
/// No person identity or tracking is established; there may be false splits or false merges.
/// Embeddings and exemplars are retained in memory only.
/// </summary>
public sealed record VideoFaceCoverageGroup(
    string GroupAlias,
    int FirstFrameNumber,
    long FirstTimestampMs,
    int Observations);

public sealed record VideoFaceCoverageAssignment(
    int FrameNumber,
    long TimestampMs,
    int FaceNumber,
    string GroupAlias,
    double? ClosestPreviousSimilarity,
    string ReviewStatus);

public sealed record VideoFaceCoverageReview(
    int PossibleAppearanceGroups,
    IReadOnlyList<VideoFaceCoverageGroup> Groups,
    IReadOnlyList<VideoFaceCoverageAssignment> Assignments)
{
    // Extra private evidence: absent in reports produced by earlier checkpoints.
    public IReadOnlyList<VideoFaceCoverageComparison>? GroupingComparisons { get; init; }
    public IReadOnlyList<VideoFaceSameFramePair>? SameFramePairs { get; init; }
}

public sealed class VideoFaceCoverageGrouper
{
    // An exploratory conservative grouping threshold, NOT a unique-person threshold.
    public const double ExploratoryRepeatSimilarity = 0.80;

    private sealed class Group(
        string alias, int firstFrameNumber, long firstTimestampMs,
        EmbeddingVector exemplar)
    {
        public string Alias { get; } = alias;
        public int FirstFrameNumber { get; } = firstFrameNumber;
        public long FirstTimestampMs { get; } = firstTimestampMs;
        public EmbeddingVector Exemplar { get; } = exemplar;
        public List<EmbeddingVector> RecentExemplars { get; } = [exemplar];
        public int Observations { get; set; } = 1;
        public int LastFrameNumber { get; set; } = firstFrameNumber;
    }

    private readonly List<Group> _groups = [];
    private readonly List<VideoFaceCoverageAssignment> _assignments = [];
    private readonly double _repeatSimilarityThreshold;
    private readonly bool _useMultipleObservations;

    public VideoFaceCoverageGrouper(
        double repeatSimilarityThreshold = ExploratoryRepeatSimilarity,
        bool useMultipleObservations = false)
    {
        if (!double.IsFinite(repeatSimilarityThreshold) ||
            repeatSimilarityThreshold < 0.5 || repeatSimilarityThreshold > 0.95)
        {
            throw new ArgumentOutOfRangeException(nameof(repeatSimilarityThreshold));
        }

        _repeatSimilarityThreshold = repeatSimilarityThreshold;
        _useMultipleObservations = useMultipleObservations;
    }

    public IReadOnlyList<VideoFaceCoverageGroup> Groups =>
        _groups.Select(group => new VideoFaceCoverageGroup(
            group.Alias, group.FirstFrameNumber, group.FirstTimestampMs,
            group.Observations)).ToArray();

    public IReadOnlyList<VideoFaceCoverageAssignment> Assignments =>
        _assignments.ToArray();

    public VideoFaceCoverageAssignment Observe(
        int frameNumber, long timestampMs, int faceNumber, EmbeddingVector embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        if (frameNumber < 1 || timestampMs < 0 || faceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frameNumber));
        }

        // The caller must feed video frames in timeline order. Two faces in one
        // frame cannot share a heuristic group, even with similar embeddings.
        if (_assignments.Count > 0 &&
            (frameNumber < _assignments[^1].FrameNumber ||
             (frameNumber == _assignments[^1].FrameNumber &&
              faceNumber <= _assignments[^1].FaceNumber)))
        {
            throw new ArgumentException("Face observations must be in frame/face order.");
        }

        Group? best = null;
        double bestScore = double.NegativeInfinity;
        foreach (Group group in _groups)
        {
            if (group.LastFrameNumber == frameNumber)
            {
                continue;
            }

            double score = _useMultipleObservations
                ? group.RecentExemplars.Max(existing => embedding.CosineSimilarity(existing))
                : embedding.CosineSimilarity(group.Exemplar);
            if (score > bestScore)
            {
                bestScore = score;
                best = group;
            }
        }

        bool possibleRepeat = best is not null &&
            bestScore >= _repeatSimilarityThreshold;
        Group match;
        if (possibleRepeat)
        {
            match = best!;
            match.Observations++;
            match.LastFrameNumber = frameNumber;

            if (_useMultipleObservations)
            {
                // Keep the first embedding as an anchor plus at most seven
                // further observations. Bounded memory and comparisons;
                // these exemplars must never be serialized.
                if (match.RecentExemplars.Count < 8)
                {
                    match.RecentExemplars.Add(embedding);
                }
                else
                {
                    int slot = 1 + ((match.Observations - 9) % 7);
                    match.RecentExemplars[slot] = embedding;
                }
            }
        }
        else
        {
            match = new Group(
                $"G{_groups.Count + 1:0000}", frameNumber, timestampMs, embedding);
            _groups.Add(match);
        }

        VideoFaceCoverageAssignment assignment = new(
            frameNumber,
            timestampMs,
            faceNumber,
            match.Alias,
            best is null ? null : Math.Round(bestScore, 4),
            possibleRepeat ? "possible-repeat" : "needs-distinct-face-review");
        _assignments.Add(assignment);
        return assignment;
    }
}
