using PhotoIdentity.Api;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Source-metadata policy tests are synthetic: they never open original bytes,
/// change PostgreSQL, or trigger a OneDrive Cloud Files operation.
/// </summary>
public sealed class VideoFirstInspectionAdmissionTests
{
    private static SourceAsset Make(
        AssetAvailability availability,
        long bytes = 1024,
        string file = "2026/uninspected.mp4",
        string mediaType = "video/mp4") => new(
            new SourceAssetReference(SourceId.New(), file),
            file, mediaType, bytes, DateTimeOffset.UtcNow, availability);

    [Fact]
    public void Unknown_online_only_requires_independent_explicit_consent()
    {
        SourceAsset online = Make(AssetAvailability.OnlineOnly);
        Assert.False(VideoFirstInspectionAdmission.CanDiscover(
            online, false, false, false));
        Assert.True(VideoFirstInspectionAdmission.CanDiscover(
            online, false, false, true));
        Assert.True(VideoFirstInspectionAdmission.CanDiscover(
            Make(AssetAvailability.Local), false, false, false));
        Assert.False(VideoFirstInspectionAdmission.CanDiscover(
            Make(AssetAvailability.Downloading), false, false, true));
        Assert.False(VideoFirstInspectionAdmission.CanDiscover(
            Make(AssetAvailability.Unavailable), false, false, true));
    }

    [Fact]
    public void Existing_inspection_exclusion_and_size_or_extension_are_fail_closed()
    {
        SourceAsset online = Make(AssetAvailability.OnlineOnly);
        Assert.False(VideoFirstInspectionAdmission.CanDiscover(
            online, true, false, true));
        Assert.False(VideoFirstInspectionAdmission.CanDiscover(
            online, false, true, true));
        Assert.False(VideoFirstInspectionAdmission.CanDiscover(
            Make(AssetAvailability.OnlineOnly, 0), false, false, true));
        Assert.True(VideoFirstInspectionAdmission.CanDiscover(
            Make(AssetAvailability.OnlineOnly, 256L * 1024 * 1024),
            false, false, true));
        Assert.False(VideoFirstInspectionAdmission.CanDiscover(
            Make(AssetAvailability.OnlineOnly, 256L * 1024 * 1024 + 1),
            false, false, true));
        Assert.False(VideoFirstInspectionAdmission.CanDiscover(
            Make(AssetAvailability.OnlineOnly, 1024, "2026/movie.avi"),
            false, false, true));
        Assert.True(VideoFirstInspectionAdmission.CanDiscover(
            Make(AssetAvailability.OnlineOnly, 1024, "2026/movie.mov",
                "video/quicktime"), false, false, true));
    }

    [Fact]
    public void Production_path_requires_source_receipt_before_cloud_files_and_never_photo_unpins()
    {
        string root = RepositoryRoot();
        string worker = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Api", "VideoArchiveProcessingService.cs"));
        string managed = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Api", "VideoManagedHydrationCoordinator.cs"));
        string capacity = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Api", "ArchiveHydrationCapacityService.cs"));
        string sources = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Persistence.Postgres",
            "PostgresArchiveSourceObservationRepository.cs"));
        string provisional = File.ReadAllText(Path.Combine(root, "src",
            "PhotoIdentity.Persistence.Postgres",
            "PostgresVideoProvisionalRepository.cs"));

        Assert.Contains("VideoFirstInspectionAdmission.CanDiscover(", worker);
        Assert.Contains("provisionalRepository.ObserveAsync(", worker);
        Assert.Contains("managedHydration.RequestForUninspectedAsync(", worker);
        Assert.Contains("managedHydration.PromoteInspectedSourceAsync(", worker);
        Assert.Contains("managedHydration.ReleaseUninspectedAsync(", worker);
        Assert.Contains("ReconcileUninspectedAsync(", worker);
        Assert.Contains("ExecuteSourceHydrationAdmissionAsync(", managed);
        Assert.Contains("await sourceOwnership.ClaimAsync(", managed);
        Assert.Contains("await oneDrive.RequestUnpinnedVideoHydrationAsync(", managed);
        Assert.Contains("await oneDrive.RequestUnpinnedVideoReleaseAsync(", managed);
        Assert.DoesNotContain("await oneDrive.RequestHydrationAsync(", managed);
        Assert.Contains("asset.media_kind = 'photo'", sources);
        Assert.Contains("lease.IsVideo", capacity);
        Assert.Contains("archive_source_managed_hydrations", provisional);
        Assert.Contains("source_copy_exclusions", provisional);
    }

    private static string RepositoryRoot()
    {
        foreach (string location in new[]
            { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(location));
            for (int depth = 0; directory is not null && depth < 12;
                 depth++, directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                    "PhotoIdentity.slnx")))
                    return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException(
            "Photo Identity repository root not found.");
    }
}
