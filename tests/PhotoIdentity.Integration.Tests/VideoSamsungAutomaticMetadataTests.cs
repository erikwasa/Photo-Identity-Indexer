using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Api;
using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class VideoSamsungAutomaticMetadataTests
{
    private static VideoPilotMetadata Media(string? make, string? model) =>
        new("mov,mp4", "h264", "High", "yuv420p", "bt709", "aac",
            1920, 1080, 0, 12, "30/1", true, false)
        {
            CameraMake = make,
            CameraModel = model
        };

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("Samsung", null, true)]
    [InlineData(null, "SM-G736B", true)]
    [InlineData("Samsung", "SM-G736B", false)]
    [InlineData("Apple", "iPhone 15 Pro", false)]
    [InlineData("Apple", null, false)]
    public void Automatic_vendor_probing_only_targets_missing_unclaimed_camera_fields(
        string? make, string? model, bool shouldProbe)
    {
        Assert.Equal(shouldProbe, VideoSamsungAutomaticMetadata.ShouldProbe(Media(make, model)));
    }

    [Fact]
    public void Verified_vendor_model_never_replaces_conflicting_existing_identity()
    {
        SamsungVideoCameraMetadata samsung = new("Samsung", "SM-G736B");
        Assert.True(VideoSamsungAutomaticMetadata.Compatible(Media(null, null), samsung));
        Assert.True(VideoSamsungAutomaticMetadata.Compatible(Media("Samsung", null), samsung));
        Assert.True(VideoSamsungAutomaticMetadata.Compatible(Media(null, "SM-G736B"), samsung));
        Assert.False(VideoSamsungAutomaticMetadata.Compatible(Media("Apple", null), samsung));
        Assert.False(VideoSamsungAutomaticMetadata.Compatible(Media(null, "Other model"), samsung));
        Assert.Equal("wi0212-exiftool-samsung-camera-v1",
            VideoSamsungAutomaticMetadata.InspectionProfile);
    }

    [Fact]
    public async Task Vendor_binary_pin_matches_physical_bytes_and_rejects_tampering()
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "photoidentity-samsung-approval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string tool = Path.Combine(dir, "exiftool.exe");
        try
        {
            await File.WriteAllTextAsync(tool, "approved fixture bytes");
            string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("approved fixture bytes")))
                    .ToLowerInvariant();
            VideoSamsungAutomaticMetadata approved = new(tool, digest);
            await approved.VerifyToolAsync();

            await File.WriteAllTextAsync(tool, "changed fixture bytes");
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => approved.VerifyToolAsync());
            Assert.Throws<ArgumentException>(
                () => new VideoSamsungAutomaticMetadata(tool, "bad"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Samsung_setting_defaults_off_persists_and_legacy_json_remains_off()
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "photoidentity-samsung-preference-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string preferencesPath = Path.Combine(dir, "video-processing-preferences.json");
        try
        {
            VideoProcessingPreferencesStore initial = new(preferencesPath, new(false, false));
            Assert.False(initial.Current.ExtractSamsungMetadata);
            await initial.SaveAsync(initial.Current with
            {
                Enabled = true,
                ExtractSamsungMetadata = true
            });
            VideoProcessingPreferencesStore restarted = new(preferencesPath, new(false, false));
            Assert.True(restarted.Current.ExtractSamsungMetadata);
            Assert.True(restarted.Current.Enabled);
            await File.WriteAllTextAsync(preferencesPath,
                JsonSerializer.Serialize(new { Enabled = true, IncludeOnlineOnly = false }));
            VideoProcessingPreferencesStore legacy = new(preferencesPath, new(false, false));
            Assert.False(legacy.Current.ExtractSamsungMetadata);
            Assert.True(legacy.Current.Enabled);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Settings_updates_preserve_prior_toggle_for_old_clients_and_quota_reset()
    {
        VideoProcessingPreferences existing = new(
            Enabled: true, IncludeOnlineOnly: false,
            MaximumCacheMiB: 1024, MaximumProxyMiB: 256,
            ExtractSamsungMetadata: true);
        VideoProcessingPreferences retained =
            new VideoProcessingSettingsUpdate(true, false).MergeWith(existing);
        Assert.True(retained.ExtractSamsungMetadata);
        Assert.Equal(1024, retained.MaximumCacheMiB);
        VideoProcessingPreferences off =
            new VideoProcessingSettingsUpdate(true, false,
                ExtractSamsungMetadata: false).MergeWith(existing);
        Assert.False(off.ExtractSamsungMetadata);
        VideoProcessingPreferences reset =
            new VideoProcessingSettingsUpdate(true, false,
                OverrideProxyLimits: false,
                ExtractSamsungMetadata: true).MergeWith(existing);
        Assert.Null(reset.MaximumCacheMiB);
        Assert.Null(reset.MaximumProxyMiB);
        Assert.True(reset.ExtractSamsungMetadata);
    }
}
