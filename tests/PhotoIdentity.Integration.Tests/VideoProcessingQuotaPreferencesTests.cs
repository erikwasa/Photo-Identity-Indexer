using Microsoft.Extensions.Configuration;
using PhotoIdentity.Api;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoProcessingQuotaPreferencesTests
{
    private static IConfiguration Settings(string? cache = null, string? proxy = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PhotoIdentity:VideoProcessing:MaximumCacheMiB"] = cache,
                ["PhotoIdentity:VideoProcessing:MaximumProxyMiB"] = proxy
            }).Build();

    [Fact]
    public async Task Persisted_quota_overrides_survive_restart_and_can_be_cleared()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-quota-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "preferences.json");
        try
        {
            // Existing pre-WI-0189 preference JSON deserializes as inherited limits.
            File.WriteAllText(path, """
                {"Enabled":false,"IncludeOnlineOnly":false}
                """);
            var store = new VideoProcessingPreferencesStore(path, new(false, false));
            Assert.Null(store.Current.MaximumCacheMiB);
            Assert.Null(store.Current.MaximumProxyMiB);
            Assert.Null(store.Current.ResolvePublicationQuota(Settings()));

            VideoProcessingPreferences chosen =
                await store.SaveAsync(new(false, false, 2048, 128));
            Assert.Equal(2048L * 1024 * 1024,
                chosen.ResolvePublicationQuota(Settings("512", "64"))!.MaximumCacheBytes);
            Assert.Equal(128L * 1024 * 1024,
                chosen.ResolvePublicationQuota(Settings("512", "64"))!.MaximumProxyBytes);

            var restarted = new VideoProcessingPreferencesStore(path, new(false, false));
            Assert.Equal(2048, restarted.Current.MaximumCacheMiB);
            Assert.Equal(128, restarted.Current.MaximumProxyMiB);
            Assert.False(restarted.Current.Enabled);
            Assert.False(restarted.Current.IncludeOnlineOnly);

            await restarted.SaveAsync(new(false, false));
            var restored = new VideoProcessingPreferencesStore(path, new(false, false));
            Assert.Null(restored.Current.MaximumCacheMiB);
            Assert.Equal(512L * 1024 * 1024,
                restored.Current.ResolvePublicationQuota(Settings("512", "64"))!.MaximumCacheBytes);
            Assert.Null(restored.Current.ResolvePublicationQuota(Settings()));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, 12)]
    [InlineData(128, null)]
    [InlineData(63, 1)]
    [InlineData(16385, 1)]
    [InlineData(1024, 257)]
    [InlineData(1024, 0)]
    public async Task Invalid_or_partial_quotas_never_mutate_persisted_preferences(
        int? cache, int? proxy)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-quota-refused-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "preferences.json");
        try
        {
            var store = new VideoProcessingPreferencesStore(path, new(false, false));
            await store.SaveAsync(new(false, false, 1024, 128));
            string original = File.ReadAllText(path);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.SaveAsync(new(true, true, cache, proxy)));
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Equal(1024, store.Current.MaximumCacheMiB);
            Assert.False(store.Current.Enabled);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Corrupted_persisted_quota_disables_processing_and_cloud_consent()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-video-corrupt-quota-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "preferences.json");
        try
        {
            File.WriteAllText(path, """
                {"Enabled":true,"IncludeOnlineOnly":true,
                 "MaximumCacheMiB":128,"MaximumProxyMiB":256}
                """);
            var store = new VideoProcessingPreferencesStore(path, new(false, false));
            Assert.False(store.Current.Enabled);
            Assert.False(store.Current.IncludeOnlineOnly);
            Assert.Null(store.Current.MaximumCacheMiB);
            Assert.Null(store.Current.MaximumProxyMiB);

            File.WriteAllText(path, """
                {"Enabled":true,"IncludeOnlineOnly":true,"MaximumCacheMiB":1024}
                """);
            var partial = new VideoProcessingPreferencesStore(path, new(false, false));
            Assert.False(partial.Current.Enabled);
            Assert.False(partial.Current.IncludeOnlineOnly);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Legacy_bool_only_API_update_preserves_existing_quota_overrides()
    {
        VideoProcessingPreferences existing = new(false, false, 1024, 128);
        VideoProcessingPreferences updated =
            new VideoProcessingSettingsUpdate(true, false).MergeWith(existing);
        Assert.True(updated.Enabled);
        Assert.Equal(1024, updated.MaximumCacheMiB);
        Assert.Equal(128, updated.MaximumProxyMiB);
    }

    [Fact]
    public void Only_explicit_override_reset_clears_saved_quota_limits()
    {
        VideoProcessingPreferences existing = new(false, false, 1024, 128);
        VideoProcessingPreferences cleared =
            new VideoProcessingSettingsUpdate(false, false,
                OverrideProxyLimits: false).MergeWith(existing);
        Assert.Null(cleared.MaximumCacheMiB);
        Assert.Null(cleared.MaximumProxyMiB);
        VideoProcessingPreferences changed =
            new VideoProcessingSettingsUpdate(false, false, 2048, 64,
                OverrideProxyLimits: true).MergeWith(existing);
        Assert.Equal(2048, changed.MaximumCacheMiB);
        Assert.Equal(64, changed.MaximumProxyMiB);
    }

    [Fact]
    public void Ambiguous_API_quota_updates_fail_without_modifying_preferences()
    {
        VideoProcessingPreferences existing = new(false, false, 1024, 128);
        Assert.Throws<ArgumentException>(() =>
            new VideoProcessingSettingsUpdate(false, false, 1024, 128)
                .MergeWith(existing));
        Assert.Throws<ArgumentException>(() =>
            new VideoProcessingSettingsUpdate(false, false, 1024, 128,
                OverrideProxyLimits: false).MergeWith(existing));
        Assert.Throws<ArgumentException>(() =>
            new VideoProcessingSettingsUpdate(false, false,
                OverrideProxyLimits: true).MergeWith(existing));
        Assert.Equal(1024, existing.MaximumCacheMiB);
    }

    [Fact]
    public void Inherited_invalid_configuration_rejects_enabling_but_valid_override_wins()
    {
        var defaultSettings = new VideoProcessingPreferences(false, false);
        Assert.Throws<ArgumentException>(() =>
            defaultSettings.ResolvePublicationQuota(Settings("1024", null)));
        var userSettings = new VideoProcessingPreferences(false, false, 1024, 128);
        Assert.Equal(1024L * 1024 * 1024,
            userSettings.ResolvePublicationQuota(Settings("bad", null))!.MaximumCacheBytes);
    }
}
