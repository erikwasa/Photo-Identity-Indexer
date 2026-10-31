using System.Globalization;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Reproducible, operator-annotated frame sampling identity. Unlike a bare
/// policy version or a digest, the complete cut positions are encoded in the
/// private, bounded job policy string so a later run can reconstruct the plan
/// without a new schema or an untracked sidecar file.
/// </summary>
public static class VideoFrameSceneSamplingIdentity
{
    public const string Prefix = "video-frame-sampling-scene-v1-n";
    public const int MaximumCuts = 8;
    public const int MaximumFrames = 4;
    public const int MaximumPolicyLength = 100;

    public static string Create(int frames, IReadOnlyList<long> cutsMs,
        long durationMs)
    {
        ArgumentNullException.ThrowIfNull(cutsMs);
        if (frames is < 1 or > MaximumFrames ||
            durationMs is < 200 or > 3_600_000 ||
            cutsMs.Count is < 1 or > MaximumCuts)
            throw new ArgumentOutOfRangeException(nameof(cutsMs),
                "Scene sampling requires bounded frames, duration and annotated cuts.");
        long previous = 0;
        foreach (long cut in cutsMs)
        {
            if (cut <= previous || cut >= durationMs)
                throw new ArgumentException(
                    "Scene cuts must be strictly increasing inside the video duration.");
            previous = cut;
        }
        string version = Prefix + frames.ToString(CultureInfo.InvariantCulture)
            + "-" + string.Join("_", cutsMs.Select(x =>
                x.ToString(CultureInfo.InvariantCulture)));
        if (version.Length > MaximumPolicyLength)
            throw new ArgumentException("Scene sampling identity exceeds storage bounds.");
        return version;
    }

    public static bool TryParse(string? version, out int frames,
        out long[] cutsMs)
    {
        frames = 0;
        cutsMs = [];
        if (version is null || version.Length > MaximumPolicyLength ||
            !version.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        string suffix = version[Prefix.Length..];
        int separator = suffix.IndexOf('-');
        if (separator != 1 ||
            suffix[0] is < '1' or > '4' ||
            suffix.Length <= separator + 1)
            return false;
        int count = suffix[0] - '0';
        string[] parts = suffix[(separator + 1)..].Split('_');
        if (parts.Length is < 1 or > MaximumCuts)
            return false;
        long[] parsed = new long[parts.Length];
        long prior = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length is < 1 or > 7 || part[0] == '0' ||
                !long.TryParse(part, NumberStyles.None,
                    CultureInfo.InvariantCulture, out long cut) ||
                cut <= prior || cut >= 3_600_000 ||
                part != cut.ToString(CultureInfo.InvariantCulture))
                return false;
            parsed[i] = cut;
            prior = cut;
        }
        frames = count;
        cutsMs = parsed;
        return true;
    }

    public static bool Permits(string policyVersion, int requestedFrames) =>
        policyVersion == VideoFrameSamplingPlanner.PolicyVersion ||
        (TryParse(policyVersion, out int count, out _) &&
         count == requestedFrames);

    public static VideoFrameSamplingPlan BuildPlan(decimal durationSeconds,
        int requestedFrames, string policyVersion)
    {
        if (policyVersion == VideoFrameSamplingPlanner.PolicyVersion)
            return VideoFrameSamplingPlanner.Plan(
                new VideoFrameSamplingRequest(durationSeconds,
                    MaximumFrames: requestedFrames));

        if (!TryParse(policyVersion, out int frames, out long[] cutsMs) ||
            frames != requestedFrames)
            throw new ArgumentException("Unrecognized or mismatched scene sampling identity.");
        long durationMs = (long)decimal.Round(durationSeconds * 1000m, 0,
            MidpointRounding.AwayFromZero);
        if (Create(requestedFrames, cutsMs, durationMs) != policyVersion)
            throw new ArgumentException("Scene sampling policy cannot be reproduced.");

        VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
            new VideoFrameSamplingRequest(durationSeconds,
                Mode: VideoFrameSamplingMode.SceneBoundaries,
                SceneBoundariesSeconds: cutsMs.Select(x => x / 1000m).ToArray(),
                MaximumFrames: requestedFrames, MaximumFramesPerScene: 1));
        if (plan.Samples.Count != requestedFrames)
            throw new ArgumentException(
                "Scene annotations cannot yield the requested bounded frame count.");
        return plan with { PolicyVersion = policyVersion };
    }
}
