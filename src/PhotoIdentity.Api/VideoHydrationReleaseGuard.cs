using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

/// <summary>
/// WI-0207 transitional fail-closed cleanup policy for previously
/// app-requested online-only video originals. A Files On-Demand pin bit
/// cannot distinguish an application pin from a concurrent user pin.
/// Never clear it merely because a worker attempt finished.
///
/// Release of an unpinned local file is permitted only after fresh
/// catalogue/dependency and photo-priority checks. This policy is NOT
/// the final cross-job durable hydration ownership implementation.
/// </summary>
public static class VideoHydrationReleaseGuard
{
    public const string NotOwned = "not-app-owned";
    public const string Pinned = "pinned-local-preserved";
    public const string ActiveDependentJob = "dependent-video-work";
    public const string PhotoPriority = "photo-work-active";
    public const string StaleRevision = "revision-or-exclusion-changed";
    public const string NotLocal = "not-local-or-unavailable";
    public const string ReleaseAllowed = "release-allowed";

    public static string Evaluate(
        bool appRequestedHydration,
        OneDriveFilesOnDemandState current,
        bool currentUnexcludedRevision,
        bool queuedOrRunningVideoWork,
        bool photoWorkActive)
    {
        if (!appRequestedHydration) return NotOwned;
        if (!currentUnexcludedRevision) return StaleRevision;
        if (photoWorkActive) return PhotoPriority;
        if (queuedOrRunningVideoWork) return ActiveDependentJob;
        if (current.Availability != AssetAvailability.Local)
            return NotLocal;

        // The app currently uses +p to hydrate an online-only video.
        // As long as this bit is set it is impossible to prove the user
        // has not also chosen 'Always keep on this device'. Retain
        // rather than issue -p/+u on their behalf.
        if (current.IsPinned) return Pinned;
        return ReleaseAllowed;
    }
}
