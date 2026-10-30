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
    public async Task Selected_video_profile_persists_across_restart_and_legacy_JSON_defaults_to_720p()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-profile-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "video-processing-preferences.json");
        try
        {
            File.WriteAllText(file,
                """{"Enabled":false,"IncludeOnlineOnly":false}""");
            var store = new VideoProcessingPreferencesStore(file, new(false, false));
            Assert.Equal("standard-720p", store.Current.EncodingProfile);
            Assert.False(store.Current.Enabled);

            await store.SaveAsync(new(false, false,
                EncodingProfile: "compact-540p"));
            var reloaded = new VideoProcessingPreferencesStore(file, new(false, false));
            Assert.Equal("compact-540p", reloaded.Current.EncodingProfile);
            Assert.False(reloaded.Current.Enabled);

            await reloaded.SaveAsync(new(false, false,
                EncodingProfile: "standard-720p"));
            Assert.Equal("standard-720p",
                new VideoProcessingPreferencesStore(file, new(false, false))
                    .Current.EncodingProfile);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("arbitrary-432p")]
    [InlineData("")]
    [InlineData("Compact-540p")]
    public async Task Malformed_or_unknown_video_profile_is_rejected_before_persisting(
        string profile)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-profile-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "video-processing-preferences.json");
        try
        {
            var store = new VideoProcessingPreferencesStore(file, new(false, false));
            await store.SaveAsync(new(false, false));
            string original = File.ReadAllText(file);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.SaveAsync(new(true, true, EncodingProfile: profile)));
            Assert.Equal(original, File.ReadAllText(file));
            Assert.False(store.Current.Enabled);
            File.WriteAllText(file,
                "{\"Enabled\":true,\"IncludeOnlineOnly\":true,\"EncodingProfile\":"
                + System.Text.Json.JsonSerializer.Serialize(profile) + "}");
            var invalid = new VideoProcessingPreferencesStore(file, new(false, false));
            Assert.False(invalid.Current.Enabled);
            Assert.False(invalid.Current.IncludeOnlineOnly);
            Assert.Equal("standard-720p", invalid.Current.EncodingProfile);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Retention_age_is_persisted_and_old_preferences_default_to_48_hours()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-retention-age-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "video-processing-preferences.json");
        try
        {
            File.WriteAllText(file,
                """{"Enabled":false,"IncludeOnlineOnly":false}""");
            var store = new VideoProcessingPreferencesStore(file, new(false, false));
            Assert.Equal(48, store.Current.RetentionAgeHours);
            await store.SaveAsync(new(false, false,
                RetentionAgeHours: 168));
            Assert.Equal(168,
                new VideoProcessingPreferencesStore(file, new(false, false))
                    .Current.RetentionAgeHours);

            await store.SaveAsync(new(false, false,
                RetentionAgeHours: 720));
            Assert.Equal(720,
                new VideoProcessingPreferencesStore(file, new(false, false))
                    .Current.RetentionAgeHours);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(47)]
    [InlineData(49)]
    [InlineData(169)]
    [InlineData(721)]
    public async Task Unsupported_retention_age_is_rejected_without_mutating_settings(
        int hours)
    {
        string root = Path.Combine(Path.GetTempPath(),
            "photoidentity-retention-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "video-processing-preferences.json");
        try
        {
            var store = new VideoProcessingPreferencesStore(file, new(false, false));
            await store.SaveAsync(new(false, false,
                EncodingProfile: "compact-540p", RetentionAgeHours: 168));
            string original = File.ReadAllText(file);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.SaveAsync(new(true, true, RetentionAgeHours: hours)));
            Assert.Equal(original, File.ReadAllText(file));
            Assert.Equal(168, store.Current.RetentionAgeHours);

            File.WriteAllText(file,
                "{\"Enabled\":true,\"IncludeOnlineOnly\":true,"
                + "\"RetentionAgeHours\":" + hours + "}");
            var invalid = new VideoProcessingPreferencesStore(file, new(false, false));
            Assert.False(invalid.Current.Enabled);
            Assert.False(invalid.Current.IncludeOnlineOnly);
            Assert.Equal(48, invalid.Current.RetentionAgeHours);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Legacy_API_preserves_retention_age_and_explicit_change_updates_only_age()
    {
        var existing = new VideoProcessingPreferences(false, false,
            1024, 128, "compact-540p", 168);
        var legacy = new VideoProcessingSettingsUpdate(true, false)
            .MergeWith(existing);
        Assert.Equal(168, legacy.RetentionAgeHours);
        Assert.Equal("compact-540p", legacy.EncodingProfile);
        var changed = new VideoProcessingSettingsUpdate(
            false, false, RetentionAgeHours: 720)
            .MergeWith(existing);
        changed.Validate();
        Assert.Equal(720, changed.RetentionAgeHours);
        Assert.Equal("compact-540p", changed.EncodingProfile);
        Assert.Equal(1024, changed.MaximumCacheMiB);
        Assert.Equal(128, changed.MaximumProxyMiB);
    }

    [Fact]
    public void Legacy_two_field_settings_API_preserves_profile_and_explicit_profile_change_is_scoped()
    {
        var current = new VideoProcessingPreferences(
            false, false, 1024, 128, "compact-540p");
        var oldApi = new VideoProcessingSettingsUpdate(true, false);
        var oldResult = oldApi.MergeWith(current);
        Assert.Equal("compact-540p", oldResult.EncodingProfile);
        Assert.Equal(1024, oldResult.MaximumCacheMiB);

        var changed = new VideoProcessingSettingsUpdate(
            false, false, EncodingProfile: "standard-720p").MergeWith(current);
        Assert.Equal("standard-720p", changed.EncodingProfile);
        Assert.Equal(1024, changed.MaximumCacheMiB);
        Assert.Equal(128, changed.MaximumProxyMiB);
        var invalid = new VideoProcessingSettingsUpdate(
            false, false, EncodingProfile: "arbitrary");
        Assert.Throws<ArgumentException>(() => invalid.MergeWith(current).Validate());
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
