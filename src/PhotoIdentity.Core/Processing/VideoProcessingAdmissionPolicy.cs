using System.Security.Cryptography;
using System.Text;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Contract for future video-only derivatives. It is intentionally not used by
/// photo jobs, and constructing one does not schedule or read source media.
/// </summary>
public enum VideoDerivativeKind
{
    PlayableProxy,
    FrameEvidence,
    FaceEvidence
}

/// <summary>
/// Stable job identity across process restarts. A change to the revision,
/// output policy, executable or preset is a distinct job, never a stale retry.
/// No original paths or personal metadata enter this identity.
/// </summary>
public sealed record VideoDerivativeJobIdentity
{
    public AssetRevisionId RevisionId { get; }
    public VideoDerivativeKind Kind { get; }
    public string PolicyVersion { get; }
    public string PresetVersion { get; }
    public string ToolSha256 { get; }

    public VideoDerivativeJobIdentity(
        AssetRevisionId revisionId,
        VideoDerivativeKind kind,
        string policyVersion,
        string presetVersion,
        string toolSha256)
    {
        if (revisionId.IsEmpty) throw new ArgumentException("A revision is required.", nameof(revisionId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        PolicyVersion = CheckVersion(policyVersion, nameof(policyVersion));
        PresetVersion = CheckVersion(presetVersion, nameof(presetVersion));
        if (toolSha256 is null || toolSha256.Length != 64 ||
            toolSha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("The tool digest must be 64 lowercase hexadecimal characters.", nameof(toolSha256));
        RevisionId = revisionId;
        Kind = kind;
        ToolSha256 = toolSha256;
    }

    private static string CheckVersion(string? version, string name)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 80 ||
            version.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_')))
            throw new ArgumentException("Versions must use 1-80 lowercase ASCII identifier characters.", name);
        return version;
    }

    /// <summary>Opaque deterministic identifier suitable for a future unique queue constraint.</summary>
    public string IdempotencyKey()
    {
        string canonical = string.Join('\n',
            "video-job-v1", RevisionId.ToString(), Kind.ToString(),
            PolicyVersion, PresetVersion, ToolSha256);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>
/// Deliberately safe defaults; validating these values is NOT equivalent to
/// OS-level CPU, GPU or memory quotas, which the worker must enforce later.
/// </summary>
public sealed record VideoProcessingBudget(
    bool Enabled = false,
    bool Paused = true,
    int MaximumConcurrentJobs = 1,
    long MaximumSourceBytes = 1024L * 1024 * 1024,
    long MaximumTemporaryBytes = 2L * 1024 * 1024 * 1024,
    int MaximumExtractedFrames = 24,
    int MaximumJobSeconds = 900)
{
    public void Validate()
    {
        if (MaximumConcurrentJobs is < 1 or > 4 ||
            MaximumSourceBytes is < 1 or > 1024L * 1024 * 1024 ||
            MaximumTemporaryBytes is < 1 or > 8L * 1024 * 1024 * 1024 ||
            MaximumExtractedFrames is < 1 or > 128 ||
            MaximumJobSeconds is < 1 or > 1800)
            throw new ArgumentOutOfRangeException(nameof(VideoProcessingBudget),
                "Video limits must be positive and within the hard safety ceilings.");
    }
}

/// <summary>Verified facts provided by the future claim handler, never from a directory listing alone.</summary>
public sealed record VideoProcessingCandidate(
    bool IsCurrentRevision,
    bool IsExcluded,
    bool IsLocallyAvailable,
    bool HasSupportedInspection,
    long SourceBytes,
    long ExpectedTemporaryBytes,
    int RequestedFrames);

/// <summary>Instantaneous scheduling facts; admission must be rechecked at claim time.</summary>
public sealed record VideoProcessingActivity(
    bool PhotoWorkActive,
    int RunningVideoJobs,
    long ReservedTemporaryBytes);

public sealed record VideoProcessingAdmissionResult(bool Admitted, string ReasonCode)
{
    public static VideoProcessingAdmissionResult Allowed { get; } = new(true, "accepted");
}

/// <summary>
/// Pure, fail-closed admission policy. A success is NOT authorization to open a
/// file: execution must still take a revision/exclusion/availability lease,
/// recheck conditions atomically and enforce real process/disk budgets.
/// </summary>
public static class VideoProcessingAdmissionPolicy
{
    public static VideoProcessingAdmissionResult Evaluate(
        VideoProcessingBudget budget,
        VideoProcessingCandidate candidate,
        VideoProcessingActivity activity)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(activity);
        budget.Validate();

        if (!budget.Enabled) return Denied("video-disabled");
        if (budget.Paused) return Denied("video-paused");
        if (activity.PhotoWorkActive) return Denied("photo-priority");
        if (!candidate.IsCurrentRevision || candidate.IsExcluded)
            return Denied("revision-unavailable");
        if (!candidate.HasSupportedInspection) return Denied("unsupported-video");
        // Never open a OneDrive placeholder merely to decide whether to enqueue.
        if (!candidate.IsLocallyAvailable) return Denied("online-only");
        if (candidate.SourceBytes is <= 0 ||
            candidate.SourceBytes > budget.MaximumSourceBytes)
            return Denied("source-budget");
        if (candidate.RequestedFrames < 0 ||
            candidate.RequestedFrames > budget.MaximumExtractedFrames)
            return Denied("frame-budget");
        if (candidate.ExpectedTemporaryBytes is <= 0 ||
            candidate.ExpectedTemporaryBytes > budget.MaximumTemporaryBytes ||
            activity.ReservedTemporaryBytes < 0 ||
            activity.ReservedTemporaryBytes >
                budget.MaximumTemporaryBytes - candidate.ExpectedTemporaryBytes)
            return Denied("temporary-budget");
        if (activity.RunningVideoJobs < 0 ||
            activity.RunningVideoJobs >= budget.MaximumConcurrentJobs)
            return Denied("concurrency-budget");
        return VideoProcessingAdmissionResult.Allowed;
    }

    private static VideoProcessingAdmissionResult Denied(string code) => new(false, code);
}
