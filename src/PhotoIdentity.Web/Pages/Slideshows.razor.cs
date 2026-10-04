using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web.Pages;

public partial class Slideshows : IAsyncDisposable
{
    internal const string PreparationBookmarksStorageKey =
        "photoidentity.slideshow.library.preparations.v2";
    internal const string PreparationReceiptsStorageKey =
        SlideshowPreparationReceiptStore.StorageKey;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    private readonly Dictionary<string, SlideshowOriginalPreparationResponse> _preparations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _preparationRevisionIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SlideshowPreparationReceipt> _receipts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _smartPhotoCounts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _polling =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _starting =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _receiptRestoreGate = new(1, 1);

    private readonly CancellationToken _lifetimeToken;

    public Slideshows() => _lifetimeToken = _lifetime.Token;

    [Inject]
    public SlideshowLibrarySessionState Session { get; set; } = default!;

    [Inject]
    public HttpClient Http { get; set; } = default!;

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    private sealed record SlideshowLibraryEntry(
        string Id,
        string Name,
        bool Manual,
        string? CoverRevisionId,
        string[] RevisionIds,
        int? PhotoCount);

    private SlideshowSettings Settings { get; set; } = SlideshowSettings.Defaults;
    private IReadOnlyList<SlideshowLibraryEntry> Collections { get; set; } = [];
    private bool Loading { get; set; } = true;
    private string? Error { get; set; }
    private string? VerificationError { get; set; }
    private bool _collectionsVerified;
    private bool _loadingCollections;
    private bool _browserStateRestored;
    private bool _refreshing;
    private int _collectionGeneration;

    protected override async Task OnInitializedAsync()
    {
        await LoadCollectionsAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            string? storedSettings = await JS.InvokeAsync<string?>(
                "localStorage.getItem",
                SlideshowSettings.StorageKey);
            Settings = SlideshowSettings.FromJson(storedSettings);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
            Settings = SlideshowSettings.Defaults;
        }

