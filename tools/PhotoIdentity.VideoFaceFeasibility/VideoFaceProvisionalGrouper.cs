using System.Text.Json.Serialization;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// PRIVATE, opt-in, conservative provisional within-video grouping.
/// Groups are NOT people, tracks, or verified identity assignments.
/// Only up to five representative face boxes per group leave memory; no vectors are serialized.
/// </summary>
public sealed record VideoFaceGroupCrop(
    int FrameNumber,
    long TimestampMs,
    int FaceNumber,
    double X,
    double Y,
    double Width,
    double Height,
    double DetectorConfidence,
    double Quality);

public sealed record VideoFaceProvisionalGroup(
    string Alias,
    int Observations,
    IReadOnlyList<VideoFaceGroupCrop> RepresentativeCrops);

public sealed record VideoFaceProvisionalGroupReview(
    int SchemaVersion,
    int ObservationsConsidered,
    int ObservationsAssigned,
    int ObservationsUnassigned,
    int MaximumGroups,
    double MinimumWithinGroupCosine,
    IReadOnlyList<VideoFaceProvisionalGroup> Groups)
{
    // Only when explicitly opted in; never present in legacy reports.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoFaceUnassignedAudit? UnassignedAudit { get; init; }
}

/// <summary>
/// Bounded single-pass grouping on SFace vectors already calculated by the
/// approved isolated experiment. No identity labels participate in grouping.
/// Group caps are explicit: unmatched evidence is accounted for, NEVER silently
/// claimed to be detected people or discarded real faces.
/// </summary>
public sealed class VideoFaceProvisionalGrouper
{
    public const int MaximumGroups = 64;
    public const int MaximumCropsPerGroup = 5;
    public const double MinimumCosine = 0.82;
    public const double MinimumWinnerMargin = 0.04;
    public const int MinimumRepresentativeFrameSeparation = 3;

    private sealed class Group
    {
        public required string Alias { get; init; }
        public readonly List<(EmbeddingVector Vector, VideoFaceGroupCrop Crop)> Representatives = [];
        public int Observations;
        public int LastObservedFrame;
    }

    private readonly List<Group> _groups = [];
    private readonly VideoFaceUnassignedAuditSampler? _unassignedAudit;
    private int _seen;
    private int _assigned;
    private int _unassigned;

    public VideoFaceProvisionalGrouper(
        bool captureUnassignedAudit = false,
        long firstTimestampMs = 0,
        long lastTimestampMs = VideoFaceFrameSetLoader.MaximumTimestampMs)
    {
        _unassignedAudit = captureUnassignedAudit
            ? new VideoFaceUnassignedAuditSampler(firstTimestampMs, lastTimestampMs)
            : null;
    }

