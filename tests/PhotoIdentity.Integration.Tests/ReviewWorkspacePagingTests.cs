using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PhotoIdentity.Web.Components;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

// Exercise the real component with deterministic HTTP responses, without an API host,
// PostgreSQL, a browser, native libraries or production background workers.
public sealed class ReviewWorkspacePagingTests
{
    private static readonly ReviewPersonResponse Person = new("person-1", "Example person");

    [Fact]
    public async Task Duplicate_only_page_stops_and_disables_automatic_load_more()
    {
        List<int> offsets = [];
        using Harness harness = new(request =>
        {
            int offset = Offset(request);
            offsets.Add(offset);
            return Task.FromResult(Json(new ReviewFacePageResponse([Face(1), Face(2)], offset, 40, 100)));
        });

        await harness.InvokeAsync("LoadFacesAsync", true, 40);
        await harness.InvokeAsync("LoadMoreAsync");

        Assert.Equal(new[] { 0, 2 }, offsets);
        Assert.Equal(new[] { "face-1", "face-2" }, harness.Faces.Select(face => face.Id));
        Assert.True(harness.Get<bool>("PagingStopped"));
        Assert.Contains("no new faces", harness.Get<string>("QueueNotice"));
        Assert.False(harness.Get<bool>("LoadingMore"));
    }

    [Fact]
    public async Task Overlapping_pages_advance_by_server_rows_and_keep_unique_order()
    {
        List<int> offsets = [];
        using Harness harness = new(request =>
        {
            int offset = Offset(request);
            offsets.Add(offset);
            ReviewFaceResponse[] items = offset switch
            {
                0 => [Face(1), Face(2)],
                2 => [Face(2), Face(3)],
                4 => [Face(4)],
                _ => throw new InvalidOperationException($"Unexpected offset {offset}"),
            };
            return Task.FromResult(Json(new ReviewFacePageResponse(items, offset, 40, 5)));
        });

        await harness.InvokeAsync("LoadFacesAsync", true, 40);

        Assert.Equal(new[] { 0, 2, 4 }, offsets);
        Assert.Equal(new[] { "face-1", "face-2", "face-3", "face-4" }, harness.Faces.Select(face => face.Id));
        Assert.Equal(5, harness.Get<int>("NextOffset"));
        Assert.False(harness.Get<bool>("HasMore"));
        Assert.False(harness.Get<bool>("PagingStopped"));
    }

    [Fact]
    public async Task Continually_shifting_partial_pages_have_a_request_budget()
    {
        int requests = 0;
        using Harness harness = new(request =>
        {
            requests++;
            return Task.FromResult(Json(new ReviewFacePageResponse(
                [Face(1), Face(requests + 1)], Offset(request), 40, 1000)));
        });

        await harness.InvokeAsync("LoadFacesAsync", true, 40);
        await harness.InvokeAsync("LoadMoreAsync");

        Assert.Equal(5, requests);
        Assert.True(harness.Get<bool>("PagingStopped"));
        Assert.Contains("several overlapping pages", harness.Get<string>("QueueNotice"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, true)]
    public async Task Empty_page_finishes_or_pauses_when_total_is_stale(int total, bool paused)
    {
        int requests = 0;
        using Harness harness = new(request =>
        {
            requests++;
            return Task.FromResult(Json(new ReviewFacePageResponse([], Offset(request), 40, total)));
        });
        await harness.InvokeAsync("LoadFacesAsync", true, 40);
        await harness.InvokeAsync("LoadMoreAsync");
        Assert.Equal(1, requests);
        Assert.Equal(paused, harness.Get<bool>("PagingStopped"));
        Assert.False(harness.Get<bool>("HasMore"));
    }

    [Theory]
    [InlineData("assign", false)]
    [InlineData("accept-suggestions", false)]
    [InlineData("assign", true)]
    [InlineData("accept-suggestions", true)]
    public async Task Committed_bulk_action_returns_before_refill_and_preserves_success_on_failure(
        string action, bool failRefill)
    {
        TaskCompletionSource<HttpResponseMessage> refill = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int commits = 0;
        using Harness harness = new(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal(30, Offset(request));
                return refill.Task;
            }
            if (path.EndsWith("/preview", StringComparison.Ordinal))
            {
                return Task.FromResult(action == "assign"
                    ? Json(new BulkReviewPreviewResponse(action, 10, 10, 0, "token", Person))
                    : Json(new BulkSuggestionPreviewResponse(10, 10, 0, "token", Person, "model", "hash")));
            }
            commits++;
            return Task.FromResult(action == "assign"
                ? Json(new BulkReviewCommitResponse(action, 10, 10, Person, DateTimeOffset.UtcNow))
                : Json(new BulkSuggestionCommitResponse(10, 10, Person, "model", "hash", DateTimeOffset.UtcNow)));
        });
        harness.SeedForBulk();

