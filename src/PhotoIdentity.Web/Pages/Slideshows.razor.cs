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
        "photoidentity.slideshow.library.prepared.v1";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    private readonly Dictionary<string, SlideshowOriginalPreparationResponse> _preparations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _preparationRevisionIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SlideshowPreparationReceipt> _receipts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _polling =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _starting =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();

    [Inject]
    public HttpClient Http { get; set; } = default!;

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    private SlideshowSettings Settings { get; set; } = SlideshowSettings.Defaults;
    private IReadOnlyList<SlideshowLibraryCollectionResponse> Collections { get; set; } = [];
    private bool Loading { get; set; } = true;
    private string? Error { get; set; }

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
        catch (JSException)
        {
            Settings = SlideshowSettings.Defaults;
        }

        await RestorePreparationBookmarksAsync();
        await RestorePreparationReceiptsAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadCollectionsAsync()
    {
        Loading = true;
        Error = null;
        try
        {
            SlideshowLibraryCollectionResponse[]? collections =
                await Http.GetFromJsonAsync<SlideshowLibraryCollectionResponse[]>(
                    "api/slideshows/collections",
                    _lifetime.Token);
            Collections = collections ?? [];
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Error = $"Saved Smart Collections could not be loaded: {exception.Message}";
        }
        finally
        {
            Loading = false;
        }
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
        catch (JSException)
        {
            // Settings remain active for this browser session.
        }
    }

    private async Task StartSlideshowAsync(SlideshowLibraryCollectionResponse collection)
    {
        if (IsBusy(collection.Id))
        {
            return;
        }

        _ = await SlideshowLibraryLaunch.RequestFullscreenAndNavigateAsync(
            JS,
            Navigation,
            collection.Id,
            "/slideshows");
    }

    private async Task PrepareOriginalsAsync(SlideshowLibraryCollectionResponse collection)
    {
        if (!_starting.Add(collection.Id) || IsServerPreparationActive(collection.Id))
        {
            return;
        }

        _receipts.Remove(collection.Id);
        await PersistPreparationReceiptsAsync();

        try
        {
            using HttpResponseMessage snapshotResponse = await Http.PostAsync(
                $"api/smart-collections/{Uri.EscapeDataString(collection.Id)}/slideshow-snapshot",
                content: null,
                _lifetime.Token);
            if (!snapshotResponse.IsSuccessStatusCode)
            {
                SetLocalFailure(
                    collection.Id,
                    $"The slideshow snapshot could not be created. Status {(int)snapshotResponse.StatusCode}.");
                return;
            }

            SmartCollectionSlideshowSnapshotResponse snapshot =
                await snapshotResponse.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>(
                    cancellationToken: _lifetime.Token)
                ?? throw new InvalidOperationException("The slideshow snapshot response was empty.");

            SlideshowPreparationReceipt receipt = SlideshowPreparationReceipt.FromSnapshot(snapshot);
            string[] revisionIds = receipt.GetRevisionIds();
            _preparationRevisionIds[collection.Id] = revisionIds;

            SlideshowOriginalPreparationRequest request = new(revisionIds);
            using HttpResponseMessage preparationResponse = await Http.PostAsJsonAsync(
                "api/slideshows/original-preparation",
                request,
                _lifetime.Token);
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
                    cancellationToken: _lifetime.Token)
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
                _lifetime.Token);
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
                    cancellationToken: _lifetime.Token)
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
                    _lifetime.Token);
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
            CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
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
                    _lifetime.Token);
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
        catch (JSException)
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
            if (!Guid.TryParse(bookmark.SessionId, out Guid sessionId) || sessionId == Guid.Empty)
            {
                continue;
            }

            _preparationRevisionIds[collectionId] =
                new SlideshowPreparationReceipt(bookmark.RevisionIds ?? []).GetRevisionIds();

            try
            {
                using HttpResponseMessage response = await Http.GetAsync(
                    $"api/slideshows/original-preparation/{sessionId:D}",
                    _lifetime.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _preparationRevisionIds.Remove(collectionId);
                    continue;
                }

                SlideshowOriginalPreparationResponse status =
                    await response.Content.ReadFromJsonAsync<SlideshowOriginalPreparationResponse>(
                        cancellationToken: _lifetime.Token)
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
        string? json;
        try
        {
            json = await JS.InvokeAsync<string?>(
                "localStorage.getItem",
                PreparationReceiptsStorageKey);
        }
        catch (JSException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(json))
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
            _receipts[collectionId] = receipt;
        }

        foreach (string collectionId in _receipts.Keys.ToArray())
        {
            if (!Collections.Any(collection =>
                    string.Equals(collection.Id, collectionId, StringComparison.OrdinalIgnoreCase)))
            {
                _receipts.Remove(collectionId);
                continue;
            }

            if (IsServerPreparationActive(collectionId))
            {
                continue;
            }

            SlideshowPreparationReceipt receipt = _receipts[collectionId];
            try
            {
                using HttpResponseMessage snapshotResponse = await Http.PostAsync(
                    $"api/smart-collections/{Uri.EscapeDataString(collectionId)}/slideshow-snapshot",
                    content: null,
                    _lifetime.Token);
                if (snapshotResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    _receipts.Remove(collectionId);
                    continue;
                }

                if (!snapshotResponse.IsSuccessStatusCode)
                {
                    continue;
                }

                SmartCollectionSlideshowSnapshotResponse snapshot =
                    await snapshotResponse.Content.ReadFromJsonAsync<SmartCollectionSlideshowSnapshotResponse>(
                        cancellationToken: _lifetime.Token)
                    ?? throw new InvalidOperationException("The slideshow snapshot response was empty.");
                if (!receipt.MatchesSnapshot(snapshot))
                {
                    _receipts.Remove(collectionId);
                    _preparations.Remove(collectionId);
                    continue;
                }

                string[] revisionIds = receipt.GetRevisionIds();
                using HttpResponseMessage validationResponse = await Http.PostAsJsonAsync(
                    "api/slideshows/original-preparation/revalidate",
                    new SlideshowOriginalPreparationRequest(revisionIds),
                    _lifetime.Token);
                if (!validationResponse.IsSuccessStatusCode)
                {
                    continue;
                }

                SlideshowOriginalRevalidationResponse validation =
                    await validationResponse.Content.ReadFromJsonAsync<SlideshowOriginalRevalidationResponse>(
                        cancellationToken: _lifetime.Token)
                    ?? throw new InvalidOperationException("The prepared-original revalidation response was empty.");
                if (!validation.Reusable || validation.Ready != revisionIds.Length || validation.Total != revisionIds.Length)
                {
                    _receipts.Remove(collectionId);
                    _preparations.Remove(collectionId);
                    continue;
                }

                _preparations[collectionId] = ReadyPreparation(validation.Total);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Keep the receipt for a future page load, but do not display stale prepared state
                // when the application cannot currently revalidate it.
                _preparations.Remove(collectionId);
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
        catch (JSException)
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
        catch (JSException)
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
        catch (JSException)
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
        catch (JSException)
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
