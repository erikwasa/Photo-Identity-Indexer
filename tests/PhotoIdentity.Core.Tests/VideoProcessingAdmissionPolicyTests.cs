using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity.Core.Tests;

public sealed class VideoProcessingAdmissionPolicyTests
{
    private static readonly VideoProcessingCandidate LocalVideo = new(
        IsCurrentRevision: true,
        IsExcluded: false,
        IsLocallyAvailable: true,
        HasSupportedInspection: true,
        SourceBytes: 50_000,
        ExpectedTemporaryBytes: 200_000,
        RequestedFrames: 12);

    private static readonly VideoProcessingActivity Idle = new(
        PhotoWorkActive: false, RunningVideoJobs: 0, ReservedTemporaryBytes: 0);

    private static readonly VideoProcessingBudget Enabled = new(
        Enabled: true, Paused: false,
        MaximumSourceBytes: 100_000,
        MaximumTemporaryBytes: 1_000_000,
        MaximumExtractedFrames: 24);

    [Fact]
    public void Defaults_never_admit_video_jobs()
    {
        Assert.False(new VideoProcessingBudget().Enabled);
        Assert.True(new VideoProcessingBudget().Paused);
        Assert.Equal("video-disabled", Decide(new VideoProcessingBudget()));
        Assert.Equal("video-paused", Decide(Enabled with { Paused = true }));
    }

    [Theory]
    [InlineData("photo-priority")]
    [InlineData("concurrency-budget")]
    [InlineData("temporary-budget")]
    [InlineData("source-budget")]
    [InlineData("frame-budget")]
    [InlineData("revision-unavailable")]
    [InlineData("unsupported-video")]
    [InlineData("online-only")]
    public void Each_explicit_guard_denies_admission(string expectedCode)
    {
        VideoProcessingCandidate candidate = LocalVideo;
        VideoProcessingActivity activity = Idle;
        switch (expectedCode)
        {
            case "photo-priority": activity = activity with { PhotoWorkActive = true }; break;
            case "concurrency-budget": activity = activity with { RunningVideoJobs = 1 }; break;
            case "temporary-budget": activity = activity with { ReservedTemporaryBytes = 900_000 }; break;
            case "source-budget": candidate = candidate with { SourceBytes = 100_001 }; break;
            case "frame-budget": candidate = candidate with { RequestedFrames = 25 }; break;
            case "revision-unavailable": candidate = candidate with { IsExcluded = true }; break;
            case "unsupported-video": candidate = candidate with { HasSupportedInspection = false }; break;
            case "online-only": candidate = candidate with { IsLocallyAvailable = false }; break;
        }

        VideoProcessingAdmissionResult result =
            VideoProcessingAdmissionPolicy.Evaluate(Enabled, candidate, activity);
        Assert.False(result.Admitted);
        Assert.Equal(expectedCode, result.ReasonCode);
    }

    [Fact]
    public void Fail_closed_on_stale_revision_and_invalid_estimates()
    {
        Assert.Equal("revision-unavailable", Decide(Enabled,
            LocalVideo with { IsCurrentRevision = false }));
        Assert.Equal("source-budget", Decide(Enabled,
            LocalVideo with { SourceBytes = 0 }));
        Assert.Equal("temporary-budget", Decide(Enabled,
            LocalVideo with { ExpectedTemporaryBytes = 0 }));
        Assert.Equal("temporary-budget", Decide(Enabled, LocalVideo,
            Idle with { ReservedTemporaryBytes = -1 }));
        Assert.Equal("concurrency-budget", Decide(Enabled, LocalVideo,
            Idle with { RunningVideoJobs = -1 }));
    }

    [Fact]
    public void Exact_budget_boundaries_admit_only_locally_verified_current_video()
    {
        VideoProcessingAdmissionResult result = VideoProcessingAdmissionPolicy.Evaluate(
            Enabled, LocalVideo with
            {
                SourceBytes = 100_000,
                ExpectedTemporaryBytes = 600_000,
                RequestedFrames = 24,
            },
            Idle with { ReservedTemporaryBytes = 400_000 });
        Assert.True(result.Admitted);
        Assert.Equal("accepted", result.ReasonCode);
    }

    [Fact]
    public void Invalid_resource_limits_are_rejected_before_scheduling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Enabled with { MaximumConcurrentJobs = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Enabled with { MaximumConcurrentJobs = 5 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Enabled with { MaximumJobSeconds = 1801 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Enabled with { MaximumSourceBytes = long.MaxValue }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Enabled with { MaximumTemporaryBytes = -1 }).Validate());
    }

    [Fact]
    public void Idempotency_key_is_revision_policy_preset_kind_and_tool_bound()
    {
        AssetRevisionId revision = AssetRevisionId.New();
        var original = new VideoDerivativeJobIdentity(revision,
            VideoDerivativeKind.PlayableProxy, "policy-v1", "720p-v1", new string('a', 64));

        string key = original.IdempotencyKey();
        Assert.Equal(64, key.Length);
        Assert.Equal(key, new VideoDerivativeJobIdentity(revision,
            VideoDerivativeKind.PlayableProxy, "policy-v1", "720p-v1",
            new string('a', 64)).IdempotencyKey());
        Assert.NotEqual(key, new VideoDerivativeJobIdentity(AssetRevisionId.New(),
            original.Kind, original.PolicyVersion, original.PresetVersion,
            original.ToolSha256).IdempotencyKey());
        Assert.NotEqual(key, new VideoDerivativeJobIdentity(revision,
            VideoDerivativeKind.FrameEvidence, original.PolicyVersion,
            original.PresetVersion, original.ToolSha256).IdempotencyKey());
        Assert.NotEqual(key, new VideoDerivativeJobIdentity(revision,
            original.Kind, "policy-v2", original.PresetVersion,
            original.ToolSha256).IdempotencyKey());
        Assert.NotEqual(key, new VideoDerivativeJobIdentity(revision,
            original.Kind, original.PolicyVersion, "720p-v2",
            original.ToolSha256).IdempotencyKey());
        Assert.NotEqual(key, new VideoDerivativeJobIdentity(revision,
            original.Kind, original.PolicyVersion, original.PresetVersion,
            new string('b', 64)).IdempotencyKey());
    }

    [Fact]
    public void Invalid_job_identity_cannot_enter_future_persistent_queue()
    {
        AssetRevisionId revision = AssetRevisionId.New();
        Assert.Throws<ArgumentException>(() => new VideoDerivativeJobIdentity(
            default, VideoDerivativeKind.PlayableProxy, "v1", "v1", new string('a', 64)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoDerivativeJobIdentity(
            revision, (VideoDerivativeKind)123, "v1", "v1", new string('a', 64)));
        Assert.Throws<ArgumentException>(() => new VideoDerivativeJobIdentity(
            revision, VideoDerivativeKind.PlayableProxy, "../unsafe", "v1", new string('a', 64)));
        Assert.Throws<ArgumentException>(() => new VideoDerivativeJobIdentity(
            revision, VideoDerivativeKind.PlayableProxy, "v1", "v1", new string('A', 64)));
    }

    private static string Decide(
        VideoProcessingBudget budget,
        VideoProcessingCandidate? candidate = null,
        VideoProcessingActivity? activity = null) =>
        VideoProcessingAdmissionPolicy.Evaluate(
            budget, candidate ?? LocalVideo, activity ?? Idle).ReasonCode;
}
