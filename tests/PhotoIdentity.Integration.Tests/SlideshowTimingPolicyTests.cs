using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowTimingPolicyTests
{
    [Fact]
    public void Missing_or_unreliable_evidence_uses_configured_duration_exactly()
    {
        SlideshowTimingDecision missing = SlideshowTimingPolicy.Create(5, null);
        SlideshowTimingDecision unreliable = SlideshowTimingPolicy.Create(
            5,
            SlideshowPresentationEvidence.Unavailable);
        SlideshowTimingDecision noFaces = SlideshowTimingPolicy.Create(
            5,
            new SlideshowPresentationEvidence(true, 0));

        Assert.Equal(TimeSpan.FromSeconds(5), missing.EffectiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), unreliable.EffectiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), noFaces.EffectiveDuration);
        Assert.Equal(1d, missing.Multiplier);
        Assert.Equal("configured", missing.Reason);
    }

    [Fact]
    public void Reliable_face_count_applies_small_bounded_adjustments()
    {
        SlideshowTimingDecision single = SlideshowTimingPolicy.Create(
            5,
            new SlideshowPresentationEvidence(true, 1));
        SlideshowTimingDecision two = SlideshowTimingPolicy.Create(
            5,
            new SlideshowPresentationEvidence(true, 2));
        SlideshowTimingDecision group = SlideshowTimingPolicy.Create(
            5,
            new SlideshowPresentationEvidence(true, 5));

        Assert.Equal(4.9d, single.EffectiveDuration.TotalSeconds, 6);
        Assert.Equal(5.2d, two.EffectiveDuration.TotalSeconds, 6);
        Assert.Equal(5.4d, group.EffectiveDuration.TotalSeconds, 6);
        Assert.InRange(single.Multiplier, 0.92d, 1.08d);
        Assert.InRange(two.Multiplier, 0.92d, 1.08d);
        Assert.InRange(group.Multiplier, 0.92d, 1.08d);
    }

    [Fact]
    public void Same_evidence_is_deterministic_across_repeated_evaluations()
    {
        SlideshowPresentationEvidence evidence = new(true, 3);

        SlideshowTimingDecision first = SlideshowTimingPolicy.Create(7, evidence);
        SlideshowTimingDecision second = SlideshowTimingPolicy.Create(7, evidence);

        Assert.Equal(first, second);
        Assert.Equal("group", first.Reason);
    }

    [Fact]
    public void Configured_duration_remains_the_dominant_scale()
    {
        SlideshowTimingDecision shortGroup = SlideshowTimingPolicy.Create(
            2,
            new SlideshowPresentationEvidence(true, 4));
        SlideshowTimingDecision longSingle = SlideshowTimingPolicy.Create(
            20,
            new SlideshowPresentationEvidence(true, 1));

        Assert.Equal(2.16d, shortGroup.EffectiveDuration.TotalSeconds, 6);
        Assert.Equal(19.6d, longSingle.EffectiveDuration.TotalSeconds, 6);
    }
}
