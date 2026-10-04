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

public sealed class SlideshowPreparedStateContinuityTests
{
    [Fact]
    public async Task Receipt_restored_while_catalogue_loads_is_revalidated_when_catalogue_finishes()
    {
        TaskCompletionSource manualRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<HttpResponseMessage> manualResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        int revalidations = 0;

        await using Harness h = new(async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/slideshows/collections":
                    return Json(Array.Empty<SlideshowLibraryCollectionResponse>());
                case "/api/photo-list-collections":
                    manualRequested.TrySetResult();
                    return await manualResponse.Task;
                case "/api/slideshows/original-preparation/revalidate":
                    Interlocked.Increment(ref revalidations);
                    return Json(new SlideshowOriginalRevalidationResponse(true, 1, 1));
                default:
                    throw new InvalidOperationException(request.RequestUri.AbsolutePath);
            }
        });

        h.Set("_browserStateRestored", true);
        Task loading = h.Invoke("LoadCollectionsAsync");
        await manualRequested.Task;

        await h.Invoke("RestorePreparationReceiptsAsync");

        Assert.True(h.HasStoredReceipt);
        Assert.Empty(h.Preparations);
        Assert.Null(h.Get<string?>("VerificationError"));
        Assert.Equal(0, Volatile.Read(ref revalidations));

        manualResponse.SetResult(Json(new[] { Manual() }));
        await loading;

        Assert.True(h.HasStoredReceipt);
        Assert.Equal("ready", h.Preparations[CollectionId].State);
        Assert.Null(h.Get<string?>("VerificationError"));
        Assert.Equal(1, Volatile.Read(ref revalidations));
    }

    private static readonly string CollectionId = Guid.NewGuid().ToString("D");
    private static readonly string RevisionId = Guid.NewGuid().ToString("D");

    private static PhotoListCollectionResponse Manual() =>
        new(
            CollectionId,
            "Prepared example",
            [RevisionId],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly HttpClient _http;
        private readonly Slideshows _component = new();

        public Dictionary<string, string?> Storage { get; } = [];

        public Dictionary<string, SlideshowOriginalPreparationResponse> Preparations =>
            Get<Dictionary<string, SlideshowOriginalPreparationResponse>>("_preparations");

        public bool HasStoredReceipt =>
            Storage.TryGetValue(SlideshowPreparationReceiptStore.StorageKey, out string? json) &&
            json is not null &&
            JsonSerializer.Deserialize<Dictionary<string, SlideshowPreparationReceipt>>(json)!
                .ContainsKey(CollectionId);

        public Harness(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        {
            _http = new HttpClient(new Handler(respond))
            {
                BaseAddress = new Uri("http://example.test"),
            };
            _component.Http = _http;
            _component.Session = new SlideshowLibrarySessionState(_http);
            _component.JS = new BrowserStorage(Storage);
            Storage[SlideshowPreparationReceiptStore.StorageKey] = JsonSerializer.Serialize(
                new Dictionary<string, SlideshowPreparationReceipt>
                {
                    [CollectionId] = new([RevisionId]),
                });
        }

        public T Get<T>(string name) =>
            (T)(typeof(Slideshows).GetField(name, Private)?.GetValue(_component) ??
                typeof(Slideshows).GetProperty(name, Private)?.GetValue(_component))!;

        public void Set(string name, object value) =>
            typeof(Slideshows).GetField(name, Private)!.SetValue(_component, value);

        public Task Invoke(string name) =>
            (Task)typeof(Slideshows).GetMethod(name, Private)!.Invoke(_component, null)!;

        public async ValueTask DisposeAsync()
        {
            await _component.DisposeAsync();
            _http.Dispose();
        }
    }

    private sealed class BrowserStorage(Dictionary<string, string?> storage) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            string key = (string)args![0]!;
            if (identifier == "localStorage.getItem")
            {
                return ValueTask.FromResult((TValue)(object?)storage.GetValueOrDefault(key)!);
            }

            if (identifier == "localStorage.removeItem")
            {
                storage.Remove(key);
            }
            else if (identifier == "localStorage.setItem")
            {
                storage[key] = (string)args[1]!;
            }
            else
            {
                throw new InvalidOperationException(identifier);
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
