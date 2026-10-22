using PhotoIdentity.Api;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Core.Identifiers;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoProcessingPreferencesTests
{
    [Fact]
    public async Task Default_off_is_persistent_and_invalid_configuration_fails_closed()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-preferences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "video-processing-preferences.json");
        try
        {
            var store = new VideoProcessingPreferencesStore(path, new(false, false));
            Assert.False(store.Current.Enabled);
            Assert.False(store.Current.IncludeOnlineOnly);

            VideoProcessingPreferences saved =
                await store.SaveAsync(new(true, false));
            Assert.True(saved.Enabled);
            Assert.False(saved.IncludeOnlineOnly);

            var restarted = new VideoProcessingPreferencesStore(path, new(false, false));
            Assert.True(restarted.Current.Enabled);
            Assert.False(restarted.Current.IncludeOnlineOnly);
            await restarted.SaveAsync(new(false, true));

            var disabled = new VideoProcessingPreferencesStore(path, new(false, false));
            Assert.False(disabled.Current.Enabled);
            Assert.True(disabled.Current.IncludeOnlineOnly);

            await File.WriteAllTextAsync(path, "{ invalid preference data");
            var corrupted = new VideoProcessingPreferencesStore(path, new(true, true));
            Assert.False(corrupted.Current.Enabled);
            Assert.False(corrupted.Current.IncludeOnlineOnly);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Queued_online_only_video_does_not_block_newer_local_video()
    {
        SourceId sourceId = SourceId.New();
        var coverage = new ArchiveCoverageState(
            new ArchiveCatalogueSource(sourceId, "local-folder",
                @"C:\Archive", DateTimeOffset.UtcNow),
            ["2024/10"]);
        VideoArchiveCandidate online = new(
            Guid.NewGuid(), sourceId.Value, @"C:\Archive",
            "2024/10/online.mp4", 100, Guid.NewGuid());
        VideoArchiveCandidate local = new(
            Guid.NewGuid(), sourceId.Value, @"C:\Archive",
            "2024/10/local.mp4", 100, Guid.NewGuid());
        Task<AssetAvailability> Availability(
            VideoArchiveCandidate candidate, CancellationToken _) =>
            Task.FromResult(candidate == online
                ? AssetAvailability.OnlineOnly : AssetAvailability.Local);

        Assert.Equal(local, await VideoArchiveCandidateSelector.SelectAsync(
            coverage, [online, local], false, Availability));
        Assert.Equal(local, await VideoArchiveCandidateSelector.SelectAsync(
            coverage, [online, local], true, Availability));
        Assert.Null(await VideoArchiveCandidateSelector.SelectAsync(
            coverage, [online], false, Availability));
        Assert.Equal(online, await VideoArchiveCandidateSelector.SelectAsync(
            coverage, [online], true, Availability));
    }

    [Fact]
    public async Task Local_pending_media_precedes_queued_cloud_media_even_with_opt_in()
    {
        VideoArchiveCandidate queuedCloud = new(
            Guid.NewGuid(), Guid.NewGuid(), "root", "cloud.mp4", 100, Guid.NewGuid());
        VideoArchiveCandidate pendingLocal = new(
            Guid.NewGuid(), Guid.NewGuid(), "root", "local.mp4", 100);
        List<string> calls = [];
        Task<VideoArchiveCandidate?> Queued(bool allowOnline)
        {
            calls.Add("queued:" + allowOnline);
            return Task.FromResult<VideoArchiveCandidate?>(
                allowOnline ? queuedCloud : null);
        }
        Task<VideoArchiveCandidate?> Pending(bool allowOnline)
        {
            calls.Add("pending:" + allowOnline);
            return Task.FromResult<VideoArchiveCandidate?>(pendingLocal);
        }

        Assert.Equal(pendingLocal,
            await VideoArchiveCandidateSelector.SelectLocalFirstAsync(
                true, Queued, Pending));
        Assert.Equal(["queued:False", "pending:False"], calls);

        calls.Clear();
        Assert.Equal(queuedCloud,
            await VideoArchiveCandidateSelector.SelectLocalFirstAsync(
                true, Queued,
                allowOnline =>
                {
                    calls.Add("pending:" + allowOnline);
                    return Task.FromResult<VideoArchiveCandidate?>(null);
                }));
        Assert.Equal(["queued:False", "pending:False", "queued:True"], calls);
    }

    [Fact]
    public async Task Narrowed_archive_coverage_rejects_old_jobs_and_prefix_siblings()
    {
        SourceId sourceId = SourceId.New();
        ArchiveCatalogueSource source = new(
            sourceId, "local-folder", @"C:\Archive", DateTimeOffset.UtcNow);
        ArchiveCoverageState broad = new(source, ["2024"]);
        ArchiveCoverageState narrowed = new(source, ["2024/10"]);
        VideoArchiveCandidate old = new(
            Guid.NewGuid(), sourceId.Value, @"C:\Archive",
            "2024/09/old.mp4", 100, Guid.NewGuid());
        VideoArchiveCandidate included = old with { Key = "2024/10/ok.mp4" };
        VideoArchiveCandidate sibling = old with { Key = "2024/100/not-covered.mp4" };
        VideoArchiveCandidate otherSource = included with { SourceId = Guid.NewGuid() };
        VideoArchiveCandidate unsafeKey = included with { Key = "../2024/10/escape.mp4" };
        Task<AssetAvailability> Local(
            VideoArchiveCandidate _, CancellationToken __) =>
            Task.FromResult(AssetAvailability.Local);

        Assert.True(VideoArchiveCandidateSelector.IsIncluded(broad, old));
        Assert.False(VideoArchiveCandidateSelector.IsIncluded(narrowed, old));
        Assert.True(VideoArchiveCandidateSelector.IsIncluded(narrowed, included));
        Assert.False(VideoArchiveCandidateSelector.IsIncluded(narrowed, sibling));
        Assert.False(VideoArchiveCandidateSelector.IsIncluded(narrowed, otherSource));
        Assert.False(VideoArchiveCandidateSelector.IsIncluded(narrowed, unsafeKey));
        Assert.Equal(included, await VideoArchiveCandidateSelector.SelectAsync(
            narrowed, [old, sibling, otherSource, unsafeKey, included],
            false, Local));
    }

    [Fact]
    public void Shared_video_worker_uses_one_bounded_job()
    {
        var job = VideoArchiveProcessingService.JobBudget;
        var tool = VideoArchiveProcessingService.ToolBudget;
        job.Validate();
        tool.Validate();
        Assert.Equal(1, job.MaximumConcurrentJobs);
        Assert.Equal(1, tool.WindowsJobLimits!.MaximumProcesses);
        Assert.True(tool.MaximumTemporaryBytes <= 256L * 1024 * 1024);
        Assert.False(new VideoProcessingPreferences(false, false).IncludeOnlineOnly);
    }
}
