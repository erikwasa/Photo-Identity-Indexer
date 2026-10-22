using PhotoIdentity.Api;
using PhotoIdentity.Core.Processing;
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
