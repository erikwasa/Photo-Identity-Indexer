using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web.Components;

public partial class SmartCollectionsWorkspace
{
    private IReadOnlyList<CreativeCollectionRecipeResponse> NamedCreativeCollections { get; set; } = [];
    private string? SelectedNamedCreativeId { get; set; }
    private string CreativeCollectionName { get; set; } = string.Empty;
    private string? _namedCreativeAnchorLoaded;
    private bool _namedCreativeLoading;

    private CreativeCollectionRecipeResponse? SelectedNamedCreative =>
        SelectedNamedCreativeId is null
            ? null
            : NamedCreativeCollections.FirstOrDefault(item =>
                string.Equals(item.Id, SelectedNamedCreativeId, StringComparison.Ordinal));

    private bool NamedCreativeDirty => SelectedNamedCreative is null ||
        !string.Equals(SelectedNamedCreative.Name, CreativeCollectionName.Trim(), StringComparison.Ordinal) ||
        SelectedNamedCreative.TargetCount != CreativeTargetCount ||
        !string.Equals(
            SelectedNamedCreative.ContextStrength,
            CreativeContextStrength,
            StringComparison.OrdinalIgnoreCase) ||
        SelectedNamedCreative.NoveltyEnabled != CreativeNoveltyEnabled;

    private string NamedCreativeStatus => SelectedNamedCreative is null
        ? "New Creative Collection"
        : NamedCreativeDirty
            ? "Unsaved Creative Collection changes"
            : $"Saved · {SelectedNamedCreative.TargetCount} photos · {SelectedNamedCreative.ContextStrength} context · freshness {(SelectedNamedCreative.NoveltyEnabled ? "on" : "off")}";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (EditingId is null)
        {
            if (_namedCreativeAnchorLoaded is not null)
            {
                _namedCreativeAnchorLoaded = null;
                NamedCreativeCollections = [];
                SelectedNamedCreativeId = null;
                CreativeCollectionName = string.Empty;
            }
            return;
        }

        if (_namedCreativeLoading ||
            string.Equals(_namedCreativeAnchorLoaded, EditingId, StringComparison.Ordinal))
        {
            return;
        }

        _namedCreativeLoading = true;
        try
        {
            await LoadNamedCreativeCollectionsAsync(selectFirst: true);
            _namedCreativeAnchorLoaded = EditingId;
        }
        finally
        {
            _namedCreativeLoading = false;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task LoadNamedCreativeCollectionsAsync(bool selectFirst)
    {
        if (EditingId is null)
        {
            NamedCreativeCollections = [];
            return;
        }

        using HttpResponseMessage response = await Http.GetAsync(
            $"api/smart-collections/{Uri.EscapeDataString(EditingId)}/creative-collections");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            NamedCreativeCollections = [];
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            Error = await ReadErrorAsync(response, "Creative Collections could not be loaded.");
            return;
        }

        NamedCreativeCollections =
            await response.Content.ReadFromJsonAsync<CreativeCollectionRecipeResponse[]>()
            ?? [];

        if (selectFirst)
        {
            CreativeCollectionRecipeResponse? selected =
                SelectedNamedCreativeId is null
                    ? NamedCreativeCollections.FirstOrDefault()
                    : NamedCreativeCollections.FirstOrDefault(item =>
                        string.Equals(item.Id, SelectedNamedCreativeId, StringComparison.Ordinal));
            if (selected is not null)
            {
                ApplyNamedCreative(selected);
            }
            else
            {
                NewNamedCreative();
            }
        }
    }

    private void NewNamedCreative()
    {
        SelectedNamedCreativeId = null;
        CreativeCollectionName = string.IsNullOrWhiteSpace(Name)
            ? "Creative Collection"
            : $"{Name.Trim()} Creative";
        CreativeTargetCount = 50;
        CreativeContextStrength = "balanced";
        CreativeNoveltyEnabled = false;
        CreativePreview = null;
        Error = null;
        Notice = null;
    }

    private void SelectNamedCreative(ChangeEventArgs args)
    {
        string? id = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(id))
        {
            NewNamedCreative();
            return;
        }

