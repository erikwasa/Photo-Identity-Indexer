using PhotoIdentity.Api;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Source.OneDriveSync;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// No live OneDrive file operations: statically verify that video-specific
/// hydration never sets +p and release never clears -p, while photo
/// platform compatibility remains available unchanged.
/// </summary>
public sealed class VideoUnpinnedOneDrivePlatformTests
{
    [Fact]
    public void Transient_video_cloud_api_never_sets_or_clears_user_pin()
    {
        string root = RepositoryRoot();
        string platform = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Source.OneDriveSync",
            "OneDriveFilesOnDemandPlatform.cs"));
        string hydration = Slice(platform,
            "public async Task RequestUnpinnedVideoHydrationAsync(",
            "public async Task RequestUnpinnedVideoReleaseAsync(");
        string release = Slice(platform,
            "public async Task RequestUnpinnedVideoReleaseAsync(",
            "internal static OneDriveFilesOnDemandState Classify(");
        Assert.Contains("CfHydratePlaceholder(", hydration);
        Assert.Contains("File.OpenHandle(", hydration);
        Assert.Contains("WaitAsync(cancellationToken)", hydration);
        Assert.DoesNotContain("RunAttribAsync(\"+p\"", hydration);
        Assert.DoesNotContain("RunAttribAsync(\"-p\"", hydration);
        Assert.Contains("RunAttribAsync(\"+u\"", release);
        Assert.Contains("current.IsPinned", release);
        Assert.DoesNotContain("RunAttribAsync(\"-p\"", release);
        Assert.DoesNotContain("RunAttribAsync(\"+p\"", release);
        Assert.Contains("RunAttribAsync(\"+p\"", platform); // Photos unchanged.
    }

    [Fact]
    public void Video_releases_are_admitted_under_source_lock_and_queue_fence()
    {
        string root = RepositoryRoot();
        string ownership = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Persistence.Postgres",
            "PostgresArchiveHydrationRepository.cs"));
        string queue = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Persistence.Postgres",
            "PostgresVideoProcessingJobRepository.cs"));
        string coordinator = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Api", "VideoManagedHydrationCoordinator.cs"));
        string worker = File.ReadAllText(Path.Combine(root,
            "src", "PhotoIdentity.Api", "VideoArchiveProcessingService.cs"));

        Assert.Contains("GetIdleVideoHydrationsAsync(", ownership);
        Assert.Contains("TryRequestIdleVideoReleaseAsync(", ownership);
        Assert.Contains("CanRetryVideoReleaseAsync(", ownership);
        Assert.Contains("FOR UPDATE OF source", ownership);
        Assert.Contains("job.status IN ('queued','running')", ownership);
        Assert.Contains("release_requested_at_utc IS NOT NULL", queue);
        Assert.Contains("FOR UPDATE OF source", queue);
        Assert.Contains("ReleaseIfIdleAsync(", coordinator);
        Assert.Contains("ReleaseIdleBatchAsync(", coordinator);
        Assert.Contains("RequestUnpinnedVideoReleaseAsync(", coordinator);
        Assert.Contains("RequestUnpinnedVideoHydrationAsync(", coordinator);
        Assert.Contains("ReleaseIfIdleAsync(", worker);
        Assert.Contains("ReleaseIdleBatchAsync(", worker);
        Assert.DoesNotContain("await oneDrive.RequestOnlineOnlyAsync(", worker);
        Assert.DoesNotContain("await oneDrive.RequestHydrationAsync(", worker);
    }

    [Fact]
    public async Task Default_platform_methods_fail_closed_without_transient_cloud_support()
    {
        IOneDriveFilesOnDemandPlatform legacy = new LegacyPlatform();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => legacy.RequestUnpinnedVideoHydrationAsync(
                "synthetic-video-only.mov"));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => legacy.RequestUnpinnedVideoReleaseAsync(
                "synthetic-video-only.mov"));
    }

    private sealed class LegacyPlatform : IOneDriveFilesOnDemandPlatform
    {
        public OneDriveFilesOnDemandState GetState(string path) =>
            new(AssetAvailability.OnlineOnly, false, true);
        public Task RequestHydrationAsync(
            string path, CancellationToken token = default) =>
            Task.CompletedTask;
        public Task RequestOnlineOnlyAsync(
            string path, CancellationToken token = default) =>
            Task.CompletedTask;
    }

    private static string Slice(string text, string start, string end)
    {
        int i = text.IndexOf(start, StringComparison.Ordinal);
        int j = text.IndexOf(end, i + start.Length, StringComparison.Ordinal);
        Assert.True(i >= 0 && j > i);
        return text[i..j];
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
