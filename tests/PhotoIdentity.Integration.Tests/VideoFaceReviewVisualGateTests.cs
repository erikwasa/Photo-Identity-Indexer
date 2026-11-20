using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Deterministic browser state-machine tests. No original video, images,
/// FFmpeg, models, PostgreSQL, network, or JS runtime are required.
/// </summary>
public sealed class VideoFaceReviewVisualGateTests
{
    [Fact]
    public void Switching_faces_revokes_previous_visual_and_both_human_approvals()
    {
        var gate = new VideoFaceReviewVisualGate();
        long first = gate.Begin("face-A");
        Assert.True(gate.CropLoaded(first, "face-A"));
        gate.VisualConfirmed = true;
        gate.PresenceApproved = true;
        Assert.True(gate.CanReview("face-A"));

        long next = gate.Begin("face-B");
        Assert.NotEqual(first, next);
        Assert.False(gate.HasVisual);
        Assert.False(gate.VisualConfirmed);
        Assert.False(gate.PresenceApproved);
        Assert.False(gate.CanReview("face-A"));
        Assert.False(gate.CanReview("face-B"));

        Assert.False(gate.CropLoaded(first, "face-A"));
        Assert.False(gate.ProxyFrameReady(first, "face-A"));
        Assert.False(gate.CropFailed(first, "face-A"));
        Assert.False(gate.ProxyFailed(first, "face-A"));
        Assert.False(gate.HasVisual);
        gate.VisualConfirmed = true;
        Assert.False(gate.CanReview("face-B")); // checkbox is not a frame

        Assert.True(gate.CropLoaded(next, "face-B"));
        Assert.False(gate.VisualConfirmed); // must inspect the new loaded image
        Assert.False(gate.CanReview("face-B"));
        gate.VisualConfirmed = true;
        Assert.True(gate.CanReview("face-B"));
    }

    [Fact]
    public void Reinspecting_the_same_face_is_a_new_independent_session()
    {
        var gate = new VideoFaceReviewVisualGate();
        long old = gate.Begin("same-face");
        gate.ProxyFrameReady(old, "same-face");
        gate.VisualConfirmed = true;
        gate.PresenceApproved = true;
        Assert.True(gate.CanReview("same-face"));

        long current = gate.Begin("same-face");
        Assert.NotEqual(old, current);
        Assert.False(gate.ProxyFrameReady(old, "same-face"));
        Assert.False(gate.CropLoaded(old, "same-face"));
        Assert.False(gate.CanReview("same-face"));
        Assert.False(gate.PresenceApproved);
        Assert.True(gate.ProxyFrameReady(current, "same-face"));
        Assert.False(gate.CanReview("same-face"));
        gate.VisualConfirmed = true;
        Assert.True(gate.CanReview("same-face"));
    }

    [Fact]
    public void Wrong_face_in_same_generation_can_never_unlock_review()
    {
        var gate = new VideoFaceReviewVisualGate();
        long current = gate.Begin("target");
        Assert.False(gate.CropLoaded(current, "other"));
        Assert.False(gate.ProxyFrameReady(current, "other"));
        Assert.False(gate.CropFailed(current, "other"));
        Assert.False(gate.ProxyFailed(current, "other"));
        Assert.False(gate.Matches(current, "other"));
        Assert.True(gate.Matches(current, "target"));
        Assert.False(gate.HasVisual);
    }

    [Fact]
    public void Proxy_fallback_requires_valid_seek_and_fresh_visual_acknowledgement()
    {
        var gate = new VideoFaceReviewVisualGate();
        long current = gate.Begin("target");
        Assert.True(gate.CropFailed(current, "target"));
        Assert.True(gate.CropUnavailable);
        gate.VisualConfirmed = true;
        Assert.False(gate.CanReview("target")); // native crop denied, no proxy

        Assert.True(gate.ProxyFrameReady(current, "target"));
        Assert.True(gate.HasVisual);
        Assert.False(gate.CropReady);
        Assert.False(gate.VisualConfirmed);
        gate.VisualConfirmed = true;
        Assert.True(gate.CanReview("target"));

        Assert.True(gate.ProxyFailed(current, "target"));
        Assert.False(gate.ProxyReady);
        Assert.True(gate.ProxyUnavailable);
        Assert.False(gate.VisualConfirmed);
        Assert.False(gate.CanReview("target"));
    }

