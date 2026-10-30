namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Planning only: this contract does not decode, hydrate, inspect a source or
/// reserve actual resources. A future executor must enforce a real decode
/// timeout, cancellation and the revision/exclusion lease for every frame.
/// </summary>
public enum VideoFrameSamplingMode
{
    Interval,
    SceneBoundaries
}

public sealed record VideoFrameSamplingRequest(
    decimal DurationSeconds,
    VideoFrameSamplingMode Mode = VideoFrameSamplingMode.Interval,
    IReadOnlyList<decimal>? SceneBoundariesSeconds = null,
    int MaximumFrames = 12,
    int MaximumFramesPerScene = 2,
    int MaximumReservedDecodeMilliseconds = 24_000,
    int ReservedDecodeMillisecondsPerFrame = 1_000);

public sealed record VideoFrameSample(long TimestampMilliseconds, int? SceneIndex);

public sealed record VideoFrameSamplingPlan(
    string PolicyVersion,
    VideoFrameSamplingMode Mode,
    long DurationMilliseconds,
    long ReservedDecodeMilliseconds,
    IReadOnlyList<VideoFrameSample> Samples);

/// <summary>
/// Deterministic, source-free sampling positions for the WI-0190 pilot.
/// A scene boundary must be supplied by a separately verified detector; no
/// scene detection or model inference is performed here. The reservation is
/// a planning ceiling, not a measurement or an enforced runtime quota.
/// </summary>
public static class VideoFrameSamplingPlanner
{
    public const string PolicyVersion = "video-frame-sampling-v1";
    public const int HardMaximumFrames = 24;
    public const int HardMaximumSceneBoundaries = 128;
    public const int MinimumSpacingMilliseconds = 250;

    public static VideoFrameSamplingPlan Plan(VideoFrameSamplingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Mode) ||
            request.DurationSeconds is < 0.2m or > 3_600m ||
            request.MaximumFrames is < 1 or > HardMaximumFrames ||
            request.MaximumFramesPerScene is < 1 or > 2 ||
            request.MaximumReservedDecodeMilliseconds is < 250 or > 48_000 ||
            request.ReservedDecodeMillisecondsPerFrame is < 250 or > 3_000 ||
            request.ReservedDecodeMillisecondsPerFrame > request.MaximumReservedDecodeMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(request),
                "Video sampling settings exceed the bounded sampling contract.");

        long duration = ToMilliseconds(request.DurationSeconds);
        IReadOnlyList<decimal> boundarySeconds = request.SceneBoundariesSeconds ?? [];
        if (boundarySeconds.Count > HardMaximumSceneBoundaries ||
            (request.Mode == VideoFrameSamplingMode.Interval && boundarySeconds.Count != 0))
            throw new ArgumentException(
                "Interval plans cannot accept scene boundaries; scene lists must be bounded.",
                nameof(request));

        List<long> boundaries = [0];
        long previous = 0;
        foreach (decimal seconds in boundarySeconds)
        {
            if (seconds <= 0 || seconds >= request.DurationSeconds)
                throw new ArgumentException("Scene boundaries must be inside the clip.", nameof(request));
            long time = ToMilliseconds(seconds);
            if (time <= previous || time >= duration)
                throw new ArgumentException("Scene boundaries must be strictly increasing at millisecond precision.", nameof(request));
            boundaries.Add(time);
            previous = time;
        }
        boundaries.Add(duration);

        int target = Math.Min(request.MaximumFrames,
            request.MaximumReservedDecodeMilliseconds / request.ReservedDecodeMillisecondsPerFrame);

        List<VideoFrameSample> candidates = [];
        if (request.Mode == VideoFrameSamplingMode.Interval)
        {
            for (int i = 0; i < target; i++)
            {
                long time = (long)decimal.Round(
                    (decimal)duration * (2 * i + 1) / (2 * target),
                    0, MidpointRounding.AwayFromZero);
                candidates.Add(new VideoFrameSample(Math.Clamp(time, 0, duration - 1), null));
            }
        }
        else
        {
            int scenes = boundaries.Count - 1;
            // Very many scenes: spread chosen scenes across the entire clip.
            // Never take just the first N scenes of a long clip.
            if (scenes > target)
            {
                for (int i = 0; i < target; i++)
                {
                    int scene = (int)((long)(2 * i + 1) * scenes / (2 * target));
                    candidates.Add(SceneSample(boundaries, scene, 0, 1));
                }
            }
            else
            {
                // First cover every scene, then add a second position where
                // allowed; scene-first scheduling avoids starving later scenes.
                for (int layer = 0; layer < request.MaximumFramesPerScene; layer++)
                    for (int scene = 0; scene < scenes; scene++)
                        candidates.Add(SceneSample(boundaries, scene, layer,
                            request.MaximumFramesPerScene));
            }
        }

        // Return presentation-time order, with no duplicate or near-duplicate
        // seek targets caused by short scenes or millisecond rounding.
        List<VideoFrameSample> selected = [];
        foreach (VideoFrameSample candidate in candidates)
        {
            if (selected.Count >= target) break;
            if (selected.Any(x =>
                Math.Abs(x.TimestampMilliseconds - candidate.TimestampMilliseconds) <
                MinimumSpacingMilliseconds))
                continue;
            selected.Add(candidate);
        }
        selected.Sort((a, b) => a.TimestampMilliseconds.CompareTo(b.TimestampMilliseconds));
        return new VideoFrameSamplingPlan(
            PolicyVersion, request.Mode, duration,
            (long)selected.Count * request.ReservedDecodeMillisecondsPerFrame,
            selected.AsReadOnly());
    }

    private static VideoFrameSample SceneSample(
        IReadOnlyList<long> boundaries, int scene, int layer, int layers)
    {
        long start = boundaries[scene];
        long length = boundaries[scene + 1] - start;
        long offset = (long)decimal.Round(
            (decimal)length * (layer + 1) / (layers + 1),
            0, MidpointRounding.AwayFromZero);
        return new VideoFrameSample(
            Math.Clamp(start + offset, start, boundaries[scene + 1] - 1), scene);
    }

    private static long ToMilliseconds(decimal seconds) =>
        (long)decimal.Round(seconds * 1_000m, 0, MidpointRounding.AwayFromZero);
}
