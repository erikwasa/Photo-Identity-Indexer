using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.JSInterop;
using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using PhotoIdentity.Web.Pages;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

// Real component methods with controlled HTTP/browser storage; no host or PostgreSQL.
public sealed class SlideshowLibraryRecoveryTests
{
    [Fact]
    public async Task Failed_manual_list_preserves_receipt_and_retry_restores_verified_prepared_state()
    {
        bool recovered = false;
        await using Harness h = new(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/slideshows/collections" => Json(Array.Empty<SlideshowLibraryCollectionResponse>()),
            "/api/photo-list-collections" => recovered ? Json(new[] { Manual() }) : Failure(),
            "/api/slideshows/original-preparation/revalidate" => Json(new SlideshowOriginalRevalidationResponse(true, 1, 1)),
            _ => throw new InvalidOperationException(),
        });
        await h.Invoke("LoadCollectionsAsync");
        await h.Invoke("RestorePreparationReceiptsAsync");
        Assert.True(h.HasStoredReceipt);
        Assert.Empty(h.Preparations);
        Assert.NotNull(h.Get<string?>("VerificationError"));

        recovered = true;
        await h.Invoke("RefreshAsync");
        Assert.True(h.HasStoredReceipt);
        Assert.Equal("ready", h.Preparations[CollectionId].State);
        Assert.Null(h.Get<string?>("VerificationError"));
    }

    [Fact]
    public async Task Failed_revalidation_hides_old_prepared_state_preserves_receipt_and_can_retry()
    {
        bool recovered = true;
        await using Harness h = new(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/slideshows/collections" => Json(Array.Empty<SlideshowLibraryCollectionResponse>()),
            "/api/photo-list-collections" => Json(new[] { Manual() }),
            "/api/slideshows/original-preparation/revalidate" => recovered ? Json(new SlideshowOriginalRevalidationResponse(true, 1, 1)) : Failure(),
            _ => throw new InvalidOperationException(),
        });
        await h.Invoke("RefreshAsync");
        Assert.Equal("ready", h.Preparations[CollectionId].State);
        recovered = false;
        await h.Invoke("RefreshAsync");
        Assert.True(h.HasStoredReceipt);
        Assert.Empty(h.Preparations);
        Assert.NotNull(h.Get<string?>("VerificationError"));
        recovered = true;
        await h.Invoke("RefreshAsync");
        Assert.Equal("ready", h.Preparations[CollectionId].State);
        Assert.Null(h.Get<string?>("VerificationError"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirmed_membership_or_local_byte_change_invalidates_receipt(bool membershipChange)
    {
        await using Harness h = new(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/slideshows/collections" => Json(Array.Empty<SlideshowLibraryCollectionResponse>()),
            "/api/photo-list-collections" => Json(new[] { Manual(membershipChange ? Guid.NewGuid().ToString("D") : RevisionId) }),
            "/api/slideshows/original-preparation/revalidate" => Json(new SlideshowOriginalRevalidationResponse(false, 0, 1)),
            _ => throw new InvalidOperationException(),
        });
        await h.Invoke("RefreshAsync");
        Assert.False(h.HasStoredReceipt);
        Assert.Empty(h.Preparations);
    }

    [Fact]
    public async Task Null_browser_bookmarks_and_receipts_do_not_escape_component_handling()
    {
        await using Harness h = new((Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new InvalidOperationException()));
        h.Storage["photoidentity.slideshow.library.preparations.v2"] = "{\"invalid\":null}";
        h.Storage[SlideshowPreparationReceiptStore.StorageKey] = "{\"invalid\":null}";
        await h.Invoke("RestorePreparationBookmarksAsync");
        await h.Invoke("RestorePreparationReceiptsAsync");
        Assert.Empty(h.Preparations);
    }

    [Fact]
    public async Task Earlier_validation_cannot_restore_prepared_after_membership_refresh()
    {
        TaskCompletionSource<HttpResponseMessage> validation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool changed = false;
        await using Harness h = new(async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/slideshows/collections": return Json(Array.Empty<SlideshowLibraryCollectionResponse>());
                case "/api/photo-list-collections": return Json(new[] { Manual(changed ? Guid.NewGuid().ToString("D") : RevisionId) });
                case "/api/slideshows/original-preparation/revalidate":
                    started.TrySetResult();
                    return await validation.Task;
                default: throw new InvalidOperationException();
            }
        });
        await h.Invoke("LoadCollectionsAsync");
        Task old = h.Invoke("RestorePreparationReceiptsAsync");
        await started.Task;
        changed = true;
        await h.Invoke("LoadCollectionsAsync");
        validation.SetResult(Json(new SlideshowOriginalRevalidationResponse(true, 1, 1)));
        await old;
        Assert.Empty(h.Preparations);
        await h.Invoke("RestorePreparationReceiptsAsync");
        Assert.False(h.HasStoredReceipt);
    }

    [Fact]
    public async Task Navigation_disposal_during_validation_does_not_restore_prepared_or_escape()
    {
        TaskCompletionSource<HttpResponseMessage> validation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Harness h = new(async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/slideshows/collections": return Json(Array.Empty<SlideshowLibraryCollectionResponse>());
                case "/api/photo-list-collections": return Json(new[] { Manual() });
                case "/api/slideshows/original-preparation/revalidate":
                    started.TrySetResult();
                    return await validation.Task;
                default: throw new InvalidOperationException();
            }
        });
        await h.Invoke("LoadCollectionsAsync");
        Task pending = h.Invoke("RestorePreparationReceiptsAsync");
        await started.Task;
        await h.DisposeAsync();
        validation.SetResult(Json(new SlideshowOriginalRevalidationResponse(true, 1, 1)));
        await pending;
        Assert.Empty(h.Preparations);
    }

    private static readonly string CollectionId = Guid.NewGuid().ToString("D");
    private static readonly string RevisionId = Guid.NewGuid().ToString("D");
    private static PhotoListCollectionResponse Manual(string? revision = null) =>
        new(CollectionId, "Example", [revision ?? RevisionId], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private static HttpResponseMessage Failure() => new(HttpStatusCode.InternalServerError);

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly HttpClient _http;
        private readonly Slideshows _component = new();
        public Dictionary<string, string?> Storage { get; } = [];
        public Dictionary<string, SlideshowOriginalPreparationResponse> Preparations => Get<Dictionary<string, SlideshowOriginalPreparationResponse>>("_preparations");
        public bool HasStoredReceipt => Storage.TryGetValue(SlideshowPreparationReceiptStore.StorageKey, out string? json) &&
            json is not null && JsonSerializer.Deserialize<Dictionary<string, SlideshowPreparationReceipt>>(json)!.ContainsKey(CollectionId);

        public Harness(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this(request => Task.FromResult(respond(request)))
        {
        }

        public Harness(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        {
            _http = new HttpClient(new Handler(respond)) { BaseAddress = new Uri("http://example.test") };
            _component.Http = _http;
            _component.Session = new SlideshowLibrarySessionState(_http);
            _component.JS = new BrowserStorage(Storage);
            typeof(Slideshows).GetField("_browserStateRestored", Private)!.SetValue(_component, true);
            Storage[SlideshowPreparationReceiptStore.StorageKey] = JsonSerializer.Serialize(
                new Dictionary<string, SlideshowPreparationReceipt> { [CollectionId] = new([RevisionId]) });
        }
        public T Get<T>(string name) => (T)(typeof(Slideshows).GetField(name, Private)?.GetValue(_component) ??
            typeof(Slideshows).GetProperty(name, Private)?.GetValue(_component))!;
        public Task Invoke(string name) => (Task)typeof(Slideshows).GetMethod(name, Private)!.Invoke(_component, null)!;
        public async ValueTask DisposeAsync() { await _component.DisposeAsync(); _http.Dispose(); }
    }

    private sealed class BrowserStorage(Dictionary<string, string?> storage) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            string key = (string)args![0]!;
            if (identifier == "localStorage.getItem") return ValueTask.FromResult((TValue)(object?)storage.GetValueOrDefault(key)!);
            if (identifier == "localStorage.removeItem") storage.Remove(key);
            else if (identifier == "localStorage.setItem") storage[key] = (string)args[1]!;
            else throw new InvalidOperationException(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
