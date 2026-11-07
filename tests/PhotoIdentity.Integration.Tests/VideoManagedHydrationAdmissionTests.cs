using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// WI-0207: no private source opens or real OneDrive operations.
/// Contract-check the production bridge and the shared capacity
/// eviction exclusion; a separate disposable Postgres test verifies
/// the durable photo/video media-kind lease discriminator.
/// </summary>
public sealed class VideoManagedHydrationAdmissionTests
{
    [Fact]
    public void Video_managed_admission_uses_durable_receipts_and_photo_storage_budget()
    {
        string root = RepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root, "src", "PhotoIdentity.Api",
            "VideoManagedHydrationCoordinator.cs"));
        string worker = File.ReadAllText(Path.Combine(
            root, "src", "PhotoIdentity.Api",
            "VideoArchiveProcessingService.cs"));
        string program = File.ReadAllText(Path.Combine(
            root, "src", "PhotoIdentity.Api", "Program.cs"));

        Assert.Contains("ExecuteHydrationAdmissionAsync(", source);
        Assert.Contains("ownership.ClaimAsync(", source);
        Assert.Contains("await oneDrive.RequestUnpinnedVideoHydrationAsync(", source);
        Assert.True(source.IndexOf("await ownership.ClaimAsync(",
            StringComparison.Ordinal) <
            source.IndexOf("await oneDrive.RequestUnpinnedVideoHydrationAsync(",
                StringComparison.Ordinal));
        Assert.Contains("sourceOwnership.GetAsync(", source);
        Assert.Contains("asset.deleted_at_utc IS NULL", source);
        Assert.Contains("source_copy_exclusions", source);
        Assert.Contains("asset.media_kind='video'", source);
        Assert.Contains("latest.asset_id=asset.id", source);
        Assert.Contains("oneDrive.GetState(sourcePath)", source);
        Assert.Contains("state.IsPinned", source);
        Assert.Contains("PostgresVideoPhotoPriorityProbe(database)", source);
        Assert.DoesNotContain("File.Delete(", source);
        Assert.DoesNotContain("File.OpenRead(", source);
        Assert.Contains("managedHydration.RequestForSelectedAsync(", worker);
        Assert.DoesNotContain("await oneDrive.RequestHydrationAsync(ownedPath",
            worker);
        Assert.Contains("managedHydration.TouchAsync(", worker);
        Assert.Contains("managedHydration.ReleaseIfIdleAsync(", worker);
        Assert.Contains("ReleaseIdleBatchAsync(", worker);
        Assert.Contains("TryRequestIdleVideoReleaseAsync(", source);
        Assert.Contains("RequestUnpinnedVideoReleaseAsync(", source);
        Assert.Contains("AddSingleton<VideoManagedHydrationCoordinator>()",
            program);
    }

    [Fact]
    public void Shared_capacity_accounts_for_video_leases_but_must_not_unpin_them()
    {
        string root = RepositoryRoot();
        string service = File.ReadAllText(Path.Combine(
            root, "src", "PhotoIdentity.Api",
            "ArchiveHydrationCapacityService.cs"));
        string repository = File.ReadAllText(Path.Combine(
            root, "src", "PhotoIdentity.Persistence.Postgres",
            "PostgresArchiveHydrationRepository.cs"));

        Assert.Contains("lease.IsVideo);", service);
        Assert.Contains("!item.Lease.IsVideo", service);
        Assert.Contains("reservedBytes = observed", service);
        Assert.Contains("item.Lease.SizeBytes", service);
        Assert.Contains("revision.media_kind = 'video'", repository);
    }

    [Fact]
    public void Video_hydration_preference_and_guard_remain_separate()
    {
        var disabled = new VideoProcessingPreferences(
            Enabled: true, IncludeOnlineOnly: false);
        Assert.False(disabled.IncludeOnlineOnly);
        Assert.Equal(VideoHydrationReleaseGuard.Pinned,
            VideoHydrationReleaseGuard.Evaluate(
                true, new PhotoIdentity.Source.OneDriveSync.OneDriveFilesOnDemandState(
                    AssetAvailability.Local, true, false),
                true, false, false));
        Assert.Equal(VideoHydrationReleaseGuard.ActiveDependentJob,
            VideoHydrationReleaseGuard.Evaluate(
                true, new PhotoIdentity.Source.OneDriveSync.OneDriveFilesOnDemandState(
                    AssetAvailability.Local, false, false),
                true, true, false));
    }

    private static string RepositoryRoot()
    {
        foreach (string location in new[]
            { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? folder = new(Path.GetFullPath(location));
            for (int depth = 0; folder is not null && depth < 12;
                 depth++, folder = folder.Parent)
            {
                if (File.Exists(Path.Combine(folder.FullName, "PhotoIdentity.slnx")))
                    return folder.FullName;
            }
        }
        throw new DirectoryNotFoundException(
            "Photo Identity repository root not found.");
    }
}
