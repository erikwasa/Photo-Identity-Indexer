using System.Text.Json;
using Microsoft.JSInterop;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web;

public static class SlideshowPreparationReceiptStore
{
    public const string StorageKey = "photoidentity.slideshow.library.prepared.v1";

    public static async Task RecordAsync(
        IJSRuntime js,
        string collectionId,
        SmartCollectionSlideshowSnapshotResponse snapshot)
    {
        try
        {
            string? json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            Dictionary<string, SlideshowPreparationReceipt> receipts = new(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    Dictionary<string, SlideshowPreparationReceipt>? stored =
                        JsonSerializer.Deserialize<Dictionary<string, SlideshowPreparationReceipt>>(json);
                    if (stored is not null)
                    {
                        foreach (var pair in stored)
                        {
                            receipts[pair.Key] = pair.Value;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Replace malformed browser state with this successfully verified snapshot.
                }
            }

            receipts[collectionId] = SlideshowPreparationReceipt.FromSnapshot(snapshot);
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(receipts));
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Browser persistence must not prevent verified playback. Receipts are revalidated
            // against membership and local bytes by the library before displaying Prepared.
        }
    }
}
