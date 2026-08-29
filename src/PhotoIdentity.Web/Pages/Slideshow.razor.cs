using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web.Pages;

public partial class Slideshow : IAsyncDisposable
{
    private const int PrefetchWindow = 1;
    private const double SwipeThresholdPixels = 50;
    private const double TapMovementTolerancePixels = 12;

    private readonly SlideshowPlaybackState Playback = new();
    private readonly CancellationTokenSource _timerCancellation = new();

    private DotNetObjectReference<Slideshow>? _dotNetReference;
    private Task? _timerTask;
    private long _lastTickTimestamp;
    private long? _pointerId;
    private double _pointerStartX;
    private double _pointerStartY;
    private bool _suppressNextClick;
    private bool _resumeAfterFullscreenRecovery;

    [Inject]
    public HttpClient Http { get; set; } = default!;

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public Guid CollectionId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "return")]
    public string? ReturnUrl { get; set; }

    private SlideshowSettings Settings { get; set; } = SlideshowSettings.Defaults;
    private SmartCollectionSlideshowSnapshotResponse? Snapshot { get; set; }
    private bool Preparing { get; set; } = true;
    private bool FullscreenActive { get; set; }
    private bool SettingsOpen { get; set; }
    private string? Error { get; set; }
    private string? ImageError { get; set; }

    private string CollectionLabel => Snapshot?.CollectionName ?? "the saved collection";
    private string PageTitleText => Snapshot is null
        ? "Slideshow · Photo Identity"
        : $"{Snapshot.CollectionName} · Slideshow · Photo Identity";
    private string SlideshowAriaLabel => Snapshot is null
        ? "Photo slideshow"
        : $"{Snapshot.CollectionName} slideshow";
    private string? CurrentImageUrl => Playback.CurrentRevisionId is string revisionId
        ? ViewerPreviewUrl(revisionId)
        : null;
    private bool ShowTimerProgress =>
        FullscreenActive &&
        Playback.IsPlaying &&
        Playback.IsImageReady &&
        Settings.ShowTimerProgress &&
        Snapshot?.Total > 0;
    private string ProgressStyle => string.Create(
        CultureInfo.InvariantCulture,
        $"width: {Playback.ProgressFraction * 100d:F1}%");

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _dotNetReference = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("photoIdentitySlideshow.register", _dotNetReference);
        FullscreenActive = await JS.InvokeAsync<bool>("photoIdentitySlideshow.isFullscreen");

        string? storedSettings = null;
        try
        {
            storedSettings = await JS.InvokeAsync<string?>("localStorage.getItem", SlideshowSettings.StorageKey);
        }
        catch (JSException)
        {
            // Browser-local persistence is best effort; defaults keep slideshow startup usable.
        }

        Settings = SlideshowSettings.FromJson(storedSettings);
        await InvokeAsync(StateHasChanged);

        await LoadSnapshotAsync();

        _lastTickTimestamp = Stopwatch.GetTimestamp();
        _timerTask = RunTimerAsync(_timerCancellation.Token);
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadSnapshotAsync()
    {
        Preparing = true;
        Error = null;
        try
        {
            using HttpResponseMessage response = await Http.PostAsync(
                $"api/smart-collections/{CollectionId:D}/slideshow-snapshot",
                content: null);
            if (!response.IsSuccessStatusCode)
            {
                Error = response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "The saved Smart Collection no longer exists."
                    : $"The slideshow snapshot could not be created. Status {(int)response.StatusCode}.";
                return;
            }

            Snapshot = await response.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>()
                ?? throw new InvalidOperationException("The slideshow snapshot response was empty.");
            Playback.LoadSnapshot(
                Snapshot.Items.Select(item => item.RevisionId),
                Settings);

            if (!FullscreenActive && Playback.IsPlaying)
            {
                _resumeAfterFullscreenRecovery = true;
                Playback.Pause();
            }

            await UpdatePrefetchAsync();
        }
        catch (Exception exception)
        {
            Error = $"The slideshow could not be prepared: {exception.Message}";
        }
        finally
        {
            Preparing = false;
        }
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                long now = Stopwatch.GetTimestamp();
                TimeSpan elapsed = Stopwatch.GetElapsedTime(_lastTickTimestamp, now);
                _lastTickTimestamp = now;

                if (!FullscreenActive)
                {
                    continue;
                }

                SlideshowAdvanceResult result = Playback.AdvanceTime(elapsed);
                if (result != SlideshowAdvanceResult.None)
                {
                    await InvokeAsync(() => HandleAdvanceResultAsync(result));
                }
                else if (ShowTimerProgress)
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task OnCurrentImageLoadedAsync()
    {
        ImageError = null;
        Playback.MarkCurrentImageReady();
        _lastTickTimestamp = Stopwatch.GetTimestamp();
        await UpdatePrefetchAsync();
    }

    private void OnCurrentImageError()
    {
        ImageError = "This photo could not be displayed from the available local/proxy viewer source.";
        Playback.MarkCurrentImageUnavailable();
    }

    private async Task OnSurfaceClickAsync()
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        await HandleAdvanceResultAsync(Playback.NextManual());
    }

    private void OnPointerDown(PointerEventArgs args)
    {
        if (!args.IsPrimary || _pointerId is not null)
        {
            return;
        }

        _pointerId = args.PointerId;
        _pointerStartX = args.ClientX;
        _pointerStartY = args.ClientY;
    }

    private async Task OnPointerUpAsync(PointerEventArgs args)
    {
        if (_pointerId != args.PointerId)
        {
            return;
        }

        double deltaX = args.ClientX - _pointerStartX;
        double deltaY = args.ClientY - _pointerStartY;
        _pointerId = null;

        bool horizontalSwipe =
            Math.Abs(deltaX) >= SwipeThresholdPixels &&
            Math.Abs(deltaX) > Math.Abs(deltaY) * 1.2;

        if (horizontalSwipe)
        {
            _suppressNextClick = true;
            SlideshowAdvanceResult result = deltaX < 0
                ? Playback.NextManual()
                : Playback.PreviousManual();
            await HandleAdvanceResultAsync(result);
            return;
        }

        if (Math.Abs(deltaX) > TapMovementTolerancePixels ||
            Math.Abs(deltaY) > TapMovementTolerancePixels)
        {
            _suppressNextClick = true;
        }
    }

    private void OnPointerCancel(PointerEventArgs args)
    {
        if (_pointerId == args.PointerId)
        {
            _pointerId = null;
            _suppressNextClick = true;
        }
    }

    [JSInvokable]
    public async Task OnSlideshowKey(string key)
    {
        switch (key)
        {
            case "ArrowLeft":
                await HandleAdvanceResultAsync(Playback.PreviousManual());
                break;
            case "ArrowRight":
                await HandleAdvanceResultAsync(Playback.NextManual());
                break;
            case " ":
                TogglePlay();
                break;
        }
    }

    [JSInvokable]
    public Task OnDocumentVisibilityChanged(bool visible)
    {
        Playback.SetDocumentVisible(visible);
        _lastTickTimestamp = Stopwatch.GetTimestamp();
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnFullscreenChanged(bool fullscreen)
    {
        FullscreenActive = fullscreen;
        _lastTickTimestamp = Stopwatch.GetTimestamp();

        if (!fullscreen)
        {
            _resumeAfterFullscreenRecovery = Playback.IsPlaying;
            Playback.Pause();
        }
        else if (_resumeAfterFullscreenRecovery)
        {
            _resumeAfterFullscreenRecovery = false;
            Playback.Resume();
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task RetryFullscreenAsync()
    {
        bool entered = await JS.InvokeAsync<bool>("photoIdentitySlideshow.requestFullscreen");
        FullscreenActive = entered;
        _lastTickTimestamp = Stopwatch.GetTimestamp();

        if (entered && _resumeAfterFullscreenRecovery)
        {
            _resumeAfterFullscreenRecovery = false;
            Playback.Resume();
        }
    }

    private void TogglePlay()
    {
        Playback.TogglePlay();
        _lastTickTimestamp = Stopwatch.GetTimestamp();
    }

    private void ToggleSettings() => SettingsOpen = !SettingsOpen;

    private async Task ChangeAutoplayAsync(ChangeEventArgs args)
    {
        bool value = ParseChecked(args);
        await ApplyAndPersistSettingsAsync(Settings with { Autoplay = value });
    }

    private async Task ChangeDurationAsync(ChangeEventArgs args)
    {
        int value = int.TryParse(args.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : SlideshowSettings.DefaultImageDurationSeconds;
        await ApplyAndPersistSettingsAsync(Settings with { ImageDurationSeconds = value });
    }

    private async Task ChangeProgressAsync(ChangeEventArgs args) =>
        await ApplyAndPersistSettingsAsync(Settings with { ShowTimerProgress = ParseChecked(args) });

    private async Task ChangeEndBehaviorAsync(ChangeEventArgs args) =>
        await ApplyAndPersistSettingsAsync(Settings with { AfterLastPhoto = args.Value?.ToString() ?? SlideshowSettings.Loop });

    private async Task ChangeProtectedAsync(ChangeEventArgs args) =>
        await ApplyAndPersistSettingsAsync(Settings with { ProtectedSlideshow = ParseChecked(args) });

    private async Task ChangePrepareOriginalsAsync(ChangeEventArgs args) =>
        await ApplyAndPersistSettingsAsync(Settings with { PrepareOriginals = ParseChecked(args) });

    private async Task ApplyAndPersistSettingsAsync(SlideshowSettings settings)
    {
        Settings = settings.Normalize();
        Playback.ApplySettings(Settings);
        _lastTickTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", SlideshowSettings.StorageKey, Settings.ToJson());
        }
        catch (JSException)
        {
            // Active settings still apply even if browser-local persistence is unavailable.
        }

        await UpdatePrefetchAsync();
    }

    private async Task HandleAdvanceResultAsync(SlideshowAdvanceResult result)
    {
        switch (result)
        {
            case SlideshowAdvanceResult.Moved:
                ImageError = null;
                _lastTickTimestamp = Stopwatch.GetTimestamp();
                await UpdatePrefetchAsync();
                StateHasChanged();
                break;
            case SlideshowAdvanceResult.ExitRequested:
                await ExitSlideshowAsync();
                break;
            default:
                _lastTickTimestamp = Stopwatch.GetTimestamp();
                StateHasChanged();
                break;
        }
    }

    private async Task UpdatePrefetchAsync()
    {
        string[] urls = Playback
            .GetPrefetchRevisionIds(PrefetchWindow)
            .Select(ViewerPreviewUrl)
            .ToArray();
        await JS.InvokeVoidAsync("photoIdentitySlideshow.setPrefetchUrls", (object)urls);
    }

    private async Task ExitSlideshowAsync()
    {
        Playback.Pause();
        await JS.InvokeVoidAsync("photoIdentitySlideshow.exitFullscreen");
        Navigation.NavigateTo(NormalizeReturnUrl(ReturnUrl), replace: true);
    }

    private static string ViewerPreviewUrl(string revisionId) =>
        $"/api/collections/photos/{Uri.EscapeDataString(revisionId)}/viewer-preview";

    private static bool ParseChecked(ChangeEventArgs args) => args.Value switch
    {
        bool value => value,
        string text when bool.TryParse(text, out bool value) => value,
        _ => false,
    };

    public static string NormalizeReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "/smart-collections";
        }

        string trimmed = value.Trim();
        if (trimmed.Equals("/smart-collections", StringComparison.Ordinal) ||
            trimmed.StartsWith("/smart-collections?", StringComparison.Ordinal) ||
            trimmed.StartsWith("/smart-collections#", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return "/smart-collections";
    }

    public async ValueTask DisposeAsync()
    {
        _timerCancellation.Cancel();
        if (_timerTask is not null)
        {
            try
            {
                await _timerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        try
        {
            await JS.InvokeVoidAsync("photoIdentitySlideshow.unregister");
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }

        _dotNetReference?.Dispose();
        _timerCancellation.Dispose();
    }
}
