using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public enum SlideshowPreparationStartupAction
{
    PlayAvailable,
    PrepareOriginals,
}

public enum SlideshowPreparationStatusAction
{
    None,
    ContinuePreparing,
    PlayPrepared,
    RecoverNoProgress,
    RecoverFailure,
}

public static class SlideshowPreparationExperience
{
    public static SlideshowPreparationStartupAction StartupAction(SlideshowSettings settings) =>
        settings.Normalize().PrepareOriginals
            ? SlideshowPreparationStartupAction.PrepareOriginals
            : SlideshowPreparationStartupAction.PlayAvailable;

    public static SlideshowPreparationStatusAction StatusAction(
        SlideshowOriginalPreparationResponse? status)
    {
        if (status is null)
        {
            return SlideshowPreparationStatusAction.None;
        }

        if (string.Equals(status.State, "ready", StringComparison.OrdinalIgnoreCase))
        {
            return SlideshowPreparationStatusAction.PlayPrepared;
        }

        if (string.Equals(status.State, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return SlideshowPreparationStatusAction.RecoverFailure;
        }

        if (string.Equals(status.State, "preparing", StringComparison.OrdinalIgnoreCase))
        {
            return status.NoProgressWarning
                ? SlideshowPreparationStatusAction.RecoverNoProgress
                : SlideshowPreparationStatusAction.ContinuePreparing;
        }

        return SlideshowPreparationStatusAction.None;
    }

    public static bool NeedsParentAttention(SlideshowOriginalPreparationResponse? status) =>
        StatusAction(status) is
            SlideshowPreparationStatusAction.RecoverNoProgress or
            SlideshowPreparationStatusAction.RecoverFailure;

    public static bool CanContinueWithAvailable(SlideshowOriginalPreparationResponse? status) =>
        status is not null &&
        (status.CanContinueWithAvailable || status.NoProgressWarning);
}
