using System.Text.Json;

namespace PhotoIdentity.Web;

public sealed record SlideshowSettings(
    bool Autoplay,
    int ImageDurationSeconds,
    bool ShowTimerProgress,
    bool ManualNavigation,
    string Orientation,
    string AfterLastPhoto,
    bool ProtectedSlideshow,
    bool PrepareOriginals)
{
    public const string StorageKey = "photoidentity.slideshow.settings.v1";
    public const string CurrentOrientation = "current";
    public const string PortraitOrientation = "portrait";
    public const string LandscapeOrientation = "landscape";
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
        ManualNavigation: true,
        Orientation: CurrentOrientation,
        AfterLastPhoto: Loop,
        ProtectedSlideshow: true,
        PrepareOriginals: false);

    public SlideshowSettings Normalize()
    {
        int duration = ImageDurationSeconds is >= MinimumImageDurationSeconds and <= MaximumImageDurationSeconds
            ? ImageDurationSeconds
            : DefaultImageDurationSeconds;
        string endBehavior = NormalizeEndBehavior(AfterLastPhoto);
        string orientation = NormalizeOrientation(Orientation);
        return this with
        {
            ImageDurationSeconds = duration,
            Orientation = orientation,
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
                persisted.ManualNavigation ?? Defaults.ManualNavigation,
                NormalizeOrientation(persisted.Orientation),
                NormalizeEndBehavior(persisted.AfterLastPhoto),
                persisted.ProtectedSlideshow ?? Defaults.ProtectedSlideshow,
                persisted.PrepareOriginals ?? Defaults.PrepareOriginals);
        }
        catch (JsonException)
        {
            return Defaults;
        }
    }

    private static string NormalizeOrientation(string? value)
    {
        string normalized = value?.Trim().ToLowerInvariant() ?? CurrentOrientation;
        return normalized switch
        {
            CurrentOrientation => CurrentOrientation,
            PortraitOrientation => PortraitOrientation,
            LandscapeOrientation => LandscapeOrientation,
            _ => CurrentOrientation,
        };
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
        bool? ManualNavigation = null,
        string? Orientation = null,
        string? AfterLastPhoto = null,
        bool? ProtectedSlideshow = null,
        bool? PrepareOriginals = null);
}
