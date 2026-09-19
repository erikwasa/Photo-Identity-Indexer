using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowPlaybackStateTests
{
    [Fact]
    public void Autoplay_waits_for_image_readiness_and_pause_resume_preserves_remaining_time()
    {
        SlideshowPlaybackState state = new();
        state.LoadSnapshot(["a", "b"], SlideshowSettings.Defaults with { ImageDurationSeconds = 5 });

        Assert.True(state.IsPlaying);
        Assert.False(state.IsImageReady);
        Assert.Equal("a", state.CurrentRevisionId);

        Assert.Equal(
            SlideshowAdvanceResult.None,
            state.AdvanceTime(TimeSpan.FromSeconds(10)));
        Assert.Equal("a", state.CurrentRevisionId);

        state.MarkCurrentImageReady();
        Assert.Equal(
            SlideshowAdvanceResult.None,
            state.AdvanceTime(TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(3), state.Remaining);

        state.Pause();
        Assert.Equal(
            SlideshowAdvanceResult.None,
            state.AdvanceTime(TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(3), state.Remaining);

        state.Resume();
        Assert.Equal(
            SlideshowAdvanceResult.Moved,
            state.AdvanceTime(TimeSpan.FromSeconds(3)));
        Assert.Equal("b", state.CurrentRevisionId);
        Assert.False(state.IsImageReady);

        Assert.Equal(
            SlideshowAdvanceResult.None,
            state.AdvanceTime(TimeSpan.FromSeconds(30)));
        state.MarkCurrentImageReady();
        Assert.Equal(TimeSpan.FromSeconds(5), state.Remaining);
    }

    [Fact]
    public void Manual_navigation_resets_timer_only_when_destination_is_ready()
    {
        SlideshowPlaybackState state = new();
        state.LoadSnapshot(["a", "b", "c"], SlideshowSettings.Defaults);
        state.MarkCurrentImageReady();
        _ = state.AdvanceTime(TimeSpan.FromSeconds(4));

        Assert.Equal(TimeSpan.FromSeconds(1), state.Remaining);
        Assert.Equal(SlideshowAdvanceResult.Moved, state.NextManual());
        Assert.Equal("b", state.CurrentRevisionId);
        Assert.False(state.IsImageReady);

        _ = state.AdvanceTime(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(5), state.Remaining);

        state.MarkCurrentImageReady();
        Assert.Equal(TimeSpan.FromSeconds(5), state.Remaining);
        Assert.Equal(SlideshowAdvanceResult.Moved, state.PreviousManual());
        Assert.Equal("a", state.CurrentRevisionId);
    }

    [Fact]
    public void Manual_navigation_can_be_disabled_without_disabling_autoplay()
    {
        SlideshowPlaybackState state = new();
        state.LoadSnapshot(
            ["a", "b"],
            SlideshowSettings.Defaults with { ManualNavigation = false });
        state.MarkCurrentImageReady();

        Assert.True(state.IsPlaying);
        Assert.Equal(SlideshowAdvanceResult.None, state.NextManual());
        Assert.Equal(SlideshowAdvanceResult.None, state.PreviousManual());
        Assert.Equal("a", state.CurrentRevisionId);

        Assert.Equal(
            SlideshowAdvanceResult.Moved,
            state.AdvanceTime(TimeSpan.FromSeconds(5)));
        Assert.Equal("b", state.CurrentRevisionId);
    }

    [Fact]
    public void Hidden_document_freezes_autoplay_without_changing_play_state()
    {
        SlideshowPlaybackState state = new();
        state.LoadSnapshot(["a", "b"], SlideshowSettings.Defaults);
        state.MarkCurrentImageReady();
        _ = state.AdvanceTime(TimeSpan.FromSeconds(2));

        state.SetDocumentVisible(false);
        Assert.True(state.IsPlaying);
        _ = state.AdvanceTime(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromSeconds(3), state.Remaining);
        Assert.Equal("a", state.CurrentRevisionId);

        state.SetDocumentVisible(true);
        Assert.Equal(
            SlideshowAdvanceResult.Moved,
            state.AdvanceTime(TimeSpan.FromSeconds(3)));
        Assert.Equal("b", state.CurrentRevisionId);
    }

    [Fact]
    public void Loop_stop_and_exit_end_behaviors_follow_contract()
    {
        SlideshowPlaybackState loop = new();
        loop.LoadSnapshot(["only"], SlideshowSettings.Defaults);
        loop.MarkCurrentImageReady();

        Assert.Equal(
            SlideshowAdvanceResult.CycledSamePhoto,
            loop.AdvanceTime(TimeSpan.FromSeconds(5)));
        Assert.Equal("only", loop.CurrentRevisionId);
        Assert.True(loop.IsImageReady);
        Assert.Equal(TimeSpan.FromSeconds(5), loop.Remaining);

        SlideshowPlaybackState stop = new();
        stop.LoadSnapshot(
            ["only"],
            SlideshowSettings.Defaults with { AfterLastPhoto = SlideshowSettings.Stop });
        stop.MarkCurrentImageReady();

        Assert.Equal(
            SlideshowAdvanceResult.StoppedAtEnd,
            stop.AdvanceTime(TimeSpan.FromSeconds(5)));
        Assert.False(stop.IsPlaying);
        Assert.Equal("only", stop.CurrentRevisionId);

        SlideshowPlaybackState exit = new();
        exit.LoadSnapshot(
            ["only"],
            SlideshowSettings.Defaults with { AfterLastPhoto = SlideshowSettings.Exit });
        exit.MarkCurrentImageReady();

        Assert.Equal(
            SlideshowAdvanceResult.ExitRequested,
            exit.AdvanceTime(TimeSpan.FromSeconds(5)));
        Assert.True(exit.ExitRequested);
        Assert.False(exit.IsPlaying);
    }

    [Fact]
    public void Zero_photo_snapshot_is_valid_and_prefetch_window_stays_bounded()
    {
        SlideshowPlaybackState zero = new();
        zero.LoadSnapshot([], SlideshowSettings.Defaults);

        Assert.Equal(0, zero.Count);
        Assert.Null(zero.CurrentRevisionId);
        Assert.False(zero.IsPlaying);
        Assert.Empty(zero.GetPrefetchRevisionIds());

        SlideshowPlaybackState many = new();
        many.LoadSnapshot(["a", "b", "c", "d"], SlideshowSettings.Defaults);
        Assert.Equal(["d", "b"], many.GetPrefetchRevisionIds(window: 1));
        Assert.Equal(2, many.GetPrefetchRevisionIds(window: 1).Count);

        many.MarkCurrentImageReady();
        Assert.Equal(SlideshowAdvanceResult.Moved, many.NextManual());
        Assert.Equal(["a", "c"], many.GetPrefetchRevisionIds(window: 1));
        Assert.Equal(2, many.GetPrefetchRevisionIds(window: 1).Count);
    }

    [Fact]
    public void Applying_duration_and_autoplay_settings_updates_active_state_predictably()
    {
        SlideshowPlaybackState state = new();
        state.LoadSnapshot(["a"], SlideshowSettings.Defaults);
        state.MarkCurrentImageReady();
        _ = state.AdvanceTime(TimeSpan.FromSeconds(2.5));

        state.ApplySettings(SlideshowSettings.Defaults with
        {
            ImageDurationSeconds = 10,
            Autoplay = false,
        });

        Assert.False(state.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(5), state.Remaining);

        state.ApplySettings(state.Settings with { Autoplay = true });
        Assert.True(state.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(5), state.Remaining);
    }

    [Fact]
    public void Adaptive_duration_starts_only_after_manual_destination_is_visible()
    {
        SlideshowPlaybackState state = new();
        state.LoadSnapshot(
            ["a", "b"],
            SlideshowSettings.Defaults with { ImageDurationSeconds = 5 });
        state.MarkCurrentImageReady(new SlideshowPresentationEvidence(true, 1));

        Assert.Equal(4.9d, state.CurrentTiming.EffectiveDuration.TotalSeconds, 6);
        _ = state.AdvanceTime(TimeSpan.FromSeconds(2));
        state.Pause();
        double pausedRemaining = state.Remaining.TotalSeconds;
        _ = state.AdvanceTime(TimeSpan.FromSeconds(30));
        Assert.Equal(pausedRemaining, state.Remaining.TotalSeconds, 6);

        state.Resume();
        Assert.Equal(SlideshowAdvanceResult.Moved, state.NextManual());
        Assert.False(state.IsImageReady);
        Assert.Equal(TimeSpan.FromSeconds(5), state.Remaining);

        _ = state.AdvanceTime(TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(5), state.Remaining);

        state.MarkCurrentImageReady(new SlideshowPresentationEvidence(true, 3));
        Assert.Equal(5.4d, state.Remaining.TotalSeconds, 6);
        Assert.Equal("group", state.CurrentTiming.Reason);
    }



    [Fact]
    public void Visual_sequence_policy_compacts_only_autoplay_continuations_in_the_same_group()
    {
        Assert.False(SlideshowVisualSequencePacingPolicy.ShouldCompact(
            null,
            "visual-group-0001",
            SlideshowArrivalKind.Initial));
        Assert.True(SlideshowVisualSequencePacingPolicy.ShouldCompact(
            "visual-group-0001",
            "visual-group-0001",
            SlideshowArrivalKind.Autoplay));
        Assert.False(SlideshowVisualSequencePacingPolicy.ShouldCompact(
            "visual-group-0001",
            "visual-group-0002",
            SlideshowArrivalKind.Autoplay));
        Assert.False(SlideshowVisualSequencePacingPolicy.ShouldCompact(
            "visual-group-0001",
            "visual-group-0001",
            SlideshowArrivalKind.Manual));
        Assert.False(SlideshowVisualSequencePacingPolicy.ShouldCompact(
            "visual-group-0001",
            "visual-group-0001",
            SlideshowArrivalKind.Loop));
    }

    [Fact]
    public void Visual_sequence_duration_is_shorter_but_bounded_for_large_groups()
    {
        SlideshowTimingDecision normal = SlideshowTimingPolicy.Create(
            5,
            SlideshowPresentationEvidence.Unavailable,
            compactVisualSequence: false);
        SlideshowTimingDecision compact = SlideshowTimingPolicy.Create(
            5,
            SlideshowPresentationEvidence.Unavailable,
            compactVisualSequence: true);
        SlideshowTimingDecision longConfigured = SlideshowTimingPolicy.Create(
            60,
            new SlideshowPresentationEvidence(true, 5),
            compactVisualSequence: true);
        SlideshowTimingDecision shortConfigured = SlideshowTimingPolicy.Create(
            2,
            SlideshowPresentationEvidence.Unavailable,
            compactVisualSequence: true);

        Assert.Equal(TimeSpan.FromSeconds(5), normal.EffectiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(2.75), compact.EffectiveDuration);
        Assert.Equal("visual-sequence", compact.Reason);
        Assert.Equal(TimeSpan.FromSeconds(3), longConfigured.EffectiveDuration);
        Assert.Equal(TimeSpan.FromSeconds(1.5), shortConfigured.EffectiveDuration);

        for (int index = 0; index < 20; index++)
        {
            SlideshowTimingDecision member = SlideshowTimingPolicy.Create(
                5,
                SlideshowPresentationEvidence.Unavailable,
                compactVisualSequence: true);
            Assert.Equal(TimeSpan.FromSeconds(2.75), member.EffectiveDuration);
        }
    }

    [Fact]
    public void Manual_destination_and_loop_restart_do_not_inherit_compact_pacing()
    {
        SlideshowPlaybackState state = new();
        state.LoadSnapshot(["a", "b", "c"], SlideshowSettings.Defaults);
        state.MarkCurrentImageReady();

        Assert.Equal(SlideshowArrivalKind.Initial, state.CurrentArrivalKind);
        Assert.Equal(
            SlideshowAdvanceResult.Moved,
            state.AdvanceTime(TimeSpan.FromSeconds(5)));
        Assert.Equal("b", state.CurrentRevisionId);
        Assert.Equal(SlideshowArrivalKind.Autoplay, state.CurrentArrivalKind);

        bool autoplayCompact = SlideshowVisualSequencePacingPolicy.ShouldCompact(
            "visual-group-0001",
            "visual-group-0001",
            state.CurrentArrivalKind);
        state.MarkCurrentImageReady(
            SlideshowPresentationEvidence.Unavailable,
            autoplayCompact);
        Assert.Equal(TimeSpan.FromSeconds(2.75), state.Remaining);

        Assert.Equal(SlideshowAdvanceResult.Moved, state.NextManual());
        Assert.Equal("c", state.CurrentRevisionId);
        Assert.Equal(SlideshowArrivalKind.Manual, state.CurrentArrivalKind);
        bool manualCompact = SlideshowVisualSequencePacingPolicy.ShouldCompact(
            "visual-group-0001",
            "visual-group-0001",
            state.CurrentArrivalKind);
        state.MarkCurrentImageReady(
            SlideshowPresentationEvidence.Unavailable,
            manualCompact);
        Assert.Equal(TimeSpan.FromSeconds(5), state.Remaining);

        Assert.Equal(
            SlideshowAdvanceResult.Moved,
            state.AdvanceTime(TimeSpan.FromSeconds(5)));
        Assert.Equal("a", state.CurrentRevisionId);
        Assert.Equal(SlideshowArrivalKind.Loop, state.CurrentArrivalKind);
        bool loopCompact = SlideshowVisualSequencePacingPolicy.ShouldCompact(
            "visual-group-0001",
            "visual-group-0001",
            state.CurrentArrivalKind);
        state.MarkCurrentImageReady(
            SlideshowPresentationEvidence.Unavailable,
            loopCompact);
        Assert.Equal(TimeSpan.FromSeconds(5), state.Remaining);
    }

    [Fact]
    public void Moment_transition_policy_is_standard_without_a_confirmed_boundary()
    {
        SlideshowMomentTransitionDecision unannotated =
            SlideshowMomentTransitionPolicy.Create(null, null);
        SlideshowMomentTransitionDecision singleton =
            SlideshowMomentTransitionPolicy.Create(null, "moment-0001");
        SlideshowMomentTransitionDecision sameMoment =
            SlideshowMomentTransitionPolicy.Create("moment-0001", "moment-0001");

        Assert.Equal(SlideshowMomentTransitionKind.Standard, unannotated.Kind);
        Assert.Equal(SlideshowMomentTransitionDecision.StandardMilliseconds, unannotated.TransitionMilliseconds);
        Assert.Equal(SlideshowMomentTransitionKind.Standard, singleton.Kind);
        Assert.Equal(SlideshowMomentTransitionKind.Standard, sameMoment.Kind);
    }

    [Fact]
    public void Moment_transition_policy_marks_each_confirmed_chapter_boundary()
    {
        SlideshowMomentTransitionDecision firstBoundary =
            SlideshowMomentTransitionPolicy.Create("moment-0001", "moment-0002");
        SlideshowMomentTransitionDecision secondBoundary =
            SlideshowMomentTransitionPolicy.Create("moment-0002", "moment-0003");

        Assert.Equal(SlideshowMomentTransitionKind.ChapterBoundary, firstBoundary.Kind);
        Assert.Equal(
            SlideshowMomentTransitionDecision.ChapterBoundaryMilliseconds,
            firstBoundary.TransitionMilliseconds);
        Assert.Equal(SlideshowMomentTransitionKind.ChapterBoundary, secondBoundary.Kind);
        Assert.InRange(
            firstBoundary.TransitionMilliseconds,
            SlideshowMomentTransitionDecision.StandardMilliseconds,
            1000);
    }

    [Fact]
    public void Duration_setting_change_preserves_progress_fraction_with_adaptive_policy()
    {
        SlideshowPlaybackState state = new();
        state.LoadSnapshot(
            ["a"],
            SlideshowSettings.Defaults with { ImageDurationSeconds = 5 });
        state.MarkCurrentImageReady(new SlideshowPresentationEvidence(true, 2));
        _ = state.AdvanceTime(TimeSpan.FromSeconds(2.6));

        Assert.Equal(0.5d, state.ProgressFraction, 6);

        state.ApplySettings(state.Settings with { ImageDurationSeconds = 10 });

        Assert.Equal(10.4d, state.CurrentTiming.EffectiveDuration.TotalSeconds, 6);
        Assert.Equal(5.2d, state.Remaining.TotalSeconds, 6);
        Assert.Equal(0.5d, state.ProgressFraction, 6);
    }
}