    /// <summary>
    /// Observe the actual face ordinal in the same frame, before any catalogue
    /// rank is consulted. Two detections in one frame cannot join one group.
    /// </summary>
    public void Observe(int frameNumber, long timestampMs, int faceNumber,
        EmbeddingVector embedding, DetectedFaceCandidate detection, ImageSize imageSize)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentNullException.ThrowIfNull(detection);
        if (frameNumber < 1 || timestampMs < 0 || faceNumber < 1 ||
            imageSize.Width < 1 || imageSize.Height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frameNumber));
        }

        _seen++;
        Group? winner = null;
        double best = double.NegativeInfinity;
        double runnerUp = double.NegativeInfinity;

        foreach (Group group in _groups)
        {
            // Same-frame evidence is never evidence of the SAME person.
            if (group.LastObservedFrame == frameNumber)
            {
                continue;
            }

            double similarity = group.Representatives.Max(
                r => embedding.CosineSimilarity(r.Vector));
            if (similarity > best)
            {
                runnerUp = best;
                best = similarity;
                winner = group;
            }
            else if (similarity > runnerUp)
            {
                runnerUp = similarity;
            }
        }

        var b = detection.BoundingBox;
        double minPixels = Math.Min(b.Width * imageSize.Width,
            b.Height * imageSize.Height);
        // A ranking proxy for the best crops, not a face-quality probability.
        double quality = Math.Round(detection.Confidence *
            Math.Min(1, Math.Max(0, minPixels) / 160), 4);
        var crop = new VideoFaceGroupCrop(frameNumber, timestampMs, faceNumber,
            b.X, b.Y, b.Width, b.Height, Math.Round(detection.Confidence, 4), quality);

        // No decision is inferred from catalogue nearest neighbours.
        // Ambiguity creates an extra group if there is capacity: false splits
        // are more reviewable than mistakenly merging two distinct people.
        bool confidentGroup = winner is not null && best >= MinimumCosine &&
            (runnerUp == double.NegativeInfinity || best - runnerUp >= MinimumWinnerMargin);
        if (!confidentGroup)
        {
            if (_groups.Count >= MaximumGroups)
            {
                _unassigned++;
                _unassignedAudit?.Observe(crop);
                return;
            }

            winner = new Group
            {
                Alias = $"G{_groups.Count + 1:D3}",
            };
            _groups.Add(winner);
        }

        Group assigned = winner ?? throw new InvalidOperationException(
            "Provisional grouping failed to assign the selected candidate.");
        assigned.Observations++;
        assigned.LastObservedFrame = frameNumber;
        _assigned++;
        RetainDiverseRepresentative(assigned, embedding, crop);
    }

    public VideoFaceProvisionalGroupReview CreateReview() =>
        new(
            1, _seen, _assigned, _unassigned, MaximumGroups,
            MinimumCosine,
            _groups.Select(group => new VideoFaceProvisionalGroup(
                group.Alias, group.Observations,
                group.Representatives.Select(r => r.Crop)
                    .OrderByDescending(c => c.Quality)
                    .ThenBy(c => c.FrameNumber)
                    .ThenBy(c => c.FaceNumber)
                    .ToArray()))
                .OrderByDescending(g => g.Observations)
                .ThenBy(g => g.Alias, StringComparer.Ordinal)
                .ToArray())
        {
            UnassignedAudit = _unassignedAudit?.CreateReview(_unassigned),
        };

    /// <summary>
    /// Read-only advisory ranks for the retained in-memory group representatives.
    /// Must be called before the grouper is discarded; no embeddings are serialized.
    /// Unassigned observations are never misrepresented as ranked groups.
    /// </summary>
    public IReadOnlyList<VideoFaceCatalogueGroupSuggestion> RankGroups(
        VideoFaceCataloguePersonMatcher matcher)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        return _groups
            .OrderByDescending(g => g.Observations)
            .ThenBy(g => g.Alias, StringComparer.Ordinal)
            .Select(group => new VideoFaceCatalogueGroupSuggestion(
                group.Alias,
                group.Representatives.Count,
                group.Observations,
                matcher.RankRepresentatives(
                    group.Representatives.Select(r => r.Vector).ToArray())))
            .ToArray();
    }

    private static void RetainDiverseRepresentative(Group group,
        EmbeddingVector embedding, VideoFaceGroupCrop crop)
    {
        // Keep diverse moments; replace a near-duplicate only when it improves
        // the proxy quality. A very low-quality observation must not evict one
        // of the best samples or turn into the sole matching anchor.
        int near = group.Representatives.FindIndex(r =>
            Math.Abs(r.Crop.FrameNumber - crop.FrameNumber) <
                MinimumRepresentativeFrameSeparation);
        if (near >= 0)
        {
            if (crop.Quality > group.Representatives[near].Crop.Quality)
            {
                group.Representatives[near] = (embedding, crop);
            }
            return;
        }

        if (group.Representatives.Count < MaximumCropsPerGroup)
        {
            group.Representatives.Add((embedding, crop));
            return;
        }

        int weakest = 0;
        for (int i = 1; i < group.Representatives.Count; i++)
        {
            if (group.Representatives[i].Crop.Quality <
                group.Representatives[weakest].Crop.Quality)
            {
                weakest = i;
            }
        }
        if (crop.Quality > group.Representatives[weakest].Crop.Quality)
        {
            group.Representatives[weakest] = (embedding, crop);
        }
    }
}
