namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Signals observed in already-sampled, operator-approved local frames.
/// No human labels, catalogue matches or inferred person IDs may drive sampling.
/// </summary>
public enum VideoFaceSamplingCueKind
{
    Motion = 1,
    SceneChange = 2,
    UncertainFace = 3,
    PossibleEntranceOrExit = 4,
    PossibleNewOrPoorlyObservedFace = 5,
}

public sealed record VideoFaceSamplingCue(long ObservedFrameTimestampMs, VideoFaceSamplingCueKind Kind);

/// <summary>
/// One bounded temporal window. FrameTimestampsMs are a deterministic plan,
/// NOT a claim that frames were extracted, processed or that people were found.
/// The next window starts at WindowEndMs when HasMoreWindows is true.
/// </summary>
public sealed record VideoFaceSamplingPlan(
    long ClipDurationMs,
    long WindowStartMs,
    long WindowEndMs,
    IReadOnlyList<long> FrameTimestampsMs,
    int CoarseFrameCount,
    bool HasMoreWindows,
    bool RefinementBudgetReached);

/// <summary>
/// Pure, isolated WI-0155 experiment. First cover the entire current window,
/// then optionally densify around content signals observed in those samples.
/// This does not read video, extract frames, trigger inference, or write data.
/// No scoring, group assignment or human identity label influences the plan.
/// </summary>
public static class VideoFaceAdaptiveSamplingPlanner
{
    public const long MaximumClipDurationMs = 3_600_000;
    public const long MaximumWindowLengthMs = 600_000;
    public const long MaximumCoarseGapMs = 8_000;
    public const int MaximumFramesPerWindow = 180;
    public const int MaximumCuesPerWindow = 256;
    private const long MinimumRefinementSeparationMs = 120;

    // Finest additions first; symmetry avoids bias toward entering or exiting.
    private static readonly long[] RefinementOffsetsMs =
        [-250, 250, -750, 750, -1_500, 1_500, -3_000, 3_000];

    /// <summary>
    /// The initial pass always traverses the WHOLE selected window, never stops
    /// on the first known face, and bounds consecutive requested sample gaps.
    /// Videos longer than ten minutes require explicit subsequent windows.
    /// </summary>
    public static VideoFaceSamplingPlan PlanCoverage(
        long clipDurationMs, long windowStartMs = 0)
    {
        if (clipDurationMs is < 200 or > MaximumClipDurationMs ||
            windowStartMs < 0 || windowStartMs >= clipDurationMs)
        {
            throw new ArgumentOutOfRangeException(nameof(clipDurationMs));
        }

        long end = Math.Min(clipDurationMs, windowStartMs + MaximumWindowLengthMs);
        long length = end - windowStartMs;
        long inset = Math.Min(100, (length - 1) / 4);
        long first = windowStartMs + inset;
        long last = end - 1 - inset;
        int intervals = first == last
            ? 0
            : checked((int)((last - first + MaximumCoarseGapMs - 1) /
                MaximumCoarseGapMs));
        var times = new long[intervals + 1];
        for (int i = 0; i <= intervals; i++)
        {
            times[i] = intervals == 0
                ? first
                : first + (last - first) * i / intervals;
        }

        if (times.Length > MaximumFramesPerWindow)
        {
            throw new InvalidOperationException("Coarse coverage exceeded the approved frame budget.");
        }

        return new VideoFaceSamplingPlan(
            clipDurationMs, windowStartMs, end, times, times.Length,
            end < clipDurationMs, RefinementBudgetReached: false);
    }

    /// <summary>
    /// Optional second pass, based ONLY on signals at already-planned frames.
    /// Identical cues in different input orders yield the same plan. Each
    /// anchor gets a nearby refinement opportunity before any anchor receives
    /// more distant samples, preventing one busy scene from monopolizing work.
    /// </summary>
    public static VideoFaceSamplingPlan Refine(
        VideoFaceSamplingPlan coverage, IReadOnlyList<VideoFaceSamplingCue> cues)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(cues);
        if (cues.Count > MaximumCuesPerWindow ||
            coverage.ClipDurationMs is < 200 or > MaximumClipDurationMs ||
            coverage.WindowStartMs < 0 ||
            coverage.WindowEndMs <= coverage.WindowStartMs ||
            coverage.WindowEndMs > coverage.ClipDurationMs ||
            coverage.WindowEndMs - coverage.WindowStartMs > MaximumWindowLengthMs ||
            coverage.HasMoreWindows != (coverage.WindowEndMs < coverage.ClipDurationMs) ||
            coverage.CoarseFrameCount < 1 ||
            coverage.FrameTimestampsMs is null ||
            coverage.FrameTimestampsMs.Count < coverage.CoarseFrameCount ||
            coverage.FrameTimestampsMs.Count > MaximumFramesPerWindow)
        {
            throw new ArgumentException("Invalid bounded coverage or cue count.");
        }

        var selected = new SortedSet<long>();
        foreach (long time in coverage.FrameTimestampsMs)
        {
            if (time < coverage.WindowStartMs || time >= coverage.WindowEndMs ||
                !selected.Add(time))
            {
                throw new ArgumentException("Coverage must contain unique in-window timestamps.");
            }
        }

        var anchors = new Dictionary<long, VideoFaceSamplingCueKind>();
        foreach (VideoFaceSamplingCue cue in cues)
        {
            if (cue is null || !Enum.IsDefined(cue.Kind) ||
                !selected.Contains(cue.ObservedFrameTimestampMs))
            {
                throw new ArgumentException("Sampling cues must belong to observed planned frames.");
            }

            if (!anchors.TryGetValue(cue.ObservedFrameTimestampMs, out var previous) ||
                cue.Kind > previous)
            {
                anchors[cue.ObservedFrameTimestampMs] = cue.Kind;
            }
        }

        // Priority resolves equal-distance competition, never recognition
        // or human labels. Temporal order is stable for equal priority.
        long[] prioritized = anchors
            .OrderByDescending(a => a.Value)
            .ThenBy(a => a.Key)
            .Select(a => a.Key)
            .ToArray();
        bool truncated = coverage.RefinementBudgetReached;
        foreach (long offset in RefinementOffsetsMs)
        {
            foreach (long anchor in prioritized)
            {
                long time = Math.Clamp(anchor + offset,
                    coverage.WindowStartMs, coverage.WindowEndMs - 1);
                long lower = Math.Max(coverage.WindowStartMs,
                    time - MinimumRefinementSeparationMs + 1);
                long upper = Math.Min(coverage.WindowEndMs - 1,
                    time + MinimumRefinementSeparationMs - 1);
                if (selected.GetViewBetween(lower, upper).Count != 0)
                {
                    continue;
                }

                if (selected.Count == MaximumFramesPerWindow)
                {
                    truncated = true;
                    continue;
                }

                selected.Add(time);
            }
        }

        return coverage with
        {
            FrameTimestampsMs = selected.ToArray(),
            RefinementBudgetReached = truncated,
        };
    }
}