        await harness.InvokeEventAsync("ExecuteBulkAsync", action).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(refill.Task.IsCompleted);
        Assert.False(harness.Get<bool>("BulkBusy"));
        Assert.True(harness.Get<bool>("LoadingMore"));
        Assert.Equal(30, harness.Faces.Count);
        Assert.Empty(harness.Get<HashSet<string>>("SelectedFaceIds"));
        string success = harness.Get<string>("Message");
        Assert.Contains(action == "assign" ? "Assigned 10" : "Accepted 10", success);
        Assert.Contains(success, harness.RenderedText);

        refill.SetResult(failRefill
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json(new ReviewFacePageResponse(Enumerable.Range(41, 10).Select(Face).ToArray(), 30, 40, 90)));
        await harness.Get<Task>("RefillTask").WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(success, harness.Get<string>("Message"));
        Assert.Null(harness.Get<string?>("Error"));
        Assert.False(harness.Get<bool>("LoadingMore"));
        Assert.Equal(1, commits);
        Assert.Equal(failRefill ? 30 : 40, harness.Faces.Count);
        if (failRefill)
        {
            Assert.Contains("saved decisions remain saved", harness.Get<string>("QueueNotice"));
        }
    }

    [Fact]
    public async Task Reset_ignores_old_response_even_when_transport_ignores_cancellation()
    {
        TaskCompletionSource<HttpResponseMessage> oldResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        using Harness harness = new(request =>
        {
            requests++;
            if (requests == 1)
            {
                started.SetResult();
                return oldResponse.Task;
            }
            return Task.FromResult(Json(new ReviewFacePageResponse([Face(99)], 0, 40, 1)));
        });

        Task oldLoad = harness.InvokeAsync("LoadFacesAsync", true, 40);
        await started.Task;
        await harness.InvokeAsync("LoadMoreAsync"); // Must not race the initial load.
        Assert.Equal(1, requests);
        await harness.InvokeAsync("LoadFacesAsync", true, 40);
        oldResponse.SetResult(Json(new ReviewFacePageResponse([Face(1)], 0, 40, 100)));
        await oldLoad;

        Assert.Equal("face-99", Assert.Single(harness.Faces).Id);
        Assert.Equal(1, harness.Get<int>("Total"));
        Assert.False(harness.Get<bool>("Loading"));
        Assert.Null(harness.Get<string?>("QueueNotice"));
    }

    [Fact]
    public async Task Disposal_drops_an_inflight_page()
    {
        TaskCompletionSource<HttpResponseMessage> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using Harness harness = new(_ => response.Task);
        Task load = harness.InvokeAsync("LoadFacesAsync", true, 40);
        harness.Component.Dispose();
        response.SetResult(Json(new ReviewFacePageResponse([Face(1)], 0, 40, 100)));
        await load;
        Assert.Empty(harness.Faces);
    }

    private static ReviewFaceResponse Face(int index) => new(
        $"face-{index}", $"/image/{index}", "example.jpg", index, null, "unreviewed", null,
        DateTimeOffset.UnixEpoch, new ReviewTopSuggestionResponse(
            index, Person, "model", "hash", 1, 0.9, 0.2, "pending", DateTimeOffset.UnixEpoch));

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static int Offset(HttpRequestMessage request) => int.Parse(
        request.RequestUri!.Query.TrimStart('?').Split('&').Single(part => part.StartsWith("offset=", StringComparison.Ordinal))[7..]);

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    // Renderer calls are isolated to the test harness.
#pragma warning disable BL0006
    private sealed class Harness : IDisposable
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly ServiceProvider _services;
        private readonly TestRenderer _renderer;
        private readonly HttpClient _http;
        public ReviewWorkspace Component { get; } = new();
        public List<ReviewFaceResponse> Faces => Get<List<ReviewFaceResponse>>("Faces");
        public string RenderedText => _renderer.Text;

        public Harness(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        {
            _http = new HttpClient(new Handler(respond)) { BaseAddress = new Uri("http://example.test") };
            _services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime, NoopJs>().BuildServiceProvider();
            _renderer = new TestRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
            Set("Http", _http);
            _renderer.Attach(Component);
        }

        public T Get<T>(string name) => (T)(typeof(ReviewWorkspace).GetProperty(name, Private)?.GetValue(Component)
            ?? typeof(ReviewWorkspace).GetField(name, Private)?.GetValue(Component))!;

        private void Set(string name, object value) => typeof(ReviewWorkspace).GetProperty(name, Private)!.SetValue(Component, value);

        public Task InvokeAsync(string name, params object[] args) => _renderer.Dispatcher.InvokeAsync(() =>
            (Task)typeof(ReviewWorkspace).GetMethod(name, Private)!.Invoke(Component, args)!);

        public Task InvokeEventAsync(string name, params object[] args) => _renderer.Dispatcher.InvokeAsync(() =>
            ((IHandleEvent)Component).HandleEventAsync(new EventCallbackWorkItem(
                (Func<Task>)(() => (Task)typeof(ReviewWorkspace).GetMethod(name, Private)!.Invoke(Component, args)!)), null));

        public void SeedForBulk()
        {
            Faces.AddRange(Enumerable.Range(1, 40).Select(Face));
            Get<HashSet<string>>("SelectedFaceIds").UnionWith(Faces.Take(10).Select(face => face.Id));
            Set("Total", 100);
            Set("NextOffset", 40);
            Set("People", new[] { Person });
            Set("BulkPersonId", Person.Id);
            Set("Options", new ReviewFilterOptionsResponse([], [new ReviewModelRevisionFilterResponse("model", "hash", DateTimeOffset.UnixEpoch, 100)]));
            Set("SelectedModelIndex", "0");
        }

        public void Dispose()
        {
            Component.Dispose();
            _renderer.Dispose();
            _services.Dispose();
            _http.Dispose();
        }
    }

    // The rendering API is intentionally confined to this small test adapter.
#pragma warning disable BL0006
    private sealed class TestRenderer(IServiceProvider services, ILoggerFactory loggerFactory) : Renderer(services, loggerFactory)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        private int _root;
        public void Attach(IComponent component) => _root = AssignRootComponentId(component);
        public string Text
        {
            get
            {
                var frames = GetCurrentRenderTreeFrames(_root);
                return string.Join(" ", frames.Array.Take(frames.Count)
                    .Where(frame => frame.FrameType == RenderTreeFrameType.Text).Select(frame => frame.TextContent));
            }
        }
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Component rendering failed", exception);
    }
#pragma warning restore BL0006

    private sealed class NoopJs : IJSRuntime, IJSObjectReference, IDisposable
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(typeof(TValue) == typeof(IJSObjectReference) ? (TValue)(object)this : default!);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
