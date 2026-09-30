using System.Text.Json;
using Microsoft.JSInterop;
using PhotoIdentity.Web;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowPreparationReceiptStoreTests
{
    [Fact]
    public async Task Player_receipt_survives_reload_preserves_other_collections_and_tracks_exact_membership()
    {
        BrowserStorage js = new();
        string first = Guid.NewGuid().ToString("D");
        string second = Guid.NewGuid().ToString("D");
        SmartCollectionSlideshowSnapshotResponse snapshot = Snapshot(first);
        await SlideshowPreparationReceiptStore.RecordAsync(js, "manual", snapshot);
        await SlideshowPreparationReceiptStore.RecordAsync(js, "smart", Snapshot(second));
        var reloaded = JsonSerializer.Deserialize<Dictionary<string, SlideshowPreparationReceipt>>(js.Json!)!;
        Assert.True(reloaded["manual"].MatchesSnapshot(snapshot));
        Assert.True(reloaded["smart"].MatchesRevisionIds([second]));
        Assert.False(reloaded["manual"].MatchesRevisionIds([first, second]));

        await SlideshowPreparationReceiptStore.RecordAsync(js, "manual", Snapshot(second));
        reloaded = JsonSerializer.Deserialize<Dictionary<string, SlideshowPreparationReceipt>>(js.Json!)!;
        Assert.False(reloaded["manual"].MatchesSnapshot(snapshot));
        Assert.True(reloaded["manual"].MatchesRevisionIds([second]));
        Assert.Equal(2, reloaded.Count);
    }

    [Fact]
    public async Task Malformed_browser_state_is_replaced_with_verified_snapshot()
    {
        BrowserStorage js = new() { Json = "broken" };
        var snapshot = Snapshot(Guid.NewGuid().ToString("D"));
        await SlideshowPreparationReceiptStore.RecordAsync(js, "manual", snapshot);
        var reloaded = JsonSerializer.Deserialize<Dictionary<string, SlideshowPreparationReceipt>>(js.Json!)!;
        Assert.True(reloaded["manual"].MatchesSnapshot(snapshot));
    }

    private static SmartCollectionSlideshowSnapshotResponse Snapshot(string revision) => new(
        Guid.NewGuid().ToString("D"), "Manual", DateTimeOffset.UtcNow,
        [new SmartCollectionSlideshowSnapshotItemResponse(revision)], 1);

    private sealed class BrowserStorage : IJSRuntime
    {
        public string? Json { get; set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args)
        {
            Assert.Equal(SlideshowPreparationReceiptStore.StorageKey, args![0]);
            if (identifier == "localStorage.getItem")
                return ValueTask.FromResult((TValue)(object?)Json!);
            Assert.Equal("localStorage.setItem", identifier);
            Json = (string)args[1]!;
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
