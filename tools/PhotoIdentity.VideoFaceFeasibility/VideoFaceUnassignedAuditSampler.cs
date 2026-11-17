using System.Text.Json.Serialization;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// A bounded, strictly private look at detections that COULD NOT join a
/// provisional group after the 64-group ceiling. Samples are not groups,
/// tracks, real-face decisions or catalogue identity proposals.
/// </summary>
public sealed record VideoFaceUnassignedAudit(
    int SchemaVersion,
    int TotalUnassignedObservations,
    int MaximumReviewSamples,
    int TemporalStrata,
    int StrataRepresented,
    IReadOnlyList<VideoFaceGroupCrop> SampledCrops)
{
    public int SampledObservations => SampledCrops.Count;
    public int UnrepresentedObservations => TotalUnassignedObservations - SampledCrops.Count;

    // Absent on legacy highest-quality-only private reports.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectionPolicy { get; init; }
}

/// <summary>
/// Fixed-memory, temporally stratified candidate review. Keep up to 8
/// examples in each of 12 temporal strata and at most two from one frame.
/// Heuristic crop quality is only a way to allocate REVIEW ATTENTION; it is
/// not a face probability, people-presence evidence or a suppression rule.
/// Vectors, canonical IDs and catalogue ranks never enter this sampler.
/// </summary>
public sealed class VideoFaceUnassignedAuditSampler
{
    public const int TemporalStrata = 12;
    public const int MaximumSamplesPerStratum = 8;
    public const int MaximumSamples = TemporalStrata * MaximumSamplesPerStratum;
    public const int MaximumSamplesPerFramePerStratum = 2;
    public const string QualityExtremesPolicy = "quality-extremes-v1";

    private readonly long _firstTimestampMs;
    private readonly long _lastTimestampMs;
    private readonly List<VideoFaceGroupCrop>[] _bins = NewBins();
    // Opt-in review diversity: independently retain the eight lowest quality
    // proxies as well as the eight highest in each time stratum, then select
    // up to four from each extreme. No actual observation is suppressed.
    private readonly List<VideoFaceGroupCrop>[]? _lowQualityBins;

    private static List<VideoFaceGroupCrop>[] NewBins() =>
        Enumerable.Range(0, TemporalStrata)
            .Select(_ => new List<VideoFaceGroupCrop>(MaximumSamplesPerStratum))
            .ToArray();

    public VideoFaceUnassignedAuditSampler(
        long firstTimestampMs, long lastTimestampMs,
        bool preserveLowQualityCandidates = false)
    {
        if (firstTimestampMs < 0 || lastTimestampMs < firstTimestampMs ||
            lastTimestampMs > VideoFaceFrameSetLoader.MaximumTimestampMs)
        {
            throw new ArgumentOutOfRangeException(nameof(firstTimestampMs));
        }

        _firstTimestampMs = firstTimestampMs;
        _lastTimestampMs = lastTimestampMs;
        if (preserveLowQualityCandidates) _lowQualityBins = NewBins();
    }

    /// <summary>
    /// An unmatched detection stays unmatched. Only its bounded diagnostic
    /// crop metadata can be sampled; the embedding is not retained.
    /// </summary>
    public void Observe(VideoFaceGroupCrop crop)
    {
        ArgumentNullException.ThrowIfNull(crop);
        if (crop.FrameNumber < 1 || crop.FaceNumber < 1 ||
            crop.TimestampMs < _firstTimestampMs ||
            crop.TimestampMs > _lastTimestampMs ||
            !double.IsFinite(crop.Quality) || crop.Quality is < 0 or > 1 ||
            !double.IsFinite(crop.DetectorConfidence) ||
            crop.DetectorConfidence is < 0 or > 1 ||
            !double.IsFinite(crop.X) || !double.IsFinite(crop.Y) ||
            !double.IsFinite(crop.Width) || !double.IsFinite(crop.Height) ||
            crop.Width <= 0 || crop.Height <= 0)
        {
            throw new ArgumentException("Invalid unassigned private diagnostic crop.");
        }

        long span = _lastTimestampMs - _firstTimestampMs + 1;
        int bin = (int)Math.Min(TemporalStrata - 1,
            (crop.TimestampMs - _firstTimestampMs) * TemporalStrata / span);
        AddCandidate(_bins[bin], crop, CropQualityComparer.Instance);
        if (_lowQualityBins is not null)
            AddCandidate(_lowQualityBins[bin], crop, ReverseQualityComparer.Instance);
    }

