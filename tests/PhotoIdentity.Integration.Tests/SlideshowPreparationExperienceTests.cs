using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowPreparationExperienceTests
{
    [Fact]
    public void Prepare_off_starts_with_available_playback()
    {
        SlideshowSettings settings = SlideshowSettings.Defaults with { PrepareOriginals = false };

        Assert.Equal(
            SlideshowPreparationStartupAction.PlayAvailable,
            SlideshowPreparationExperience.StartupAction(settings));
    }

    [Fact]
    public void Prepare_on_success_advances_to_prepared_playback()
    {
        SlideshowSettings settings = SlideshowSettings.Defaults with { PrepareOriginals = true };
        SlideshowOriginalPreparationResponse ready = Status(
            state: "ready",
            ready: 4,
            total: 4);

        Assert.Equal(
            SlideshowPreparationStartupAction.PrepareOriginals,
            SlideshowPreparationExperience.StartupAction(settings));
        Assert.Equal(
            SlideshowPreparationStatusAction.PlayPrepared,
            SlideshowPreparationExperience.StatusAction(ready));
        Assert.False(SlideshowPreparationExperience.NeedsParentAttention(ready));
    }

    [Fact]
    public void No_progress_is_an_exception_with_retry_and_available_recovery()
    {
        SlideshowOriginalPreparationResponse stalled = Status(
            state: "preparing",
            ready: 1,
            total: 4,
            noProgressWarning: true,
            canRetry: true,
            canContinueWithAvailable: true);

        Assert.Equal(
            SlideshowPreparationStatusAction.RecoverNoProgress,
            SlideshowPreparationExperience.StatusAction(stalled));
        Assert.True(SlideshowPreparationExperience.NeedsParentAttention(stalled));
        Assert.True(stalled.CanRetry);
        Assert.True(SlideshowPreparationExperience.CanContinueWithAvailable(stalled));
    }

    [Fact]
    public void Capacity_failure_is_explicit_and_keeps_available_recovery()
    {
        SlideshowOriginalPreparationResponse capacity = Status(
            state: "failed",
            ready: 0,
            total: 4,
            requiredAdditionalBytes: 600,
            availableManagedCapacity: 500,
            message: "Insufficient managed capacity.",
            canContinueWithAvailable: true);

        Assert.Equal(
            SlideshowPreparationStatusAction.RecoverFailure,
            SlideshowPreparationExperience.StatusAction(capacity));
        Assert.True(SlideshowPreparationExperience.NeedsParentAttention(capacity));
        Assert.True(capacity.RequiredAdditionalBytes > capacity.AvailableManagedCapacity);
        Assert.True(SlideshowPreparationExperience.CanContinueWithAvailable(capacity));
    }

    [Fact]
    public void Verification_failure_is_explicit_and_keeps_available_recovery()
    {
        SlideshowOriginalPreparationResponse verification = Status(
            state: "failed",
            ready: 2,
            total: 4,
            message: "A prepared original failed immutable verification.",
            canContinueWithAvailable: true);

        Assert.Equal(
            SlideshowPreparationStatusAction.RecoverFailure,
            SlideshowPreparationExperience.StatusAction(verification));
        Assert.True(SlideshowPreparationExperience.NeedsParentAttention(verification));
        Assert.Contains("verification", verification.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(SlideshowPreparationExperience.CanContinueWithAvailable(verification));
    }

    [Fact]
    public void Continue_with_available_is_not_offered_for_routine_progress()
    {
        SlideshowOriginalPreparationResponse preparing = Status(
            state: "preparing",
            ready: 2,
            total: 4);

        Assert.Equal(
            SlideshowPreparationStatusAction.ContinuePreparing,
            SlideshowPreparationExperience.StatusAction(preparing));
        Assert.False(SlideshowPreparationExperience.NeedsParentAttention(preparing));
        Assert.False(SlideshowPreparationExperience.CanContinueWithAvailable(preparing));
    }

    private static SlideshowOriginalPreparationResponse Status(
        string state,
        int ready,
        int total,
        bool noProgressWarning = false,
        bool canRetry = false,
        long requiredAdditionalBytes = 0,
        long availableManagedCapacity = 0,
        string? message = null,
        bool canContinueWithAvailable = false) =>
        new(
            Guid.NewGuid().ToString("D"),
            state,
            ready,
            total,
            Downloading: state == "preparing" ? Math.Max(0, total - ready) : 0,
            Queued: 0,
            WaitingForRelease: 0,
            HydrationRequests: state == "preparing" ? 1 : 0,
            Phase: state,
            LastProgressAtUtc: new DateTimeOffset(2026, 9, 17, 18, 0, 0, TimeSpan.Zero),
            NoProgressSeconds: noProgressWarning ? 91 : 0,
            NoProgressWarning: noProgressWarning,
            CanRetry: canRetry,
            RequiredAdditionalBytes: requiredAdditionalBytes,
            AvailableManagedCapacity: availableManagedCapacity,
            Message: message,
            CanContinueWithAvailable: canContinueWithAvailable);
}
