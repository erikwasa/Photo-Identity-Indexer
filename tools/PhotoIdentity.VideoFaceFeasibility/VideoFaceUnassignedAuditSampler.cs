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

    private readonly long _firstTimestampMs;
    private readonly long _lastTimestampMs;
    private readonly List<VideoFaceGroupCrop>[] _bins =
        Enumerable.Range(0, TemporalStrata)
            .Select(_ => new List<VideoFaceGroupCrop>(MaximumSamplesPerStratum))
            .ToArray();

    public VideoFaceUnassignedAuditSampler(long firstTimestampMs, long lastTimestampMs)
    {
        if (firstTimestampMs < 0 || lastTimestampMs < firstTimestampMs ||
            lastTimestampMs > VideoFaceFrameSetLoader.MaximumTimestampMs)
        {
            throw new ArgumentOutOfRangeException(nameof(firstTimestampMs));
        }

        _firstTimestampMs = firstTimestampMs;
        _lastTimestampMs = lastTimestampMs;
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
            crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 ||
            crop.X + crop.Width > 1.000001 || crop.Y + crop.Height > 1.000001)
        {
            throw new ArgumentException("Invalid unassigned private diagnostic crop.");
        }

        long span = _lastTimestampMs - _firstTimestampMs + 1;
        int bin = (int)Math.Min(TemporalStrata - 1,
            (crop.TimestampMs - _firstTimestampMs) * TemporalStrata / span);
        List<VideoFaceGroupCrop> samples = _bins[bin];
        // Prevent one crowded frame from consuming every slot, without
        // discarding a second distinct face simply because it co-occurs.
        VideoFaceGroupCrop[] sameFrame = samples
            .Where(sample => sample.FrameNumber == crop.FrameNumber)
            .OrderBy(sample => sample, CropQualityComparer.Instance)
            .ToArray();

        if (sameFrame.Length >= MaximumSamplesPerFramePerStratum)
        {
            VideoFaceGroupCrop weakestSame = sameFrame[0];
            if (CropQualityComparer.Instance.Compare(crop, weakestSame) > 0)
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
            sample => sample, CropQualityComparer.Instance)!;
        if (CropQualityComparer.Instance.Compare(crop, weakest) > 0)
        {
            samples.Remove(weakest);
            samples.Add(crop);
        }
    }

    public VideoFaceUnassignedAudit CreateReview(int totalUnassignedObservations)
    {
        VideoFaceGroupCrop[] selected = _bins
            .SelectMany(bin => bin)
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
            SampledCrops: selected);
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
            // Stable tie-breakers select earlier moments and the first
            // face ordinal, never a catalogue match or human identity.
            priority = right.TimestampMs.CompareTo(left.TimestampMs);
            return priority != 0 ? priority : right.FaceNumber.CompareTo(left.FaceNumber);
        }
    }
}