    private static void AddCandidate(List<VideoFaceGroupCrop> samples,
        VideoFaceGroupCrop crop, IComparer<VideoFaceGroupCrop> priority)
    {
        // Cap two examples from the same frame independently of quality.
        VideoFaceGroupCrop[] sameFrame = samples
            .Where(sample => sample.FrameNumber == crop.FrameNumber).ToArray();
        if (sameFrame.Length >= MaximumSamplesPerFramePerStratum)
        {
            VideoFaceGroupCrop weakestSame = sameFrame.MinBy(
                sample => sample, priority)!;
            if (priority.Compare(crop, weakestSame) > 0)
            {
                samples.Remove(weakestSame);
                samples.Add(crop);
            }
            return;
        }

        if (samples.Count < MaximumSamplesPerStratum)
        {
            samples.Add(crop);
            return;
        }

        VideoFaceGroupCrop weakest = samples.MinBy(
            sample => sample, priority)!;
        if (priority.Compare(crop, weakest) > 0)
        {
            samples.Remove(weakest);
            samples.Add(crop);
        }
    }

    private static IReadOnlyList<VideoFaceGroupCrop> SelectQualityExtremes(
        List<VideoFaceGroupCrop> strongest, List<VideoFaceGroupCrop> weakest)
    {
        var selected = new List<VideoFaceGroupCrop>(MaximumSamplesPerStratum);
        var ids = new HashSet<(int Frame, int Face)>();
        var perFrame = new Dictionary<int, int>();

        void Take(IEnumerable<VideoFaceGroupCrop> ordered, int limit)
        {
            int added = 0;
            foreach (VideoFaceGroupCrop crop in ordered)
            {
                if (added == limit || selected.Count == MaximumSamplesPerStratum) break;
                if (ids.Contains((crop.FrameNumber, crop.FaceNumber)) ||
                    perFrame.GetValueOrDefault(crop.FrameNumber) >=
                    MaximumSamplesPerFramePerStratum)
                    continue;
                ids.Add((crop.FrameNumber, crop.FaceNumber));
                perFrame[crop.FrameNumber] = perFrame.GetValueOrDefault(crop.FrameNumber) + 1;
                selected.Add(crop);
                added++;
            }
        }

        VideoFaceGroupCrop[] high = strongest
            .OrderByDescending(c => c, CropQualityComparer.Instance).ToArray();
        VideoFaceGroupCrop[] low = weakest
            .OrderByDescending(c => c, ReverseQualityComparer.Instance).ToArray();
        Take(high, MaximumSamplesPerStratum / 2);
        Take(low, MaximumSamplesPerStratum / 2);
        // If a stratum is sparse or the extremes overlap, use the remaining
        // eligible candidates rather than leaving artificial empty slots.
        Take(high.Concat(low), MaximumSamplesPerStratum - selected.Count);
        return selected;
    }

    public VideoFaceUnassignedAudit CreateReview(int totalUnassignedObservations)
    {
        VideoFaceGroupCrop[] selected = (_lowQualityBins is null
                ? _bins.SelectMany(bin => bin)
                : _bins.SelectMany((bin, i) =>
                    SelectQualityExtremes(bin, _lowQualityBins[i])))
            .OrderBy(crop => crop.TimestampMs)
            .ThenBy(crop => crop.FrameNumber)
            .ThenBy(crop => crop.FaceNumber)
            .ToArray();
        if (totalUnassignedObservations < selected.Length)
            throw new ArgumentOutOfRangeException(nameof(totalUnassignedObservations));

        return new VideoFaceUnassignedAudit(
            SchemaVersion: 1,
            TotalUnassignedObservations: totalUnassignedObservations,
            MaximumReviewSamples: MaximumSamples,
            TemporalStrata: TemporalStrata,
            StrataRepresented: _bins.Count(bin => bin.Count > 0),
            SampledCrops: selected)
        {
            SelectionPolicy = _lowQualityBins is null ? null : QualityExtremesPolicy,
        };
    }

    private sealed class CropQualityComparer : IComparer<VideoFaceGroupCrop>
    {
        public static CropQualityComparer Instance { get; } = new();

        public int Compare(VideoFaceGroupCrop? left, VideoFaceGroupCrop? right)
        {
            if (left is null) return right is null ? 0 : -1;
            if (right is null) return 1;

            int priority = left.Quality.CompareTo(right.Quality);
            if (priority != 0) return priority;
            priority = left.DetectorConfidence.CompareTo(right.DetectorConfidence);
            if (priority != 0) return priority;
            priority = right.TimestampMs.CompareTo(left.TimestampMs);
            return priority != 0 ? priority : right.FaceNumber.CompareTo(left.FaceNumber);
        }
    }

    private sealed class ReverseQualityComparer : IComparer<VideoFaceGroupCrop>
    {
        public static ReverseQualityComparer Instance { get; } = new();

        public int Compare(VideoFaceGroupCrop? left, VideoFaceGroupCrop? right) =>
            CropQualityComparer.Instance.Compare(right, left);
    }
}
