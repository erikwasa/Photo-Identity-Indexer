using System.Text.Json;

namespace PhotoIdentity.Web;

public sealed record SlideshowSettings(
    bool Autoplay,
    int ImageDurationSeconds,
    bool ShowTimerProgress,
    string AfterLastPhoto,
    bool ProtectedSlideshow,
    bool PrepareOriginals)
{
    public const string StorageKey = "photoidentity.slideshow.settings.v1";
    public const string Loop = "loop";
    public const string Stop = "stop";
    public const string Exit = "exit";
    public const int DefaultImageDurationSeconds = 5;
    public const int MinimumImageDurationSeconds = 1;
    public const int MaximumImageDurationSeconds = 60;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SlideshowSettings Defaults { get; } = new(
        Autoplay: true,
        ImageDurationSeconds: DefaultImageDurationSeconds,
        ShowTimerProgress: true,
        AfterLastPhoto: Loop,
        ProtectedSlideshow: true,
        PrepareOriginals: false);

    public SlideshowSettings Normalize()
    {
        int duration = ImageDurationSeconds is >= MinimumImageDurationSeconds and <= MaximumImageDurationSeconds
            ? ImageDurationSeconds
            : DefaultImageDurationSeconds;
        string endBehavior = NormalizeEndBehavior(AfterLastPhoto);
        return this with
        {
            ImageDurationSeconds = duration,
            AfterLastPhoto = endBehavior,
        };
    }

    public string ToJson() => JsonSerializer.Serialize(Normalize(), JsonOptions);

    public static SlideshowSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Defaults;
        }

        try
        {
            PersistedSlideshowSettings? persisted =
                JsonSerializer.Deserialize<PersistedSlideshowSettings>(json, JsonOptions);
            if (persisted is null)
            {
                return Defaults;
            }

            int duration = persisted.ImageDurationSeconds is >= MinimumImageDurationSeconds and <= MaximumImageDurationSeconds
                ? persisted.ImageDurationSeconds.Value
                : DefaultImageDurationSeconds;

            return new SlideshowSettings(
                persisted.Autoplay ?? Defaults.Autoplay,
                duration,
                persisted.ShowTimerProgress ?? Defaults.ShowTimerProgress,
                NormalizeEndBehavior(persisted.AfterLastPhoto),
                persisted.ProtectedSlideshow ?? Defaults.ProtectedSlideshow,
                persisted.PrepareOriginals ?? Defaults.PrepareOriginals);
        }
        catch (JsonException)
        {
            return Defaults;
        }
    }

    private static string NormalizeEndBehavior(string? value)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? Loop;
        return normalized switch
        {
            Loop => Loop,
            Stop => Stop,
            Exit => Exit,
            _ => Loop,
        };
    }

    private sealed record PersistedSlideshowSettings(
        bool? Autoplay = null,
        int? ImageDurationSeconds = null,
        bool? ShowTimerProgress = null,
        string? AfterLastPhoto = null,
        bool? ProtectedSlideshow = null,
        bool? PrepareOriginals = null);
}
