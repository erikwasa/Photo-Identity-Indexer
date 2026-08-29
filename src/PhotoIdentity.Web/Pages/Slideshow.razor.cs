using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
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
    private readonly SlideshowProtectionState Protection = new();
    private readonly SlideshowNavigationGate _navigationGate = new();
    private readonly CancellationTokenSource _timerCancellation = new();

    private DotNetObjectReference<Slideshow>? _dotNetReference;
    private CancellationTokenSource? _parentUnlockCancellation;
    private CancellationTokenSource? _exitHoldCancellation;
    private CancellationTokenSource? _preparationCancellation;
    private Task? _timerTask;
    private Task? _preparationTask;
    private long _lastTickTimestamp;
    private long? _pointerId;
    private long? _exitPointerId;
    private double _pointerStartX;
    private double _pointerStartY;
    private bool _suppressNextClick;
    private bool _resumeAfterFullscreenRecovery;
    private bool _resumeAfterParentControls;
    private bool _resumeAfterPreparation;
    private bool _preparedOriginalsReady;
    private bool _continueAvailableForSession;
    private bool _deliberateExit;

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
    private SlideshowOriginalPreparationResponse? OriginalPreparation { get; set; }
    private SlideshowBrowserProtectionStatus Capabilities { get; set; } =
        SlideshowBrowserProtectionStatus.Unknown;
    private bool ProtectionStatusKnown { get; set; }
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
        ? PlaybackResourceUrl(revisionId)
        : null;
    private bool PreparingOriginals =>
        OriginalPreparation?.State == "preparing";
    private bool PreparationFailed =>
        OriginalPreparation?.State == "failed";
    private bool ShowTimerProgress =>
        FullscreenActive &&
        !Protection.ParentControlsOpen &&
        Playback.IsPlaying &&
        Playback.IsImageReady &&
        Settings.ShowTimerProgress &&
        Snapshot?.Total > 0;
    private bool ShowAdultToolbar =>
        FullscreenActive &&
        !Preparing &&
        !PreparingOriginals &&
        string.IsNullOrWhiteSpace(Error) &&
        !Settings.ProtectedSlideshow;
    private IReadOnlyList<string> ProtectionWarnings =>
        ProtectionStatusKnown ? Capabilities.ParentWarnings() : [];
    private string ProgressStyle => string.Create(
        CultureInfo.InvariantCulture,
        $"width: {Playback.ProgressFraction * 100d:F1}%");
    private string ProtectionTuningStyle =>
        $"--parent-zone-size: {SlideshowProtectionTuning.ParentZoneSizeCss}; " +
        $"--parent-zone-inset: {SlideshowProtectionTuning.ParentZoneInsetCss};";
    private double ExitHoldSeconds => SlideshowProtectionTuning.ExitHold.TotalSeconds;
    private string ParentControlsHeading => Protection.RecoveryReason switch
    {
        SlideshowRecoveryReason.BrowserBack => "Back navigation was blocked",
        SlideshowRecoveryReason.FullscreenLost => "Fullscreen was lost",
        SlideshowRecoveryReason.ProtectionWarning => "Check phone protection",
        SlideshowRecoveryReason.PreparationFailure => "Best-quality preparation needs attention",
        _ => "Slideshow controls",
    };

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
        Protection.Configure(Settings.ProtectedSlideshow);

        try
        {
            Capabilities = await JS.InvokeAsync<SlideshowBrowserProtectionStatus>(
                "photoIdentitySlideshow.getProtectionStatus");
            ProtectionStatusKnown = true;

            if (FullscreenActive)
            {
                await AcquireProtectionsAsync(showWarning: false);
            }
        }
        catch (JSException)
        {
            ProtectionStatusKnown = false;
        }

        await InvokeAsync(StateHasChanged);
        await LoadSnapshotAsync();
        MaybeOpenProtectionWarning();

        _lastTickTimestamp = Stopwatch.GetTimestamp();
        _timerTask = RunTimerAsync(_timerCancellation.Token);
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadSnapshotAsync()
    {
        Preparing = true;
        Error = null;
        bool beginOriginalPreparation = false;
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

            beginOriginalPreparation = Settings.PrepareOriginals;
            if (beginOriginalPreparation)
            {
                _resumeAfterPreparation =
                    Playback.IsPlaying ||
                    _resumeAfterFullscreenRecovery ||
                    _resumeAfterParentControls;
                Playback.Pause();
            }
            else
            {
                await UpdatePrefetchAsync();
            }
        }
        catch (Exception exception)
        {
            Error = $"The slideshow could not be prepared: {exception.Message}";
        }
        finally
        {
            Preparing = false;
        }

        if (beginOriginalPreparation && Snapshot is not null && Error is null)
        {
            await BeginOriginalPreparationAsync();
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

                if (!FullscreenActive || Protection.ParentControlsOpen)
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
        bool wasPlaying = Playback.IsPlaying;
        Playback.MarkCurrentImageUnavailable();

        if (_preparedOriginalsReady && OriginalPreparation is not null)
        {
            ImageError = "A prepared original became unavailable or failed immutable verification.";
            _preparedOriginalsReady = false;
            OriginalPreparation = OriginalPreparation with
            {
                State = "failed",
                Message = "A prepared original became unavailable or failed immutable verification. Continue with available/proxy images or cancel preparation.",
                CanContinueWithAvailable = true,
            };
            _resumeAfterPreparation =
                wasPlaying ||
                _resumeAfterParentControls ||
                _resumeAfterFullscreenRecovery ||
                Settings.Autoplay;
            Playback.Pause();

            if (Settings.ProtectedSlideshow)
            {
                OpenParentControls(SlideshowRecoveryReason.PreparationFailure);
                _resumeAfterParentControls = _resumeAfterPreparation;
            }

            return;
        }

        ImageError = "This photo could not be displayed from the available local/proxy viewer source.";
    }

    private async Task OnSurfaceClickAsync()
    {
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        if (!CanNavigatePresentation())
        {
            return;
        }

        await RequestNavigationAsync(SlideshowNavigationDirection.Next);
    }

    private void OnPointerDown(PointerEventArgs args)
    {
        if (!CanNavigatePresentation() || !args.IsPrimary || _pointerId is not null)
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
            await RequestNavigationAsync(
                deltaX < 0
                    ? SlideshowNavigationDirection.Next
                    : SlideshowNavigationDirection.Previous);
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

    private bool CanNavigatePresentation() =>
        FullscreenActive &&
        !Preparing &&
        !PreparingOriginals &&
        !PreparationFailed &&
        string.IsNullOrWhiteSpace(Error) &&
        !Protection.ParentControlsOpen;

    private async Task RequestNavigationAsync(SlideshowNavigationDirection direction)
    {
        if (!CanNavigatePresentation() ||
            !_navigationGate.TryStart(direction, out SlideshowNavigationDirection current))
        {
            return;
        }

        try
        {
            while (current != SlideshowNavigationDirection.None)
            {
                SlideshowAdvanceResult result = current == SlideshowNavigationDirection.Next
                    ? Playback.NextManual()
                    : Playback.PreviousManual();

                await HandleAdvanceResultAsync(result);

                if (!_navigationGate.CompleteStep(out current))
                {
                    break;
                }
            }
        }
        finally
        {
            _navigationGate.Reset();
        }
    }

    private void OnParentPointerDown(SlideshowParentZone zone, PointerEventArgs args)
    {
        if (!Settings.ProtectedSlideshow)
        {
            return;
        }

        bool started = Protection.PointerDown(zone, args.PointerId, DateTimeOffset.UtcNow);
        if (!started)
        {
            return;
        }

        CancelParentUnlockTimer();
        _parentUnlockCancellation = new CancellationTokenSource();
        _ = WaitForParentUnlockAsync(_parentUnlockCancellation);
    }

    private void OnParentPointerUp(SlideshowParentZone zone, PointerEventArgs args)
    {
        Protection.PointerUp(zone, args.PointerId);
        CancelParentUnlockTimer();
    }

    private async Task WaitForParentUnlockAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(SlideshowProtectionTuning.ParentUnlockHold, cancellation.Token);
            await InvokeAsync(() =>
            {
                if (cancellation.IsCancellationRequested ||
                    !ReferenceEquals(_parentUnlockCancellation, cancellation) ||
                    !Protection.TryCompleteParentUnlock(DateTimeOffset.UtcNow))
                {
                    return;
                }

                _resumeAfterParentControls = Playback.IsPlaying || _resumeAfterFullscreenRecovery;
                Playback.Pause();
                SettingsOpen = false;
                _lastTickTimestamp = Stopwatch.GetTimestamp();
                StateHasChanged();
            });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelParentUnlockTimer()
    {
        if (_parentUnlockCancellation is null)
        {
            return;
        }

        _parentUnlockCancellation.Cancel();
        _parentUnlockCancellation.Dispose();
        _parentUnlockCancellation = null;
    }

    [JSInvokable]
    public async Task OnSlideshowKey(string key)
    {
        if (Protection.ParentControlsOpen)
        {
            return;
        }

        switch (key)
        {
            case "ArrowLeft":
                await RequestNavigationAsync(SlideshowNavigationDirection.Previous);
                break;
            case "ArrowRight":
                await RequestNavigationAsync(SlideshowNavigationDirection.Next);
                break;
            case " " when FullscreenActive:
                TogglePlay();
                break;
        }
    }

    [JSInvokable]
    public Task OnParentShortcut()
    {
        if (Settings.ProtectedSlideshow)
        {
            OpenParentControls(SlideshowRecoveryReason.ParentUnlocked);
            StateHasChanged();
        }

        return Task.CompletedTask;
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
    public async Task OnFullscreenChanged(bool fullscreen)
    {
        FullscreenActive = fullscreen;
        _lastTickTimestamp = Stopwatch.GetTimestamp();

        if (!fullscreen)
        {
            _resumeAfterFullscreenRecovery =
                Playback.IsPlaying ||
                _resumeAfterParentControls ||
                _resumeAfterFullscreenRecovery;
            Playback.Pause();

            if (Settings.ProtectedSlideshow)
            {
                Protection.ShowRecovery(SlideshowRecoveryReason.FullscreenLost);
                SettingsOpen = false;
            }
        }
        else
        {
            Protection.ClearRecovery();
            await AcquireProtectionsAsync(showWarning: true);

            if (!Protection.ParentControlsOpen && _resumeAfterFullscreenRecovery)
            {
                _resumeAfterFullscreenRecovery = false;
                Playback.Resume();
            }
        }

        StateHasChanged();
    }

    [JSInvokable]
    public Task OnProtectionStatusChanged(SlideshowBrowserProtectionStatus status)
    {
        Capabilities = status;
        ProtectionStatusKnown = true;

        if (Playback.IsDocumentVisible)
        {
            MaybeOpenProtectionWarning();
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task AcquireProtectionsAsync(bool showWarning)
    {
        try
        {
            Capabilities = await JS.InvokeAsync<SlideshowBrowserProtectionStatus>(
                "photoIdentitySlideshow.acquireProtections");
            ProtectionStatusKnown = true;

            if (showWarning)
            {
                MaybeOpenProtectionWarning();
            }
        }
        catch (JSException)
        {
            ProtectionStatusKnown = false;
            if (showWarning && Settings.ProtectedSlideshow)
            {
                OpenParentControls(SlideshowRecoveryReason.ProtectionWarning);
            }
        }
    }

    private void MaybeOpenProtectionWarning()
    {
        if (!Settings.ProtectedSlideshow ||
            !ProtectionStatusKnown ||
            !FullscreenActive ||
            Preparing ||
            Snapshot is null ||
            Protection.ParentControlsOpen ||
            ProtectionWarnings.Count == 0)
        {
            return;
        }

        OpenParentControls(SlideshowRecoveryReason.ProtectionWarning);
    }

    private async Task RetryFullscreenAsync()
    {
        bool entered = await JS.InvokeAsync<bool>("photoIdentitySlideshow.requestFullscreen");
        FullscreenActive = entered;
        _lastTickTimestamp = Stopwatch.GetTimestamp();

        if (!entered)
        {
            return;
        }

        Protection.ClearRecovery();
        await AcquireProtectionsAsync(showWarning: true);

        if (!Protection.ParentControlsOpen && _resumeAfterFullscreenRecovery)
        {
            _resumeAfterFullscreenRecovery = false;
            Playback.Resume();
        }
    }

    private void OnBeforeInternalNavigation(LocationChangingContext context)
    {
        if (!Protection.ShouldPreventNavigation(_deliberateExit))
        {
            return;
        }

        context.PreventNavigation();
        OpenParentControls(SlideshowRecoveryReason.BrowserBack);
    }

    private void OpenParentControls(SlideshowRecoveryReason reason)
    {
        if (!Settings.ProtectedSlideshow)
        {
            return;
        }

        if (!Protection.ParentControlsOpen)
        {
            _resumeAfterParentControls =
                Playback.IsPlaying ||
                _resumeAfterFullscreenRecovery;
        }

        Playback.Pause();
        Protection.OpenParentControls(reason);
        SettingsOpen = false;
        CancelParentUnlockTimer();
        _lastTickTimestamp = Stopwatch.GetTimestamp();
    }

    private void CloseParentControls()
    {
        bool shouldResume = _resumeAfterParentControls;
        Protection.CloseParentControls();
        SettingsOpen = false;
        _resumeAfterParentControls = false;

        if (shouldResume && FullscreenActive)
        {
            _resumeAfterFullscreenRecovery = false;
            Playback.Resume();
        }
        else if (shouldResume)
        {
            _resumeAfterFullscreenRecovery = true;
        }

        _lastTickTimestamp = Stopwatch.GetTimestamp();
    }

    private void ToggleParentPlayback()
    {
        _resumeAfterParentControls = !_resumeAfterParentControls;
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

    private async Task ChangePrepareOriginalsAsync(ChangeEventArgs args)
    {
        bool enabled = ParseChecked(args);
        await ApplyAndPersistSettingsAsync(Settings with { PrepareOriginals = enabled });

        if (Snapshot is null)
        {
            return;
        }

        if (enabled)
        {
            _continueAvailableForSession = false;
            await BeginOriginalPreparationAsync();
        }
        else
        {
            bool shouldResume = _resumeAfterPreparation;
            await EndOriginalPreparationAsync();
            _preparedOriginalsReady = false;
            _continueAvailableForSession = false;
            OriginalPreparation = null;
            ImageError = null;
            await UpdatePrefetchAsync();

            if (shouldResume)
            {
                ResumeAfterPreparationHold();
            }
        }
    }

    private async Task ApplyAndPersistSettingsAsync(SlideshowSettings settings)
    {
        SlideshowSettings previous = Settings;
        bool parentWasOpen = Protection.ParentControlsOpen;
        bool desiredResume = _resumeAfterParentControls;

        Settings = settings.Normalize();
        Playback.ApplySettings(Settings);
        Protection.Configure(Settings.ProtectedSlideshow);

        if (PreparingOriginals && previous.Autoplay != Settings.Autoplay)
        {
            _resumeAfterPreparation = Settings.Autoplay;
            Playback.Pause();
        }

        if (parentWasOpen && Settings.ProtectedSlideshow)
        {
            if (previous.Autoplay != Settings.Autoplay)
            {
                desiredResume = Settings.Autoplay;
            }

            _resumeAfterParentControls = desiredResume;
            Playback.Pause();
        }
        else if (parentWasOpen && !Settings.ProtectedSlideshow)
        {
            _resumeAfterParentControls = false;
            if (desiredResume && FullscreenActive)
            {
                Playback.Resume();
            }
        }

        if (!previous.ProtectedSlideshow && Settings.ProtectedSlideshow)
        {
            SettingsOpen = false;
            if (FullscreenActive)
            {
                await AcquireProtectionsAsync(showWarning: true);
            }
        }

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

    private async Task BeginOriginalPreparationAsync()
    {
        if (Snapshot is null || _continueAvailableForSession)
        {
            return;
        }

        await EndOriginalPreparationAsync();

        _resumeAfterPreparation =
            _resumeAfterPreparation ||
            Playback.IsPlaying ||
            _resumeAfterParentControls ||
            _resumeAfterFullscreenRecovery;
        Playback.Pause();
        _preparedOriginalsReady = false;
        ImageError = null;
        OriginalPreparation = new SlideshowOriginalPreparationResponse(
            string.Empty,
            "preparing",
            0,
            Snapshot.Total,
            0,
            0,
            "Preflighting the complete slideshow against the configured storage policy.",
            false);

        CancellationTokenSource cancellation = new();
        _preparationCancellation = cancellation;
        StateHasChanged();

        try
        {
            SlideshowOriginalPreparationRequest request = new(
                Snapshot.Items.Select(item => item.RevisionId).ToArray());
            using HttpResponseMessage response = await Http.PostAsJsonAsync(
                "api/slideshows/original-preparation",
                request,
                cancellation.Token);

            if (!response.IsSuccessStatusCode)
            {
                SetPreparationFailure(
                    $"Best-quality slideshow preparation could not start. Status {(int)response.StatusCode}.");
                return;
            }

            SlideshowOriginalPreparationResponse status =
                await response.Content.ReadFromJsonAsync<SlideshowOriginalPreparationResponse>(
                    cancellationToken: cancellation.Token)
                ?? throw new InvalidOperationException(
                    "The slideshow original preparation response was empty.");
            OriginalPreparation = status;
            await ApplyPreparationStatusAsync(status);

            if (!Guid.TryParse(status.SessionId, out Guid sessionId) ||
                sessionId == Guid.Empty)
            {
                SetPreparationFailure(
                    "The best-quality slideshow preparation session identifier was invalid.");
                return;
            }

            _preparationTask = MaintainOriginalPreparationAsync(
                sessionId,
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SetPreparationFailure(
                $"Best-quality slideshow preparation could not start: {exception.Message}");
        }
    }

    private async Task MaintainOriginalPreparationAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TimeSpan delay = _preparedOriginalsReady
                    ? TimeSpan.FromMinutes(1)
                    : TimeSpan.FromMilliseconds(500);
                await Task.Delay(delay, cancellationToken);

                using HttpResponseMessage response = await Http.GetAsync(
                    $"api/slideshows/original-preparation/{sessionId:D}",
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    await InvokeAsync(() =>
                    {
                        SetPreparationFailure(
                            "The best-quality slideshow preparation session expired or became unavailable.");
                        return Task.CompletedTask;
                    });
                    return;
                }

                SlideshowOriginalPreparationResponse status =
                    await response.Content.ReadFromJsonAsync<SlideshowOriginalPreparationResponse>(
                        cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException(
                        "The slideshow original preparation status response was empty.");

                await InvokeAsync(() => ApplyPreparationStatusAsync(status));
                if (status.State is "failed" or "cancelled")
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await InvokeAsync(() =>
            {
                SetPreparationFailure(
                    $"Best-quality slideshow preparation status could not be refreshed: {exception.Message}");
                return Task.CompletedTask;
            });
        }
    }

    private async Task ApplyPreparationStatusAsync(
        SlideshowOriginalPreparationResponse status)
    {
        OriginalPreparation = status;

        switch (status.State)
        {
            case "ready":
                if (!_preparedOriginalsReady)
                {
                    _preparedOriginalsReady = true;
                    ImageError = null;
                    await UpdatePrefetchAsync();
                    ResumeAfterPreparationHold();
                }
                break;

            case "failed":
                _preparedOriginalsReady = false;
                Playback.Pause();
                if (Settings.ProtectedSlideshow)
                {
                    OpenParentControls(SlideshowRecoveryReason.PreparationFailure);
                    _resumeAfterParentControls = _resumeAfterPreparation;
                }
                break;

            case "preparing":
                Playback.Pause();
                break;
        }

        StateHasChanged();
    }

    private void SetPreparationFailure(string message)
    {
        int ready = OriginalPreparation?.Ready ?? 0;
        int total = OriginalPreparation?.Total ?? Snapshot?.Total ?? 0;
        long required = OriginalPreparation?.RequiredAdditionalBytes ?? 0;
        long available = OriginalPreparation?.AvailableManagedCapacity ?? 0;
        string sessionId = OriginalPreparation?.SessionId ?? string.Empty;

        OriginalPreparation = new SlideshowOriginalPreparationResponse(
            sessionId,
            "failed",
            ready,
            total,
            required,
            available,
            message,
            true);
        _preparedOriginalsReady = false;
        Playback.Pause();

        if (Settings.ProtectedSlideshow)
        {
            OpenParentControls(SlideshowRecoveryReason.PreparationFailure);
            _resumeAfterParentControls = _resumeAfterPreparation;
        }

        StateHasChanged();
    }

    private void ResumeAfterPreparationHold()
    {
        bool shouldResume = _resumeAfterPreparation;
        _resumeAfterPreparation = false;

        if (!shouldResume)
        {
            return;
        }

        if (Protection.ParentControlsOpen)
        {
            _resumeAfterParentControls = true;
            return;
        }

        if (FullscreenActive)
        {
            _resumeAfterFullscreenRecovery = false;
            Playback.Resume();
        }
        else
        {
            _resumeAfterFullscreenRecovery = true;
        }

        _lastTickTimestamp = Stopwatch.GetTimestamp();
    }

    private async Task ContinueWithAvailableAsync()
    {
        bool shouldResume =
            _resumeAfterPreparation ||
            _resumeAfterParentControls;

        await EndOriginalPreparationAsync();
        _preparedOriginalsReady = false;
        _continueAvailableForSession = true;
        OriginalPreparation = null;
        ImageError = null;
        await UpdatePrefetchAsync();

        _resumeAfterPreparation = false;
        if (Protection.ParentControlsOpen)
        {
            _resumeAfterParentControls = shouldResume;
            CloseParentControls();
        }
        else if (shouldResume && FullscreenActive)
        {
            Playback.Resume();
        }
        else if (shouldResume)
        {
            _resumeAfterFullscreenRecovery = true;
        }

        StateHasChanged();
    }

    private async Task CancelOriginalPreparationAsync()
    {
        await EndOriginalPreparationAsync();
        _preparedOriginalsReady = false;
        _continueAvailableForSession = true;
        _resumeAfterPreparation = false;
        _resumeAfterParentControls = false;
        OriginalPreparation = null;
        ImageError = null;
        Playback.Pause();
        await UpdatePrefetchAsync();
        StateHasChanged();
    }

    private async Task EndOriginalPreparationAsync()
    {
        CancellationTokenSource? cancellation = _preparationCancellation;
        _preparationCancellation = null;
        if (cancellation is not null)
        {
            cancellation.Cancel();
        }

        Guid sessionId = default;
        bool hasSession =
            OriginalPreparation is not null &&
            Guid.TryParse(OriginalPreparation.SessionId, out sessionId) &&
            sessionId != Guid.Empty;

        if (hasSession)
        {
            try
            {
                using HttpResponseMessage _ = await Http.DeleteAsync(
                    $"api/slideshows/original-preparation/{sessionId:D}");
            }
            catch
            {
                // The server-side lease is also expiring, so failed cleanup cannot strand it.
            }
        }

        cancellation?.Dispose();
        _preparationTask = null;
    }

    private void BeginExitHold(PointerEventArgs args)
    {
        CancelExitHoldInternal();
        _exitPointerId = args.PointerId;
        _exitHoldCancellation = new CancellationTokenSource();
        _ = WaitForExitHoldAsync(_exitHoldCancellation, args.PointerId);
    }

    private void CancelExitHold(PointerEventArgs args)
    {
        if (_exitPointerId == args.PointerId)
        {
            CancelExitHoldInternal();
        }
    }

    private async Task WaitForExitHoldAsync(
        CancellationTokenSource cancellation,
        long pointerId)
    {
        try
        {
            await Task.Delay(SlideshowProtectionTuning.ExitHold, cancellation.Token);
            if (cancellation.IsCancellationRequested ||
                !ReferenceEquals(_exitHoldCancellation, cancellation) ||
                _exitPointerId != pointerId ||
                !Protection.ParentControlsOpen)
            {
                return;
            }

            await InvokeAsync(ExitSlideshowAsync);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelExitHoldInternal()
    {
        if (_exitHoldCancellation is not null)
        {
            _exitHoldCancellation.Cancel();
            _exitHoldCancellation.Dispose();
            _exitHoldCancellation = null;
        }

        _exitPointerId = null;
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
            .Select(PlaybackResourceUrl)
            .ToArray();
        await JS.InvokeVoidAsync("photoIdentitySlideshow.setPrefetchUrls", (object)urls);
    }

    private async Task ExitSlideshowAsync()
    {
        _deliberateExit = true;
        Playback.Pause();
        _navigationGate.Reset();
        CancelParentUnlockTimer();
        CancelExitHoldInternal();
        await EndOriginalPreparationAsync();

        try
        {
            await JS.InvokeVoidAsync("photoIdentitySlideshow.releaseProtections");
            await JS.InvokeVoidAsync("photoIdentitySlideshow.exitFullscreen");
        }
        catch (JSException)
        {
            // Deliberate return remains available even if browser cleanup fails.
        }

        Navigation.NavigateTo(NormalizeReturnUrl(ReturnUrl), replace: true);
    }

    private string PlaybackResourceUrl(string revisionId)
    {
        if (_preparedOriginalsReady &&
            OriginalPreparation is not null &&
            Guid.TryParse(OriginalPreparation.SessionId, out Guid sessionId) &&
            sessionId != Guid.Empty)
        {
            return $"/api/slideshows/original-preparation/{sessionId:D}/photos/{Uri.EscapeDataString(revisionId)}/original";
        }

        return ViewerPreviewUrl(revisionId);
    }

    private static string ViewerPreviewUrl(string revisionId) =>
        $"/api/collections/photos/{Uri.EscapeDataString(revisionId)}/viewer-preview";

    private static bool ParseChecked(ChangeEventArgs args) => args.Value switch
    {
        bool value => value,
        string text when bool.TryParse(text, out bool value) => value,
        _ => false,
    };

    private static string CapabilityLabel(SlideshowBrowserFeatureStatus status)
    {
        if (status.Active)
        {
            return string.IsNullOrWhiteSpace(status.Mode)
                ? "active"
                : $"active ({status.Mode})";
        }

        if (status.Failed)
        {
            return "failed";
        }

        return status.Supported ? "available" : "unsupported";
    }

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
        CancelParentUnlockTimer();
        CancelExitHoldInternal();
        _navigationGate.Reset();
        await EndOriginalPreparationAsync();

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