        try
        {
            await RestorePreparationBookmarksAsync();
            // Browser storage is available now. Mark that fact before receipt restoration so a
            // concurrently finishing catalogue load can queue the same validation behind the gate.
            _browserStateRestored = true;
            await RestorePreparationReceiptsAsync();
            if (!_lifetime.IsCancellationRequested)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _loadingCollections || _lifetime.IsCancellationRequested)
        {
            return;
        }
        _refreshing = true;
        try
        {
            await LoadCollectionsAsync();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task LoadCollectionsAsync()
    {
        if (_loadingCollections || _lifetime.IsCancellationRequested)
        {
            return;
        }
        _loadingCollections = true;
        _collectionGeneration++;
        _collectionsVerified = false;
        if (Session.SmartCollections is not null && Session.ManualCollections is not null)
        {
            ApplyCollections(Session.SmartCollections, Session.ManualCollections);
        }
        Loading = Session.SmartCollections is null;
        Error = null;
        try
        {
            SlideshowLibraryCollectionResponse[] smartCollections =
                await Http.GetFromJsonAsync<SlideshowLibraryCollectionResponse[]>(
                    "api/slideshows/collections",
                    _lifetimeToken)
                ?? [];

            PhotoListCollectionResponse[] manualCollections = [];
            using (HttpResponseMessage manualResponse = await Http.GetAsync(
                       "api/photo-list-collections",
                       _lifetimeToken))
            {
                if (manualResponse.IsSuccessStatusCode)
                {
                    manualCollections =
                        await manualResponse.Content.ReadFromJsonAsync<PhotoListCollectionResponse[]>(
                            cancellationToken: _lifetimeToken)
                        ?? [];
                }
                else if (manualResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    throw new HttpRequestException(
                        $"Manual slideshows could not be loaded. HTTP {(int)manualResponse.StatusCode}.");
                }
            }

            _lifetimeToken.ThrowIfCancellationRequested();
            Session.SmartCollections = smartCollections;
            Session.ManualCollections = manualCollections;
            ApplyCollections(smartCollections, manualCollections);
            _collectionsVerified = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Error = $"Saved slideshows could not be loaded: {exception.Message}";
        }
        finally
        {
            Loading = false;
            _loadingCollections = false;
        }

        // On a fresh page instance browser-local receipts can become available before the async
        // catalogue load completes. Re-run validation once the authoritative collection list is
        // known so player-written and standalone receipts are not stranded until manual refresh.
        if (_collectionsVerified && _browserStateRestored && !_lifetime.IsCancellationRequested)
        {
            await RestorePreparationReceiptsAsync();
        }
    }

    private void ApplyCollections(
        SlideshowLibraryCollectionResponse[] smartCollections,
        PhotoListCollectionResponse[] manualCollections)
    {
        Collections = smartCollections
            .Select(collection => new SlideshowLibraryEntry(
                collection.Id,
                collection.Name,
                Manual: false,
                CoverRevisionId: null,
                RevisionIds: [],
                PhotoCount: null))
            .Concat(manualCollections.Select(collection => new SlideshowLibraryEntry(
                collection.Id,
                collection.Name,
                Manual: true,
                CoverRevisionId: collection.RevisionIds.FirstOrDefault(),
                RevisionIds: collection.RevisionIds,
                PhotoCount: SlideshowLibraryPresentation.ManualPhotoCount(collection))))
            .OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(collection => collection.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task ChangeSettingsAsync(SlideshowSettings settings)
    {
        Settings = settings.Normalize();
        try
        {
            await JS.InvokeVoidAsync(
                "localStorage.setItem",
                SlideshowSettings.StorageKey,
                Settings.ToJson());
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Settings remain active for this browser session.
        }
    }

    private async Task StartSlideshowAsync(SlideshowLibraryEntry collection)
    {
        if (IsBusy(collection.Id))
        {
            return;
        }

        _ = await SlideshowLibraryLaunch.RequestFullscreenAndNavigateAsync(
            JS,
            Navigation,
            collection.Id,
            "/slideshows",
            manual: collection.Manual);
    }

    private async Task PrepareOriginalsAsync(SlideshowLibraryEntry collection)
    {
        if (!_starting.Add(collection.Id) || IsServerPreparationActive(collection.Id))
        {
            return;
        }

        _receipts.Remove(collection.Id);
        await PersistPreparationReceiptsAsync();

        try
        {
            string snapshotPath = collection.Manual
                ? $"api/photo-list-collections/{Uri.EscapeDataString(collection.Id)}/slideshow-snapshot"
                : $"api/smart-collections/{Uri.EscapeDataString(collection.Id)}/slideshow-snapshot";
            using HttpResponseMessage snapshotResponse = await Http.PostAsync(
                snapshotPath,
                content: null,
                _lifetimeToken);
            if (!snapshotResponse.IsSuccessStatusCode)
            {
                SetLocalFailure(
                    collection.Id,
                    $"The slideshow snapshot could not be created. Status {(int)snapshotResponse.StatusCode}.");
                return;
            }

            SmartCollectionSlideshowSnapshotResponse snapshot =
                await snapshotResponse.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>(
                    cancellationToken: _lifetimeToken)
                ?? throw new InvalidOperationException("The slideshow snapshot response was empty.");

            SlideshowPreparationReceipt receipt = SlideshowPreparationReceipt.FromSnapshot(snapshot);
            string[] revisionIds = receipt.GetRevisionIds();
            _preparationRevisionIds[collection.Id] = revisionIds;

            SlideshowOriginalPreparationRequest request = new(revisionIds);
            using HttpResponseMessage preparationResponse = await Http.PostAsJsonAsync(
                "api/slideshows/original-preparation",
                request,
                _lifetimeToken);
            if (!preparationResponse.IsSuccessStatusCode)
            {
                _preparationRevisionIds.Remove(collection.Id);
                SetLocalFailure(
                    collection.Id,
                    $"Original preparation could not start. Status {(int)preparationResponse.StatusCode}.");
                return;
            }

            SlideshowOriginalPreparationResponse status =
                await preparationResponse.Content.ReadFromJsonAsync<SlideshowOriginalPreparationResponse>(
                    cancellationToken: _lifetimeToken)
                ?? throw new InvalidOperationException("The original preparation response was empty.");

            _preparations[collection.Id] = status;
            await PersistPreparationBookmarksAsync();
            await HandlePreparationStatusAsync(collection.Id, status);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _preparationRevisionIds.Remove(collection.Id);
            SetLocalFailure(
                collection.Id,
                $"Original preparation could not start: {exception.Message}");
        }
        finally
        {
            _starting.Remove(collection.Id);
            StateHasChanged();
        }
    }

    private async Task RetryPreparationAsync(string collectionId)
    {
        if (!_preparations.TryGetValue(collectionId, out SlideshowOriginalPreparationResponse? current) ||
            !current.CanRetry ||
            !Guid.TryParse(current.SessionId, out Guid sessionId) ||
            sessionId == Guid.Empty)
        {
            return;
        }

        try
        {
            using HttpResponseMessage response = await Http.PostAsync(
                $"api/slideshows/original-preparation/{sessionId:D}/retry",
                content: null,
                _lifetimeToken);
            if (!response.IsSuccessStatusCode)
            {
                _preparations[collectionId] = current with
                {
                    Message = $"Preparation retry could not be requested. Status {(int)response.StatusCode}.",
                };
                return;
            }

            SlideshowOriginalPreparationResponse status =
                await response.Content.ReadFromJsonAsync<SlideshowOriginalPreparationResponse>(
                    cancellationToken: _lifetimeToken)
                ?? throw new InvalidOperationException("The preparation retry response was empty.");
            _preparations[collectionId] = status;
            await PersistPreparationBookmarksAsync();
            StartPolling(collectionId);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _preparations[collectionId] = current with
            {
                Message = $"Preparation retry could not be requested: {exception.Message}",
            };
        }

        StateHasChanged();
    }

    private async Task CancelPreparationAsync(string collectionId)
    {
        StopPolling(collectionId);

        if (_preparations.TryGetValue(collectionId, out SlideshowOriginalPreparationResponse? current) &&
            Guid.TryParse(current.SessionId, out Guid sessionId) &&
            sessionId != Guid.Empty)
        {
            try
            {
                using HttpResponseMessage _ = await Http.DeleteAsync(
                    $"api/slideshows/original-preparation/{sessionId:D}",
                    _lifetimeToken);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch
            {
                // The server lease also expires; local UI cleanup should remain available.
            }
        }

        _preparations.Remove(collectionId);
        _preparationRevisionIds.Remove(collectionId);
        await PersistPreparationBookmarksAsync();
        StateHasChanged();
    }

    private async Task HandlePreparationStatusAsync(
        string collectionId,
        SlideshowOriginalPreparationResponse status)
    {
        _preparations[collectionId] = status;

        if (status.State == "ready")
        {
            await ReleaseCompletedPreparationAsync(collectionId, status);
            return;
        }

        if (status.State == "preparing")
        {
            StartPolling(collectionId);
        }
    }

    private void StartPolling(string collectionId)
    {
        if (_polling.ContainsKey(collectionId) ||
            !_preparations.TryGetValue(collectionId, out SlideshowOriginalPreparationResponse? status) ||
            !Guid.TryParse(status.SessionId, out Guid sessionId) ||
            sessionId == Guid.Empty)
        {
            return;
        }

        CancellationTokenSource cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        _polling[collectionId] = cancellation;
        _ = PollPreparationAsync(collectionId, sessionId, cancellation);
    }

    private async Task PollPreparationAsync(
        string collectionId,
        Guid sessionId,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, cancellation.Token);
                using HttpResponseMessage response = await Http.GetAsync(
                    $"api/slideshows/original-preparation/{sessionId:D}",
                    cancellation.Token);
                if (!response.IsSuccessStatusCode)
                {
                    await InvokeAsync(async () =>
                    {
                        _preparations.Remove(collectionId);
                        _preparationRevisionIds.Remove(collectionId);
                        await PersistPreparationBookmarksAsync();
                        StateHasChanged();
                    });
                    return;
                }

                SlideshowOriginalPreparationResponse status =
                    await response.Content.ReadFromJsonAsync<SlideshowOriginalPreparationResponse>(
                        cancellationToken: cancellation.Token)
                    ?? throw new InvalidOperationException("The preparation status response was empty.");

                await InvokeAsync(async () =>
                {
                    _preparations[collectionId] = status;
                    if (status.State == "ready")
                    {
                        await ReleaseCompletedPreparationAsync(collectionId, status);
                    }
                    else if (status.State is "failed" or "cancelled")
                    {
                        StopPolling(collectionId);
                        await PersistPreparationBookmarksAsync();
                    }

                    StateHasChanged();
                });

                if (status.State is "ready" or "failed" or "cancelled")
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await InvokeAsync(() =>
            {
                if (_preparations.TryGetValue(collectionId, out SlideshowOriginalPreparationResponse? current))
                {
                    _preparations[collectionId] = current with
                    {
                        Message = $"Preparation status could not be refreshed: {exception.Message}",
                    };
                }

                StateHasChanged();
            });
        }
        finally
        {
            if (_polling.TryGetValue(collectionId, out CancellationTokenSource? active) &&
                ReferenceEquals(active, cancellation))
            {
                _polling.Remove(collectionId);
            }

            cancellation.Dispose();
        }
    }

    private async Task ReleaseCompletedPreparationAsync(
        string collectionId,
        SlideshowOriginalPreparationResponse status)
    {
        StopPolling(collectionId);

        if (Guid.TryParse(status.SessionId, out Guid sessionId) && sessionId != Guid.Empty)
        {
            try
            {
                using HttpResponseMessage _ = await Http.DeleteAsync(
                    $"api/slideshows/original-preparation/{sessionId:D}",
                    _lifetimeToken);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch
            {
                // The ready lease is ephemeral and will expire even if this cleanup fails.
            }
        }

        _preparations[collectionId] = ReadyPreparation(status.Total);
        if (_preparationRevisionIds.TryGetValue(collectionId, out string[]? revisionIds))
        {
            _receipts[collectionId] = new SlideshowPreparationReceipt(revisionIds);
        }

        _preparationRevisionIds.Remove(collectionId);
        await PersistPreparationBookmarksAsync();
        await PersistPreparationReceiptsAsync();
    }

    private async Task RestorePreparationBookmarksAsync()
    {
        string? json;
        try
        {
            json = await JS.InvokeAsync<string?>(
                "localStorage.getItem",
                PreparationBookmarksStorageKey);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        Dictionary<string, SlideshowPreparationBookmark>? bookmarks;
        try
        {
            bookmarks = JsonSerializer.Deserialize<Dictionary<string, SlideshowPreparationBookmark>>(json);
        }
        catch (JsonException)
        {
            bookmarks = null;
        }

        if (bookmarks is null)
        {
            await RemovePreparationBookmarksAsync();
            return;
        }

        foreach ((string collectionId, SlideshowPreparationBookmark bookmark) in bookmarks)
        {
            if (bookmark is null || !Guid.TryParse(bookmark.SessionId, out Guid sessionId) || sessionId == Guid.Empty)
            {
                continue;
            }

            _preparationRevisionIds[collectionId] =
                new SlideshowPreparationReceipt(bookmark.RevisionIds ?? []).GetRevisionIds();

            try
            {
                using HttpResponseMessage response = await Http.GetAsync(
                    $"api/slideshows/original-preparation/{sessionId:D}",
                    _lifetimeToken);
                if (!response.IsSuccessStatusCode)
                {
                    _preparationRevisionIds.Remove(collectionId);
                    continue;
                }

                SlideshowOriginalPreparationResponse status =
                    await response.Content.ReadFromJsonAsync<SlideshowOriginalPreparationResponse>(
                        cancellationToken: _lifetimeToken)
                    ?? throw new InvalidOperationException("The preparation status response was empty.");
                _preparations[collectionId] = status;

                if (status.State == "ready")
                {
                    await ReleaseCompletedPreparationAsync(collectionId, status);
                }
                else if (status.State == "preparing")
                {
                    StartPolling(collectionId);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // A stale bookmark is discarded below.
                _preparationRevisionIds.Remove(collectionId);
            }
        }

        await PersistPreparationBookmarksAsync();
    }

    private async Task RestorePreparationReceiptsAsync()
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await _receiptRestoreGate.WaitAsync(_lifetimeToken);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await RestorePreparationReceiptsCoreAsync();
        }
        finally
        {
            _receiptRestoreGate.Release();
        }
    }

    private async Task RestorePreparationReceiptsCoreAsync()
    {
        int generation = _collectionGeneration;
        VerificationError = null;
        string? json;
        try
        {
            json = await JS.InvokeAsync<string?>(
                "localStorage.getItem",
                PreparationReceiptsStorageKey);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        if (generation != _collectionGeneration || _lifetime.IsCancellationRequested)
        {
            return;
        }

        Dictionary<string, SlideshowPreparationReceipt>? receipts;
        try
        {
            receipts = JsonSerializer.Deserialize<Dictionary<string, SlideshowPreparationReceipt>>(json);
        }
        catch (JsonException)
        {
            receipts = null;
        }

        if (receipts is null)
        {
            await RemovePreparationReceiptsAsync();
            return;
        }

        foreach ((string collectionId, SlideshowPreparationReceipt receipt) in receipts)
        {
            if (receipt is not null)
            {
                _receipts[collectionId] = receipt;
            }
        }

        if (!_collectionsVerified)
        {
            foreach (string collectionId in _receipts.Keys)
            {
                if (_preparations.GetValueOrDefault(collectionId)?.State == "ready")
                {
                    _preparations.Remove(collectionId);
                }
            }

            // During the initial async catalogue load this is a normal ordering race, not a
            // verification failure. Once loading finishes LoadCollectionsAsync retries here.
            if (!_loadingCollections && _receipts.Count > 0)
            {
                VerificationError = "Prepared originals could not be verified while slideshows are unavailable. Retry refresh to check them again.";
            }
            return;
        }

        foreach (string collectionId in _receipts.Keys.ToArray())
        {
            SlideshowLibraryEntry? collection = Collections.FirstOrDefault(collection =>
                string.Equals(collection.Id, collectionId, StringComparison.OrdinalIgnoreCase));
            if (collection is null)
            {
                _receipts.Remove(collectionId);
                continue;
            }

            if (IsServerPreparationActive(collectionId))
            {
                continue;
            }

            SlideshowPreparationReceipt receipt = _receipts[collectionId];
            if (_preparations.GetValueOrDefault(collectionId)?.State == "ready")
            {
                _preparations.Remove(collectionId);
            }
            try
            {
                if (collection.Manual)
                {
                    if (!receipt.MatchesRevisionIds(collection.RevisionIds))
                    {
                        _receipts.Remove(collectionId);
                        _preparations.Remove(collectionId);
                        continue;
                    }
                }
                else
                {
                    using HttpResponseMessage snapshotResponse = await Http.PostAsync(
                        $"api/smart-collections/{Uri.EscapeDataString(collectionId)}/slideshow-snapshot",
                        content: null,
                        _lifetimeToken);
                    if (generation != _collectionGeneration || _lifetime.IsCancellationRequested)
                    {
                        return;
                    }
                    if (snapshotResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        _receipts.Remove(collectionId);
                        continue;
                    }

                    if (!snapshotResponse.IsSuccessStatusCode)
                    {
                        VerificationError = "Prepared originals could not be verified. Retry refresh to check them again.";
                        continue;
                    }

                    SmartCollectionSlideshowSnapshotResponse snapshot =
                        await snapshotResponse.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>(
                            cancellationToken: _lifetimeToken)
                        ?? throw new InvalidOperationException("The slideshow snapshot response was empty.");
                    if (generation != _collectionGeneration || _lifetime.IsCancellationRequested)
                    {
                        return;
                    }
                    if (!receipt.MatchesSnapshot(snapshot))
                    {
                        _receipts.Remove(collectionId);
                        _preparations.Remove(collectionId);
                        continue;
                    }
                }

                string[] revisionIds = receipt.GetRevisionIds();
                using HttpResponseMessage validationResponse = await Http.PostAsJsonAsync(
                    "api/slideshows/original-preparation/revalidate",
                    new SlideshowOriginalPreparationRequest(revisionIds),
                    _lifetimeToken);
                if (generation != _collectionGeneration || _lifetime.IsCancellationRequested)
                {
                    return;
                }
                if (!validationResponse.IsSuccessStatusCode)
                {
                    VerificationError = "Prepared originals could not be verified. Retry refresh to check them again.";
                    continue;
                }

                SlideshowOriginalRevalidationResponse validation =
                    await validationResponse.Content.ReadFromJsonAsync<SlideshowOriginalRevalidationResponse>(
                        cancellationToken: _lifetimeToken)
                    ?? throw new InvalidOperationException("The prepared-original revalidation response was empty.");
                if (generation != _collectionGeneration || _lifetime.IsCancellationRequested)
                {
                    return;
                }
                if (!validation.Reusable || validation.Ready != revisionIds.Length || validation.Total != revisionIds.Length)
                {
                    _receipts.Remove(collectionId);
                    _preparations.Remove(collectionId);
                    continue;
                }

                _lifetimeToken.ThrowIfCancellationRequested();
                _preparations[collectionId] = ReadyPreparation(validation.Total);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                if (generation != _collectionGeneration || _lifetime.IsCancellationRequested)
                {
                    return;
                }
                // Keep the receipt for a future page load, but do not display stale prepared state
                // when the application cannot currently revalidate it.
                _preparations.Remove(collectionId);
                VerificationError = "Prepared originals could not be verified. Retry refresh to check them again.";
            }
        }

        await PersistPreparationReceiptsAsync();
    }

    private async Task PersistPreparationBookmarksAsync()
    {
        Dictionary<string, SlideshowPreparationBookmark> bookmarks = _preparations
            .Where(pair =>
                pair.Value.State is "preparing" or "failed" &&
                Guid.TryParse(pair.Value.SessionId, out Guid parsed) &&
                parsed != Guid.Empty &&
                _preparationRevisionIds.ContainsKey(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => new SlideshowPreparationBookmark(
                    pair.Value.SessionId,
                    _preparationRevisionIds[pair.Key]),
                StringComparer.OrdinalIgnoreCase);

        try
        {
            if (bookmarks.Count == 0)
            {
                await RemovePreparationBookmarksAsync();
            }
            else
            {
                await JS.InvokeVoidAsync(
                    "localStorage.setItem",
                    PreparationBookmarksStorageKey,
                    JsonSerializer.Serialize(bookmarks));
            }
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Reattachment is best effort; server preparation still follows its own lifetime.
        }
    }

    private async Task PersistPreparationReceiptsAsync()
    {
        try
        {
            if (_receipts.Count == 0)
            {
                await RemovePreparationReceiptsAsync();
            }
            else
            {
                await JS.InvokeVoidAsync(
                    "localStorage.setItem",
                    PreparationReceiptsStorageKey,
                    JsonSerializer.Serialize(_receipts));
            }
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
            // The preparation itself remains valid even when browser-local persistence is unavailable.
        }
    }

    private async Task RemovePreparationBookmarksAsync()
    {
        try
        {
            await JS.InvokeVoidAsync(
                "localStorage.removeItem",
                PreparationBookmarksStorageKey);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
        }
    }

    private async Task RemovePreparationReceiptsAsync()
    {
        try
        {
            await JS.InvokeVoidAsync(
                "localStorage.removeItem",
                PreparationReceiptsStorageKey);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
        }
    }

    private void StopPolling(string collectionId)
    {
        if (_polling.TryGetValue(collectionId, out CancellationTokenSource? cancellation))
        {
            _polling.Remove(collectionId);
            cancellation.Cancel();
        }
    }

    private void SetLocalFailure(string collectionId, string message)
    {
        _preparations[collectionId] = new SlideshowOriginalPreparationResponse(
            string.Empty,
            "failed",
            0,
            0,
            0,
            0,
            0,
            0,
            "failed",
            DateTimeOffset.UtcNow,
            0,
            false,
            false,
            0,
            0,
            message,
            false);
    }

    private static SlideshowOriginalPreparationResponse ReadyPreparation(int total) =>
        new(
            string.Empty,
            "ready",
            total,
            total,
            0,
            0,
            0,
            0,
            "ready",
            DateTimeOffset.UtcNow,
            0,
            false,
            false,
            0,
            0,
            "Originals are prepared and can be reused by a later slideshow.",
            false);

    private SlideshowOriginalPreparationResponse? PreparationFor(string collectionId) =>
        _preparations.GetValueOrDefault(collectionId);

    private int? PhotoCountFor(SlideshowLibraryEntry collection)
    {
        if (collection.Manual)
        {
            return collection.PhotoCount;
        }

        return _smartPhotoCounts.TryGetValue(collection.Id, out int count)
            ? count
            : null;
    }

    private Task SetSmartPhotoCountAsync(string collectionId, int? count)
    {
        if (count.HasValue)
        {
            _smartPhotoCounts[collectionId] = count.Value;
        }
        else
        {
            _smartPhotoCounts.Remove(collectionId);
        }

        return Task.CompletedTask;
    }

    private bool IsStarting(string collectionId) => _starting.Contains(collectionId);

    private bool IsServerPreparationActive(string collectionId) =>
        _preparations.TryGetValue(collectionId, out SlideshowOriginalPreparationResponse? status) &&
        status.State is "preparing" or "failed" &&
        !string.IsNullOrWhiteSpace(status.SessionId);

    private bool IsBusy(string collectionId) =>
        IsStarting(collectionId) || IsServerPreparationActive(collectionId);

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        foreach (CancellationTokenSource cancellation in _polling.Values.ToArray())
        {
            cancellation.Cancel();
        }

        _polling.Clear();
        _lifetime.Dispose();
        await Task.CompletedTask;
    }
}
