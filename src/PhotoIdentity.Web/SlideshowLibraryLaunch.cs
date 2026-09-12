using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace PhotoIdentity.Web;

public static class SlideshowLibraryLaunch
{
    public static async Task<string?> RequestFullscreenAndNavigateAsync(
        IJSRuntime js,
        NavigationManager navigation,
        string collectionId,
        string returnUrl)
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
        navigation.NavigateTo(
            $"/slideshow/{Uri.EscapeDataString(collectionId)}?return={encodedReturn}");
        return notice;
    }
}
