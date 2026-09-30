using System.Net;
using System.Net.Http.Json;
using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowLibrarySessionStateTests
{
    [Fact]
    public async Task Same_anchor_shares_cover_work_and_cancelled_card_does_not_cancel_other_cards()
    {
        DelayedHandler handler = new();
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://localhost/") };
        SlideshowLibrarySessionState state = new(http);
        using CancellationTokenSource cancellation = new();
        Task<SmartCollectionPageResponse?> first = state.GetCoverAsync("anchor", cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<SmartCollectionPageResponse?> second = state.GetCoverAsync("anchor");
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        handler.Response.SetResult(Page(53));
        Assert.Equal(53, (await second)!.Total);
        Assert.Equal(53, state.CachedCover("anchor")!.Total);
        Assert.Equal(53, (await state.GetCoverAsync("anchor"))!.Total);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Editing_a_definition_invalidates_its_cover_and_old_inflight_work_cannot_restore_it()
    {
        DelayedHandler handler = new();
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://localhost/") };
        SlideshowLibrarySessionState state = new(http);
        Task<SmartCollectionPageResponse?> old = state.GetCoverAsync("anchor");
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var oldResponse = handler.Response;
        state.InvalidateCover("anchor");
        handler.Reset();
        Task<SmartCollectionPageResponse?> fresh = state.GetCoverAsync("anchor");
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Response.SetResult(Page(54));
        await fresh;
        oldResponse.SetResult(Page(53));
        await old;
        Assert.Equal(54, state.CachedCover("anchor")!.Total);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Stale_cover_remains_visible_until_refresh_finishes_and_failure_can_be_retried()
    {
        Clock clock = new();
        DelayedHandler handler = new();
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://localhost/") };
        SlideshowLibrarySessionState state = new(http, clock);
        handler.Response.SetResult(Page(53));
        await state.GetCoverAsync("anchor");
        clock.Now += TimeSpan.FromMinutes(2);
        handler.Reset();
        Task<SmartCollectionPageResponse?> refresh = state.GetCoverAsync("anchor");
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(53, state.CachedCover("anchor")!.Total);
        handler.Response.SetException(new HttpRequestException("Unavailable"));
        await Assert.ThrowsAsync<HttpRequestException>(() => refresh);
        Assert.Equal(53, state.CachedCover("anchor")!.Total);
        handler.Reset();
        handler.Response.SetResult(Page(54));
        Assert.Equal(54, (await state.GetCoverAsync("anchor"))!.Total);
        Assert.Equal(3, handler.Calls);
    }

    private static SmartCollectionPageResponse Page(int count) => new(
        [], 0, 1, count,
        new SmartCollectionFilterResponse([], "any", [], "any", null, null));

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<SmartCollectionPageResponse> Response { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Reset()
        {
            Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Started.TrySetResult();
            SmartCollectionPageResponse page = await Response.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(page) };
        }
    }
}
