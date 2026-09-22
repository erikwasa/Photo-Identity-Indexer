using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace PhotoIdentity.Web;

public static class SlideshowLibraryLaunch
{
    public static async Task<string?> RequestFullscreenAndNavigateAsync(
        IJSRuntime js,
        NavigationManager navigation,
        string collectionId,
        string returnUrl,
        bool manual = false)
    {
        ArgumentNullException.ThrowIfNull(js);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(returnUrl);

        string? notice = null;
        try
        {
            _ = await js.InvokeAsync<bool>("photoIdentitySlideshow.requestFullscreen");
        }
        catch (JSException exception)
        {
            notice = $"Fullscreen could not be requested before slideshow navigation: {exception.Message}";
        }

        string encodedReturn = Uri.EscapeDataString(returnUrl);
        string manualQuery = manual ? "&manual=true" : string.Empty;
        navigation.NavigateTo(
            $"/slideshow/{Uri.EscapeDataString(collectionId)}?return={encodedReturn}{manualQuery}");
        return notice;
    }
}
