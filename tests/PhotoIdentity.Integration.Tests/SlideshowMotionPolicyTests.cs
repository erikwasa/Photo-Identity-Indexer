using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowMotionPolicyTests
{
    [Fact]
    public void No_face_photo_gets_deterministic_subtle_motion_when_geometry_is_reliable()
    {
        string revisionId = "00000001-0000-0000-0000-000000000000";

        SlideshowMotionPlan first = SlideshowMotionPolicy.Create(revisionId, [], geometryReliable: true);
        SlideshowMotionPlan second = SlideshowMotionPolicy.Create(revisionId, [], geometryReliable: true);

        Assert.True(first.Enabled);
        Assert.Equal(1.03d, first.Scale, 6);
        Assert.Equal(first, second);
        Assert.InRange(first.OriginXPercent, 46d, 54d);
        Assert.InRange(first.OriginYPercent, 46d, 54d);
    }

    [Fact]
    public void One_safe_face_pivots_motion_around_the_protected_subject()
    {
        SlideshowFaceBoxResponse face = new(0.30d, 0.25d, 0.20d, 0.25d);

        SlideshowMotionPlan plan = SlideshowMotionPolicy.Create(
            Guid.NewGuid().ToString("D"),
            [face],
            geometryReliable: true);

        Assert.True(plan.Enabled);
        Assert.Equal(40d, plan.OriginXPercent, 6);
        Assert.Equal(37.5d, plan.OriginYPercent, 6);
        Assert.Equal(1.03d, plan.Scale, 6);
    }

    [Fact]
    public void Two_compact_faces_can_move_together()
    {
        SlideshowFaceBoxResponse left = new(0.25d, 0.28d, 0.14d, 0.18d);
        SlideshowFaceBoxResponse right = new(0.50d, 0.27d, 0.14d, 0.18d);

        SlideshowMotionPlan plan = SlideshowMotionPolicy.Create(
            Guid.NewGuid().ToString("D"),
            [left, right],
            geometryReliable: true);

        Assert.True(plan.Enabled);
        Assert.InRange(plan.OriginXPercent, 44d, 45d);
    }

    [Fact]
    public void Group_photo_falls_back_to_static()
    {
        SlideshowMotionPlan plan = SlideshowMotionPolicy.Create(
            Guid.NewGuid().ToString("D"),
            [
                new SlideshowFaceBoxResponse(0.20d, 0.30d, 0.10d, 0.12d),
                new SlideshowFaceBoxResponse(0.40d, 0.30d, 0.10d, 0.12d),
                new SlideshowFaceBoxResponse(0.60d, 0.30d, 0.10d, 0.12d),
            ],
            geometryReliable: true);

        Assert.False(plan.Enabled);
    }

    [Fact]
    public void Edge_near_face_falls_back_to_static_before_zoom_can_crop_it()
    {
        SlideshowMotionPlan plan = SlideshowMotionPolicy.Create(
            Guid.NewGuid().ToString("D"),
            [new SlideshowFaceBoxResponse(0.005d, 0.30d, 0.14d, 0.18d)],
            geometryReliable: true);

        Assert.False(plan.Enabled);
    }

    [Fact]
    public void Tight_or_unreliable_geometry_falls_back_to_static()
    {
        SlideshowMotionPlan tight = SlideshowMotionPolicy.Create(
            Guid.NewGuid().ToString("D"),
            [new SlideshowFaceBoxResponse(0.25d, 0.15d, 0.50d, 0.65d)],
            geometryReliable: true);
        SlideshowMotionPlan unavailable = SlideshowMotionPolicy.Create(
            Guid.NewGuid().ToString("D"),
            [],
            geometryReliable: false);

        Assert.False(tight.Enabled);
        Assert.False(unavailable.Enabled);
    }

    [Fact]
    public void Invalid_face_geometry_is_never_animated()
    {
        SlideshowMotionPlan plan = SlideshowMotionPolicy.Create(
            Guid.NewGuid().ToString("D"),
            [new SlideshowFaceBoxResponse(0.90d, 0.20d, 0.20d, 0.20d)],
            geometryReliable: true);

        Assert.False(plan.Enabled);
    }
}
