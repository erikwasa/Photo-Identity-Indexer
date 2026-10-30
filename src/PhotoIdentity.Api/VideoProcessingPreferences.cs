using System.Text.Json;
using System.Globalization;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Cli;
using Microsoft.Extensions.Configuration;

namespace PhotoIdentity.Api;

/// <summary>
/// Optional user preference overrides for the registered-byte publication
/// fence. Null/null inherits the existing launcher/application configuration.
/// Neither an inherited nor a user-configured quota enables auto eviction.
/// </summary>
public sealed record VideoProcessingPreferences(
    bool Enabled, bool IncludeOnlineOnly,
    int? MaximumCacheMiB = null, int? MaximumProxyMiB = null,
    string EncodingProfile = VideoProxyEncodingProfiles.StandardKey,
    int RetentionAgeHours = 48,
    bool StopAfterOneVideoStepPerAdvance = false)
{
    public void Validate()
    {
        _ = VideoProxyEncodingProfiles.Resolve(EncodingProfile);
        if (RetentionAgeHours is not (48 or 168 or 720))
            throw new ArgumentException(
                "Video retention age must be 48 hours, 7 days or 30 days.");
        // Must reject partially persisted or caller-forged preferences;
        // there is never a silent "one limit disabled" interpretation.
        _ = VideoProxyPublicationQuotaConfiguration.ParseOptional(
            MaximumCacheMiB?.ToString(CultureInfo.InvariantCulture),
            MaximumProxyMiB?.ToString(CultureInfo.InvariantCulture));
    }

    public VideoProxyRetentionLimits? ResolvePublicationQuota(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Validate();
        if (MaximumCacheMiB.HasValue)
            return VideoProxyPublicationQuotaConfiguration.ParseOptional(
                MaximumCacheMiB.Value.ToString(CultureInfo.InvariantCulture),
                MaximumProxyMiB!.Value.ToString(CultureInfo.InvariantCulture));
        return VideoProxyPublicationQuotaConfiguration.ParseOptional(
            configuration["PhotoIdentity:VideoProcessing:MaximumCacheMiB"],
            configuration["PhotoIdentity:VideoProcessing:MaximumProxyMiB"]);
    }
}

/// <summary>
/// Persist only non-sensitive operator preferences separately from the
/// catalogue. Invalid/unreadable files fail closed; the Settings UI updates
/// the active snapshot without a host restart.
/// </summary>
public sealed class VideoProcessingPreferencesStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VideoProcessingPreferences _current;

    public VideoProcessingPreferences Current => Volatile.Read(ref _current);

    public VideoProcessingPreferencesStore(IConfiguration configuration)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoIdentity", "video-processing-preferences.json"),
            new VideoProcessingPreferences(
                configuration.GetValue<bool>("PhotoIdentity:VideoProcessing:Enabled"),
                configuration.GetValue<bool>("PhotoIdentity:VideoProcessing:IncludeOnlineOnly")))
    {
    }

    public VideoProcessingPreferencesStore(string path, VideoProcessingPreferences defaults)
    {
        defaults.Validate();
        _path = Path.GetFullPath(path);
        _current = LoadOrDisable(_path, defaults);
    }

    public async Task<VideoProcessingPreferences> SaveAsync(
        VideoProcessingPreferences requested, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(requested);
        requested.Validate();
        await _gate.WaitAsync(token);
        try
        {
            string directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp,
                    JsonSerializer.Serialize(requested), token);
                File.Move(temp, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            Volatile.Write(ref _current, requested);
            return requested;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static VideoProcessingPreferences LoadOrDisable(
        string path, VideoProcessingPreferences defaults)
    {
        if (!File.Exists(path)) return defaults;
        try
        {
            var value = JsonSerializer.Deserialize<VideoProcessingPreferences>(
                File.ReadAllText(path));
            if (value is null) return new(false, false);
            value.Validate();
            return value;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
                or JsonException or NotSupportedException
                or ArgumentException)
        {
            return new(false, false);
        }
    }
}
