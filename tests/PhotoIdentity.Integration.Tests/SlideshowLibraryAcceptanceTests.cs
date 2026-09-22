using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowLibraryAcceptanceTests
{
    [Fact]
    public async Task Library_launch_requests_fullscreen_before_navigation()
    {
        List<string> events = [];
        RecordingJsRuntime js = new(events);
        RecordingNavigationManager navigation = new(events);
        string collectionId = Guid.NewGuid().ToString("D");

        string? notice = await SlideshowLibraryLaunch.RequestFullscreenAndNavigateAsync(
            js,
            navigation,
            collectionId,
            "/slideshows");

        Assert.Null(notice);
        Assert.Equal(new[] { "fullscreen", "navigate" }, events);
        Assert.Equal(
            $"/slideshow/{collectionId}?return=%2Fslideshows",
            navigation.LastRelativeUri);
    }

    [Fact]
    public async Task Library_launch_still_navigates_when_fullscreen_is_unavailable()
    {
        List<string> events = [];
        RecordingJsRuntime js = new(events, fullscreenResult: false);
        RecordingNavigationManager navigation = new(events);
        string collectionId = Guid.NewGuid().ToString("D");

        string? notice = await SlideshowLibraryLaunch.RequestFullscreenAndNavigateAsync(
            js,
            navigation,
            collectionId,
            "/slideshows");

        Assert.Null(notice);
        Assert.Equal(new[] { "fullscreen", "navigate" }, events);
        Assert.NotNull(navigation.LastRelativeUri);
    }

    [Fact]
    public async Task Library_launch_still_navigates_when_fullscreen_is_rejected_by_interop()
    {
        List<string> events = [];
        RecordingJsRuntime js = new(events, throwOnFullscreen: true);
        RecordingNavigationManager navigation = new(events);
        string collectionId = Guid.NewGuid().ToString("D");

        string? notice = await SlideshowLibraryLaunch.RequestFullscreenAndNavigateAsync(
            js,
            navigation,
            collectionId,
            "/slideshows");

        Assert.NotNull(notice);
        Assert.Equal(new[] { "fullscreen", "navigate" }, events);
        Assert.NotNull(navigation.LastRelativeUri);
    }

    [Fact]
    public async Task Manual_library_launch_routes_to_manual_snapshot_playback()
    {
        List<string> events = [];
        RecordingJsRuntime js = new(events);
        RecordingNavigationManager navigation = new(events);
        string collectionId = Guid.NewGuid().ToString("D");

        string? notice = await SlideshowLibraryLaunch.RequestFullscreenAndNavigateAsync(
            js,
            navigation,
            collectionId,
            $"/manual-collections/{collectionId}",
            manual: true);

        Assert.Null(notice);
        Assert.Equal(new[] { "fullscreen", "navigate" }, events);
        Assert.Equal(
            $"/slideshow/{collectionId}?return=%2Fmanual-collections%2F{collectionId}&manual=true",
            navigation.LastRelativeUri);
        Assert.Equal(
            $"/manual-collections/{collectionId}",
            PhotoIdentity.Web.Pages.Slideshow.NormalizeReturnUrl(
                $"/manual-collections/{collectionId}"));
    }

    [Fact]
    public void Prepared_receipt_is_path_free_and_matches_the_same_revision_set_only()
    {
        string first = Guid.NewGuid().ToString("D");
        string second = Guid.NewGuid().ToString("D");
        SmartCollectionSlideshowSnapshotResponse snapshot = Snapshot(first, second);

        SlideshowPreparationReceipt receipt = SlideshowPreparationReceipt.FromSnapshot(snapshot);
        string json = JsonSerializer.Serialize(receipt);

        Assert.True(receipt.MatchesSnapshot(Snapshot(second, first)));
        Assert.False(receipt.MatchesSnapshot(Snapshot(first, Guid.NewGuid().ToString("D"))));
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("filename", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(receipt.GetRevisionIds(), value => Assert.True(Guid.TryParse(value, out _)));
    }

    private static SmartCollectionSlideshowSnapshotResponse Snapshot(params string[] revisionIds) =>
        new(
            Guid.NewGuid().ToString("D"),
            "Family",
            new DateTimeOffset(2026, 9, 12, 20, 0, 0, TimeSpan.Zero),
            revisionIds.Select(value => new SmartCollectionSlideshowSnapshotItemResponse(value)).ToArray(),
            revisionIds.Length);

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        private readonly List<string> _events;
        private readonly bool _throwOnFullscreen;
        private readonly bool _fullscreenResult;

        public RecordingJsRuntime(
            List<string> events,
            bool throwOnFullscreen = false,
            bool fullscreenResult = true)
        {
            _events = events;
            _throwOnFullscreen = throwOnFullscreen;
            _fullscreenResult = fullscreenResult;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            _events.Add("fullscreen");
            if (_throwOnFullscreen)
            {
                throw new JSException("Fullscreen rejected for test.");
            }

            return ValueTask.FromResult((TValue)(object)_fullscreenResult);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        private readonly List<string> _events;

        public RecordingNavigationManager(List<string> events)
        {
            _events = events;
            Initialize("http://localhost/", "http://localhost/slideshows");
        }

        public string? LastRelativeUri { get; private set; }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Record(uri);
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Record(uri);
        }

        private void Record(string uri)
        {
            _events.Add("navigate");
            LastRelativeUri = uri;
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
