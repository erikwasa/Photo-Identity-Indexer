using PhotoIdentity.Api;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// WI-0207 transitional conservative release policy; synthetic states only.
/// No files are pinned, hydrated, released or opened by these tests.
/// </summary>
public sealed class VideoHydrationReleaseGuardTests
{
    [Theory]
    [InlineData(false, AssetAvailability.Local, false, true, false, false,
        VideoHydrationReleaseGuard.NotOwned)]
    [InlineData(true, AssetAvailability.Local, true, true, false, false,
        VideoHydrationReleaseGuard.Pinned)]
    [InlineData(true, AssetAvailability.Local, false, true, true, false,
        VideoHydrationReleaseGuard.ActiveDependentJob)]
    [InlineData(true, AssetAvailability.Local, false, true, false, true,
        VideoHydrationReleaseGuard.PhotoPriority)]
    [InlineData(true, AssetAvailability.Local, false, false, false, false,
        VideoHydrationReleaseGuard.StaleRevision)]
    [InlineData(true, AssetAvailability.OnlineOnly, false, true, false, false,
        VideoHydrationReleaseGuard.NotLocal)]
    [InlineData(true, AssetAvailability.Downloading, true, true, false, false,
        VideoHydrationReleaseGuard.NotLocal)]
    [InlineData(true, AssetAvailability.Unavailable, false, true, false, false,
        VideoHydrationReleaseGuard.NotLocal)]
    [InlineData(true, AssetAvailability.Error, false, true, false, false,
        VideoHydrationReleaseGuard.NotLocal)]
    [InlineData(true, AssetAvailability.Local, false, true, false, false,
        VideoHydrationReleaseGuard.ReleaseAllowed)]
    public void Release_requires_app_initiation_current_unexcluded_revision_no_dependents_and_unpinned_local(
        bool appHydration, AssetAvailability availability, bool pinned,
        bool currentRevision, bool dependents, bool photoWork, string expected)
    {
        string decision = VideoHydrationReleaseGuard.Evaluate(
            appHydration,
            new OneDriveFilesOnDemandState(availability, pinned, false,
                @"C:\Private\OneDrive\recording.mov"),
            currentRevision, dependents, photoWork);
        Assert.Equal(expected, decision);
        Assert.DoesNotContain("Private", decision, StringComparison.Ordinal);
        Assert.DoesNotContain("recording", decision, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_contract_forbids_unconditional_app_requested_release()
    {
        string root = RepositoryRoot();
        string worker = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Api", "VideoArchiveProcessingService.cs"));
        Assert.Contains("ReadVideoReleaseEvidenceAsync(", worker);
        Assert.Contains("VideoHydrationReleaseGuard.Evaluate(", worker);
        Assert.Contains("video_processing_jobs job", worker);
        Assert.Contains("job.status IN ('queued','running')", worker);
        Assert.Contains("asset.deleted_at_utc IS NULL", worker);
        Assert.Contains("source_copy_exclusions", worker);
        Assert.Contains("PostgresVideoPhotoPriorityProbe(", worker);
        Assert.Contains("oneDrive.GetState(ownedPath)", worker);
        Assert.Contains(
            "if (decision == VideoHydrationReleaseGuard.ReleaseAllowed)", worker);
        Assert.Contains("await managedHydration.ReleaseIfIdleAsync(", worker);
        Assert.DoesNotContain("await oneDrive.RequestOnlineOnlyAsync(", worker);
        Assert.DoesNotContain("File.Delete(ownedPath", worker);
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
