namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Pure aggregate evidence calculation. Individual private identifiers,
/// annotations and cost observations never leave the input collection.
/// Caller must first enforce current v38 batch provenance and same-snapshot
/// data access; this type has no authority to declare WI-0190 accepted.
/// </summary>
public sealed record VideoFrameAcceptanceObservation(
    Guid JobId, IReadOnlyList<string> Categories,
    int Frames, long DurationMs, long EvidencePayloadBytes,
    VideoFrameSceneCoverageSummary? AnnotatedCoverage,
    long? ExecutionWallMs, long? CpuTimeMs,
    long? GpuTimeMs, long? AzureCostMinorUnits,
    int? CaptionsJudged, int? CaptionsAccurate);

public sealed record VideoFrameAcceptanceEvidenceSummary(
    int Clips, int Frames, long EvidencePayloadBytes,
    long MeanEvidencePayloadBytesPerClip,
    long MinDurationMs, long MaxDurationMs,
    IReadOnlyDictionary<string, int> CategoryClipCounts,
    IReadOnlyList<string> MissingCategories,
    int AnnotatedClips, int AnnotatedScenes,
    int CoveredScenes, int MissedScenes, double? WeightedSceneCoveragePercent,
    int ExecutionTimedClips, long ObservedExecutionWallMs,
    double? ObservedFramesPerMinute,
    int CpuMeasuredClips, long ObservedCpuTimeMs,
    int GpuMeasuredClips, long ObservedGpuTimeMs,
    int AzureCostMeasuredClips, long ObservedAzureCostMinorUnits,
    int CaptionJudgedClips, int CaptionsJudged, int CaptionsAccurate,
    double? HumanCaptionAccuracyPercent,
    bool HumanEvidenceSelfReported, bool FullAcceptanceEstablished);

public static class VideoFrameAcceptanceEvidence
{
    public const string Protocol = "wi0190-acceptance-evidence-v1";
    public const int MaximumClips = 32;
    public static readonly IReadOnlyList<string> RequiredCategories =
        ["short", "multi-scene", "long", "rotated", "low-light"];

    public static VideoFrameAcceptanceEvidenceSummary Summarize(
        IReadOnlyList<VideoFrameAcceptanceObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count is < 1 or > MaximumClips)
            throw new ArgumentException("Acceptance evidence requires bounded clips.");
        HashSet<Guid> identities = [];
        var counts = RequiredCategories.ToDictionary(x => x, _ => 0,
            StringComparer.Ordinal);
        long totalPayload = 0, totalDuration = 0, wall = 0, cpu = 0, gpu = 0, azure = 0;
        int frames = 0, timedFrames = 0, annotatedClips = 0, scenes = 0, covered = 0;
        int timed = 0, cpuMeasured = 0, gpuMeasured = 0, azureMeasured = 0;
        int judgedClips = 0, judged = 0, accurate = 0;
        long minDuration = long.MaxValue, maxDuration = 0;
        foreach (VideoFrameAcceptanceObservation item in observations)
        {
            if (item is null || item.JobId == Guid.Empty ||
                !identities.Add(item.JobId) ||
                item.Categories is null || item.Categories.Count < 1 ||
                item.Categories.Count > RequiredCategories.Count ||
                item.Categories.Distinct(StringComparer.Ordinal).Count() != item.Categories.Count ||
                item.Categories.Any(c => !counts.ContainsKey(c)) ||
                item.Frames is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames ||
                item.DurationMs is < 200 or > 3_600_000 ||
                item.EvidencePayloadBytes < (long)item.Frames * 5 ||
                item.ExecutionWallMs is < 1 or > 86_400_000 ||
                item.CpuTimeMs is < 0 or > 86_400_000 ||
                item.GpuTimeMs is < 0 or > 86_400_000 ||
                item.AzureCostMinorUnits is < 0 or > 1_000_000_000 ||
                item.CaptionsJudged.HasValue != item.CaptionsAccurate.HasValue ||
                item.CaptionsJudged is < 1 or > VideoFrameSamplingPlanner.HardMaximumFrames ||
                item.CaptionsAccurate is < 0 ||
                item.CaptionsAccurate > item.CaptionsJudged ||
                (item.CaptionsJudged is int c && c > item.Frames))
                throw new ArgumentException("Invalid or inconsistent private acceptance observation.");
            foreach (string category in item.Categories) counts[category]++;
            checked
            {
                frames += item.Frames;
                totalPayload += item.EvidencePayloadBytes;
                totalDuration += item.DurationMs;
            }
            minDuration = Math.Min(minDuration, item.DurationMs);
            maxDuration = Math.Max(maxDuration, item.DurationMs);
            if (item.AnnotatedCoverage is not null)
            {
                var coverage = item.AnnotatedCoverage;
                if (coverage.SampledFrames != item.Frames ||
                    coverage.TotalScenes is < 1 or > 129 ||
                    coverage.CoveredScenes < 1 || coverage.MissedScenes < 0 ||
                    coverage.TotalScenes != coverage.CoveredScenes + coverage.MissedScenes)
                    throw new ArgumentException("Scene coverage does not match durable evidence.");
                annotatedClips++;
                scenes += coverage.TotalScenes;
                covered += coverage.CoveredScenes;
            }
            if (item.ExecutionWallMs is long elapsed)
            {
                timed++;
                timedFrames += item.Frames;
                wall += elapsed;
            }
            if (item.CpuTimeMs is long cpuMs)
            {
                cpuMeasured++;
                cpu += cpuMs;
            }
            if (item.GpuTimeMs is long gpuMs)
            {
                gpuMeasured++;
                gpu += gpuMs;
            }
            if (item.AzureCostMinorUnits is long amount)
            {
                azureMeasured++;
                azure += amount;
            }
            if (item.CaptionsJudged is int captionCount)
            {
                judgedClips++;
                judged += captionCount;
                accurate += item.CaptionsAccurate!.Value;
            }
        }
        return new(
            observations.Count, frames, totalPayload,
            totalPayload / observations.Count,
            minDuration, maxDuration, counts,
            RequiredCategories.Where(c => counts[c] == 0).ToArray(),
            annotatedClips, scenes, covered, scenes - covered,
            scenes == 0 ? null : Math.Round(100d * covered / scenes, 2),
            timed, wall,
            wall == 0 ? null : Math.Round(timedFrames * 60_000d / wall, 3),
            cpuMeasured, cpu, gpuMeasured, gpu,
            azureMeasured, azure, judgedClips, judged, accurate,
            judged == 0 ? null : Math.Round(100d * accurate / judged, 2),
            HumanEvidenceSelfReported: true,
            FullAcceptanceEstablished: false);
    }
}