        CreativeCollectionRecipeResponse? recipe = NamedCreativeCollections.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.Ordinal));
        if (recipe is not null)
        {
            ApplyNamedCreative(recipe);
        }
    }

    private void ApplyNamedCreative(CreativeCollectionRecipeResponse recipe)
    {
        SelectedNamedCreativeId = recipe.Id;
        CreativeCollectionName = recipe.Name;
        CreativeTargetCount = recipe.TargetCount;
        CreativeContextStrength = recipe.ContextStrength;
        CreativeNoveltyEnabled = recipe.NoveltyEnabled;
        CreativePreview = null;
        Error = null;
        Notice = null;
    }

    private async Task<CreativeCollectionRecipeResponse?> SaveNamedCreativeCoreAsync(bool showNotice)
    {
        if (EditingId is null || !ValidateCreativeSettings())
        {
            return null;
        }

        string creativeName = CreativeCollectionName.Trim();
        if (string.IsNullOrWhiteSpace(creativeName))
        {
            Error = "Creative Collection name is required.";
            return null;
        }

        CreativeCollectionRecipeRequest request = new(
            CreativeTargetCount,
            CreativeContextStrength,
            CreativeNoveltyEnabled,
            creativeName);

        using HttpResponseMessage response = SelectedNamedCreativeId is null
            ? await Http.PostAsJsonAsync(
                $"api/smart-collections/{Uri.EscapeDataString(EditingId)}/creative-collections",
                request)
            : await Http.PutAsJsonAsync(
                $"api/creative-collections/{Uri.EscapeDataString(SelectedNamedCreativeId)}",
                request);

        if (!response.IsSuccessStatusCode)
        {
            Error = await ReadErrorAsync(response, "The Creative Collection could not be saved.");
            return null;
        }

        CreativeCollectionRecipeResponse saved =
            await response.Content.ReadFromJsonAsync<CreativeCollectionRecipeResponse>()
            ?? throw new InvalidOperationException("The saved Creative Collection response was empty.");

        SelectedNamedCreativeId = saved.Id;
        ApplyNamedCreative(saved);
        await LoadNamedCreativeCollectionsAsync(selectFirst: false);
        if (showNotice)
        {
            Notice = $"Creative Collection '{saved.Name}' saved.";
        }
        return saved;
    }

    private async Task SaveNamedCreativeAsync()
    {
        Busy = true;
        Error = null;
        Notice = null;
        try
        {
            CreativeCollectionRecipeResponse? saved = await SaveNamedCreativeCoreAsync(showNotice: true);
            if (saved is not null)
            {
                await PreviewCreativeCoreAsync();
            }
        }
        catch (Exception exception)
        {
            Error = $"The Creative Collection could not be saved: {exception.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task DeleteNamedCreativeAsync()
    {
        if (SelectedNamedCreativeId is null)
        {
            return;
        }

        string deletedName = CreativeCollectionName;
        Busy = true;
        Error = null;
        Notice = null;
        try
        {
            using HttpResponseMessage response = await Http.DeleteAsync(
                $"api/creative-collections/{Uri.EscapeDataString(SelectedNamedCreativeId)}");
            if (response.StatusCode != HttpStatusCode.NoContent)
            {
                Error = await ReadErrorAsync(response, "The Creative Collection could not be deleted.");
                return;
            }

            SelectedNamedCreativeId = null;
            await LoadNamedCreativeCollectionsAsync(selectFirst: true);
            Notice = $"Deleted {deletedName}.";
        }
        catch (Exception exception)
        {
            Error = $"The Creative Collection could not be deleted: {exception.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task StartNamedCreativeSlideshowAsync()
    {
        if (EditingId is null)
        {
            return;
        }

        Busy = true;
        Error = null;
        Notice = null;
        try
        {
            try
            {
                _ = await JS.InvokeAsync<bool>("photoIdentitySlideshow.requestFullscreen");
            }
            catch (JSException exception)
            {
                Notice = $"Fullscreen could not be requested before slideshow navigation: {exception.Message}";
            }

            CreativeCollectionRecipeResponse? saved = await SaveNamedCreativeCoreAsync(showNotice: false);
            if (saved is null)
            {
                return;
            }

            string returnUrl = Uri.EscapeDataString(CurrentSavedWorkspaceUrl);
            Navigation.NavigateTo(
                $"/slideshow/{EditingId}?creative=true&creativeCollection={Uri.EscapeDataString(saved.Id)}&return={returnUrl}");
        }
        catch (Exception exception)
        {
            Error = $"The Creative Collection could not be started: {exception.Message}";
        }
        finally
        {
            Busy = false;
        }
    }
}
