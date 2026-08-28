using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveAdvancementWorkClassifierTests
{
    [Fact]
    public void Runnable_work_stays_running_while_a_onedrive_transition_exists()
    {
        ArchiveAdvancementWorkClassification state = ArchiveAdvancementWorkClassifier.Classify(
            hasRunnableWork: true,
            hasOneDriveTransition: true);

        Assert.True(state.HasWork);
        Assert.False(state.WaitingForOneDrive);
    }

    [Fact]
    public void Onedrive_transition_is_waiting_when_it_is_the_only_remaining_work()
    {
        ArchiveAdvancementWorkClassification state = ArchiveAdvancementWorkClassifier.Classify(
            hasRunnableWork: false,
            hasOneDriveTransition: true);

        Assert.True(state.HasWork);
        Assert.True(state.WaitingForOneDrive);
    }

    [Fact]
    public void Onedrive_blocked_work_reports_waiting_even_when_it_is_still_pending()
    {
        ArchiveAdvancementWorkClassification state = ArchiveAdvancementWorkClassifier.Classify(
            hasRunnableWork: false,
            hasOneDriveTransition: true,
            hasOneDriveBlockedWork: true);

        Assert.True(state.HasWork);
        Assert.True(state.WaitingForOneDrive);
    }

    [Fact]
    public void Runnable_work_still_runs_when_other_work_is_blocked_on_onedrive()
    {
        ArchiveAdvancementWorkClassification state = ArchiveAdvancementWorkClassifier.Classify(
            hasRunnableWork: true,
            hasOneDriveTransition: true,
            hasOneDriveBlockedWork: true);

        Assert.True(state.HasWork);
        Assert.False(state.WaitingForOneDrive);
    }

    [Fact]
    public void No_runnable_work_or_transition_is_complete()
    {
        ArchiveAdvancementWorkClassification state = ArchiveAdvancementWorkClassifier.Classify(
            hasRunnableWork: false,
            hasOneDriveTransition: false);

        Assert.False(state.HasWork);
        Assert.False(state.WaitingForOneDrive);
    }
}
