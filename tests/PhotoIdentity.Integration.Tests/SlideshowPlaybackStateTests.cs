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
}
