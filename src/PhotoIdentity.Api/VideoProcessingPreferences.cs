using System.Text.Json;

namespace PhotoIdentity.Api;

/// <summary>
/// Persist only non-sensitive operator preferences, separately from the
/// catalogue. Fail closed when the preferences file is invalid/unreadable.
/// The Settings UI updates the in-memory snapshot without a host restart.
/// </summary>
public sealed record VideoProcessingPreferences(bool Enabled, bool IncludeOnlineOnly);

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
        _path = Path.GetFullPath(path);
        _current = LoadOrDisable(_path, defaults);
    }

    public async Task<VideoProcessingPreferences> SaveAsync(
        VideoProcessingPreferences requested, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(requested);
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
            return value ?? new(false, false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
                or JsonException or NotSupportedException)
        {
            return new(false, false);
        }
    }
}