    [Fact]
    public void Native_crop_can_support_review_without_playable_proxy()
    {
        var gate = new VideoFaceReviewVisualGate();
        long current = gate.Begin("target");
        Assert.True(gate.ProxyFailed(current, "target"));
        Assert.False(gate.HasVisual);
        Assert.True(gate.CropLoaded(current, "target"));
        Assert.True(gate.HasVisual);
        Assert.True(gate.CropReady);
        gate.VisualConfirmed = true;
        Assert.True(gate.CanReview("target"));

        // A later proxy failure may not fabricate new evidence, and revokes
        // stale acknowledgement even though the native crop remains visible.
        Assert.True(gate.ProxyFailed(current, "target"));
        Assert.True(gate.CropReady);
        Assert.False(gate.VisualConfirmed);
        gate.VisualConfirmed = true;
        Assert.True(gate.CanReview("target"));
    }

    [Fact]
    public void Newly_arriving_visual_context_revokes_prior_presence_approval()
    {
        var gate = new VideoFaceReviewVisualGate();
        long current = gate.Begin("target");
        gate.ProxyFrameReady(current, "target");
        gate.VisualConfirmed = true;
        gate.PresenceApproved = true;
        Assert.True(gate.CanReview("target"));

        // An image may complete only after the proxy was inspected.
        Assert.True(gate.CropLoaded(current, "target"));
        Assert.False(gate.VisualConfirmed);
        Assert.False(gate.PresenceApproved);
        gate.VisualConfirmed = true;
        gate.PresenceApproved = true;

        Assert.True(gate.CropFailed(current, "target"));
        Assert.False(gate.VisualConfirmed);
        Assert.False(gate.PresenceApproved);
        Assert.True(gate.HasVisual); // separately loaded proxy still valid
    }

    [Fact]
    public void Reset_disarms_all_pending_callbacks_even_for_the_same_observation()
    {
        var gate = new VideoFaceReviewVisualGate();
        long previous = gate.Begin("target");
        gate.CropLoaded(previous, "target");
        gate.VisualConfirmed = true;
        gate.Reset();
        Assert.Null(gate.ObservationId);
        Assert.False(gate.CropLoaded(previous, "target"));
        Assert.False(gate.ProxyFrameReady(previous, "target"));
        Assert.False(gate.CanReview("target"));
        Assert.Throws<ArgumentException>(() => gate.Begin("  "));
    }

    [Fact]
    public void Blazor_page_mounts_distinct_visual_players_and_ignores_stale_callbacks()
    {
        string path = Path.Combine(Root(), "src", "PhotoIdentity.Web", "Pages",
            "VideoFacePersistedReview.razor");
        string page = File.ReadAllText(path);
        Assert.Contains(@"@key=""inspectionToken""", page);
        Assert.Contains("VisualGate.Begin(face.Id)", page);
        Assert.Contains("VisualGate.Reset()", page);
        Assert.Contains("VisualGate.CanReview(face.Id)", page);
        Assert.Contains("OnNativeCropLoaded(inspectionToken, inspectedFace.Id)", page);
        Assert.Contains("OnNativeCropUnavailable(inspectionToken, inspectedFace.Id)", page);
        Assert.Contains("OnSeeked(inspectionToken, inspectedFace.Id)", page);
        Assert.Contains("OnPlaybackError(inspectionToken, inspectedFace.Id)", page);
        Assert.Contains("VisualGate.ProxyFailed(generation, observationId)", page);
        Assert.Contains("!CanUndo(face)", page);
        Assert.DoesNotContain("private bool NativeCropReady;", page);
        Assert.DoesNotContain("private bool FrameReady;", page);
        Assert.DoesNotContain("localStorage", page);
    }

    private static string Root()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? dir = new(Path.GetFullPath(start));
            for (int depth = 0; dir is not null && depth < 12; depth++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "PhotoIdentity.slnx")))
                    return dir.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
