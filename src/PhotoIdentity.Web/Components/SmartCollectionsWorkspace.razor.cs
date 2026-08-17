using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web.Components;

public partial class SmartCollectionsWorkspace
{
    private const int PageSize = 40;
    private static readonly JsonSerializerOptions NavigationJsonOptions = new(JsonSerializerDefaults.Web);

    [Inject]
    public HttpClient Http { get; set; } = default!;

    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Parameter]
    [SupplyParameterFromQuery(Name = "mode")]
    public string? NavigationMode { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "collection")]
    public string? NavigationCollectionId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "offset")]
    public int? NavigationOffset { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "preview")]
    public string? NavigationPreviewKey { get; set; }

    private IReadOnlyList<ReviewPersonResponse> People { get; set; } = [];
    private IReadOnlyList<PhotoTagDefinitionResponse> Tags { get; set; } = [];
    private IReadOnlyList<SmartCollectionDefinitionResponse> Definitions { get; set; } = [];
    private HashSet<string> SelectedPeople { get; } = new(StringComparer.Ordinal);
    private HashSet<string> SelectedTags { get; } = new(StringComparer.OrdinalIgnoreCase);
    private SmartCollectionPageResponse? Results { get; set; }
    private string? EditingId { get; set; }
    private string? TransientPreviewKey { get; set; }
    private string Name { get; set; } = "";
    private string PeopleMatch { get; set; } = "all";
    private string TagMatch { get; set; } = "all";
    private string Taken { get; set; } = "";
    private string? SelectedPlace { get; set; }
    private bool UseLocation { get; set; }
    private string South { get; set; } = "";
    private string West { get; set; } = "";
    private string North { get; set; } = "";
    private string East { get; set; } = "";
    private bool Loading { get; set; } = true;
    private bool Busy { get; set; }
    private string? Error { get; set; }
    private string? Notice { get; set; }
    private ResultMode ActiveResultMode { get; set; }

    private IReadOnlyList<ReviewPersonResponse> OrderedPeople => People
        .OrderByDescending(person => person.IsFavorite)
        .ThenBy(person => person.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(person => person.Id, StringComparer.Ordinal)
        .ToArray();

    private IReadOnlyList<PhotoTagDefinitionResponse> OrderedTags => Tags
        .OrderBy(tag => tag.Value, StringComparer.OrdinalIgnoreCase)
        .ThenBy(tag => tag.Id, StringComparer.Ordinal)
        .ToArray();

    private string EditorHeading => EditingId is null ? "New smart collection" : "Edit smart collection";
    private string SaveLabel => EditingId is null ? "Save collection" : "Save changes";
    private int FirstResult => Results is null || Results.Items.Length == 0 ? 0 : Results.Offset + 1;
    private int LastResult => Results is null ? 0 : Results.Offset + Results.Items.Length;
    private string CurrentWorkspaceReturnUrl => ActiveResultMode switch
    {
        ResultMode.Saved when EditingId is not null && Results is not null =>
            SmartCollectionNavigation.BuildSavedWorkspaceUrl(EditingId, Results.Offset),
        ResultMode.Transient when TransientPreviewKey is not null && Results is not null =>
            SmartCollectionNavigation.BuildTransientWorkspaceUrl(TransientPreviewKey, Results.Offset),
        _ => "/smart-collections",
    };

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Task<ReviewPersonResponse[]?> peopleTask = Http.GetFromJsonAsync<ReviewPersonResponse[]>("api/review/people");
            Task<PhotoTagDefinitionResponse[]?> tagTask = Http.GetFromJsonAsync<PhotoTagDefinitionResponse[]>("api/tags");
            Task<SmartCollectionDefinitionResponse[]?> definitionTask =
                Http.GetFromJsonAsync<SmartCollectionDefinitionResponse[]>("api/smart-collections");

            await Task.WhenAll(peopleTask, tagTask, definitionTask);
            People = await peopleTask ?? [];
            Tags = await tagTask ?? [];
            Definitions = await definitionTask ?? [];
            await RestoreNavigationStateAsync();
        }
        catch (Exception exception)
        {
            Error = $"Smart collections could not be loaded: {exception.Message}";
        }
        finally
        {
            Loading = false;
        }
    }

    private async Task NewCollection()
    {
        ResetEditor();
        await ReplaceWorkspaceUrlAsync("/smart-collections");
    }

    private void ResetEditor()
    {
        EditingId = null;
        TransientPreviewKey = null;
        Name = "";
        SelectedPeople.Clear();
        SelectedTags.Clear();
        PeopleMatch = "all";
        TagMatch = "all";
        Taken = "";
        SelectedPlace = null;
        UseLocation = false;
        South = West = North = East = "";
        Results = null;
        ActiveResultMode = ResultMode.None;
        Error = null;
        Notice = null;
    }

    private async Task OpenSavedAsync(SmartCollectionDefinitionResponse definition)
    {
        ApplyDefinition(definition);
        await QuerySavedAsync(0);
    }

    private void ApplyDefinition(SmartCollectionDefinitionResponse definition)
    {
        EditingId = definition.Id;
        TransientPreviewKey = null;
        Name = definition.Name;
        SelectedPeople.Clear();
        foreach (string person in definition.Filter.People)
        {
            SelectedPeople.Add(person);
        }

        SelectedTags.Clear();
        foreach (string tag in definition.Filter.Tags)
        {
            SelectedTags.Add(tag);
        }

        PeopleMatch = definition.Filter.PeopleMatch;
        TagMatch = definition.Filter.TagMatch;
        Taken = ToEditableTaken(definition.Filter.Taken);
        if (definition.Filter.Location is SmartCollectionLocationRequest location)
        {
            SelectedPlace = location.Place;
            UseLocation = location.South.HasValue &&
                location.West.HasValue &&
                location.North.HasValue &&
                location.East.HasValue;
            South = location.South?.ToString(CultureInfo.InvariantCulture) ?? "";
            West = location.West?.ToString(CultureInfo.InvariantCulture) ?? "";
            North = location.North?.ToString(CultureInfo.InvariantCulture) ?? "";
            East = location.East?.ToString(CultureInfo.InvariantCulture) ?? "";
        }
        else
        {
            SelectedPlace = null;
            UseLocation = false;
            South = West = North = East = "";
        }

        Results = null;
        ActiveResultMode = ResultMode.None;
        Error = null;
        Notice = null;
    }

    private void ApplyTransientNavigationState(SmartCollectionTransientNavigationState state)
    {
        EditingId = state.EditingId;
        Name = state.Name;
        SelectedPeople.Clear();
        foreach (string person in state.People)
        {
            SelectedPeople.Add(person);
        }

        SelectedTags.Clear();
        foreach (string tag in state.Tags)
        {
            SelectedTags.Add(tag);
        }

        PeopleMatch = state.PeopleMatch;
        TagMatch = state.TagMatch;
        Taken = state.Taken;
        SelectedPlace = state.Place;
        UseLocation = state.UseLocation;
        South = state.South;
        West = state.West;
        North = state.North;
        East = state.East;
        Results = null;
        ActiveResultMode = ResultMode.None;
        Error = null;
        Notice = null;
    }

    private void TogglePerson(string id, ChangeEventArgs args)
    {
        if (IsChecked(args))
        {
            SelectedPeople.Add(id);
        }
        else
        {
            SelectedPeople.Remove(id);
        }
    }

    private void ToggleTag(string value, ChangeEventArgs args)
    {
        if (IsChecked(args))
        {
            SelectedTags.Add(value);
        }
        else
        {
            SelectedTags.Remove(value);
        }
    }

    private async Task SaveAsync()
    {
        Error = null;
        Notice = null;
        if (!TryBuildDefinitionRequest(out SmartCollectionDefinitionRequest? request))
        {
            return;
        }

        Busy = true;
        try
        {
            using HttpResponseMessage response = EditingId is null
                ? await Http.PostAsJsonAsync("api/smart-collections", request)
                : await Http.PutAsJsonAsync($"api/smart-collections/{EditingId}", request);

            if (!response.IsSuccessStatusCode)
            {
                Error = await ReadErrorAsync(response, "The smart collection could not be saved.");
                return;
            }

            SmartCollectionDefinitionResponse saved =
                await response.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException("The saved collection response was empty.");
            ApplyDefinition(saved);
            await RefreshDefinitionsAsync();
            Notice = "Smart collection saved.";
            await QuerySavedCoreAsync(0);
            if (ActiveResultMode == ResultMode.Saved && Results is not null)
            {
                await ReplaceSavedWorkspaceUrlAsync();
            }
        }
        catch (Exception exception)
        {
            Error = $"The smart collection could not be saved: {exception.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task PreviewAsync()
    {
        Error = null;
        Notice = null;
        if (!TryBuildQueryRequest(0, out SmartCollectionQueryRequest? request))
        {
            return;
        }

        Busy = true;
        try
        {
            await QueryTransientCoreAsync(request!);
            if (ActiveResultMode == ResultMode.Transient && Results is not null)
            {
                await PersistTransientNavigationAsync();
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task DeleteAsync(SmartCollectionDefinitionResponse definition)
    {
        Busy = true;
        Error = null;
        Notice = null;
        try
        {
            using HttpResponseMessage response = await Http.DeleteAsync($"api/smart-collections/{definition.Id}");
            if (response.StatusCode is not HttpStatusCode.NoContent)
            {
                Error = await ReadErrorAsync(response, "The smart collection could not be deleted.");
                return;
            }

            if (string.Equals(EditingId, definition.Id, StringComparison.Ordinal))
            {
                ResetEditor();
                Notice = "Smart collection deleted.";
                await ReplaceWorkspaceUrlAsync("/smart-collections");
            }
            else
            {
                Notice = $"Deleted {definition.Name}.";
            }

            await RefreshDefinitionsAsync();
        }
        catch (Exception exception)
        {
            Error = $"The smart collection could not be deleted: {exception.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task PreviousPageAsync()
    {
        if (Results is null)
        {
            return;
        }

        int offset = Math.Max(0, Results.Offset - PageSize);
        await LoadActiveResultsAsync(offset);
    }

    private async Task NextPageAsync()
    {
        if (Results is null)
        {
            return;
        }

        await LoadActiveResultsAsync(Results.Offset + PageSize);
    }

    private async Task LoadActiveResultsAsync(int offset)
    {
        Busy = true;
        try
        {
            if (ActiveResultMode == ResultMode.Saved && EditingId is not null)
            {
                await QuerySavedCoreAsync(offset);
                if (ActiveResultMode == ResultMode.Saved && Results is not null)
                {
                    await ReplaceSavedWorkspaceUrlAsync();
                }

                return;
            }

            if (ActiveResultMode == ResultMode.Transient && TryBuildQueryRequest(offset, out SmartCollectionQueryRequest? request))
            {
                await QueryTransientCoreAsync(request!);
                if (ActiveResultMode == ResultMode.Transient && Results is not null)
                {
                    await PersistTransientNavigationAsync();
                }
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task QuerySavedAsync(int offset)
    {
        Busy = true;
        try
        {
            await QuerySavedCoreAsync(offset);
            if (ActiveResultMode == ResultMode.Saved && Results is not null)
            {
                await ReplaceSavedWorkspaceUrlAsync();
            }
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task QuerySavedCoreAsync(int offset)
    {
        if (EditingId is null)
        {
            return;
        }

        Error = null;
        try
        {
            using HttpResponseMessage response = await Http.GetAsync(
                $"api/smart-collections/{EditingId}/query?offset={Math.Max(0, offset)}&limit={PageSize}");
            if (!response.IsSuccessStatusCode)
            {
                Error = await ReadErrorAsync(response, "The saved collection could not be evaluated.");
                return;
            }

            Results = await response.Content.ReadFromJsonAsync<SmartCollectionPageResponse>()
                ?? throw new InvalidOperationException("The smart collection query response was empty.");
            ActiveResultMode = ResultMode.Saved;
        }
        catch (Exception exception)
        {
            Error = $"The saved collection could not be evaluated: {exception.Message}";
        }
    }

    private async Task QueryTransientCoreAsync(SmartCollectionQueryRequest request)
    {
        Error = null;
        try
        {
            using HttpResponseMessage response = await Http.PostAsJsonAsync("api/smart-collections/query", request);
            if (!response.IsSuccessStatusCode)
            {
                Error = await ReadErrorAsync(response, "The smart collection preview could not be evaluated.");
                return;
            }

            Results = await response.Content.ReadFromJsonAsync<SmartCollectionPageResponse>()
                ?? throw new InvalidOperationException("The smart collection preview response was empty.");
            ActiveResultMode = ResultMode.Transient;
        }
        catch (Exception exception)
        {
            Error = $"The smart collection preview could not be evaluated: {exception.Message}";
        }
    }

    private async Task RestoreNavigationStateAsync()
    {
        int offset = Math.Max(0, NavigationOffset ?? 0);
        if (string.Equals(NavigationMode, "saved", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(NavigationCollectionId))
        {
            SmartCollectionDefinitionResponse? definition = Definitions.FirstOrDefault(
                candidate => string.Equals(candidate.Id, NavigationCollectionId, StringComparison.Ordinal));
            if (definition is null)
            {
                Notice = "The saved Smart Collection referenced by this browser history entry no longer exists.";
                return;
            }

            ApplyDefinition(definition);
            await QuerySavedCoreAsync(offset);
            return;
        }

        if (!string.Equals(NavigationMode, "transient", StringComparison.OrdinalIgnoreCase) ||
            !IsValidPreviewKey(NavigationPreviewKey))
        {
            return;
        }

        try
        {
            string previewKey = NavigationPreviewKey!;
            string storageKey = SmartCollectionNavigation.PreviewStorageKey(previewKey);
            string? json = await JS.InvokeAsync<string?>("sessionStorage.getItem", storageKey);
            if (string.IsNullOrWhiteSpace(json))
            {
                Notice = "This unsaved Smart Collection preview is no longer available in this browser tab.";
                return;
            }

            SmartCollectionTransientNavigationState? state =
                JsonSerializer.Deserialize<SmartCollectionTransientNavigationState>(json, NavigationJsonOptions);
            if (state is null)
            {
                Notice = "This unsaved Smart Collection preview could not be restored.";
                return;
            }

            ApplyTransientNavigationState(state);
            TransientPreviewKey = previewKey;
            if (TryBuildQueryRequest(offset, out SmartCollectionQueryRequest? request))
            {
                await QueryTransientCoreAsync(request!);
            }
        }
        catch (Exception exception) when (exception is JSException or JsonException)
        {
            Notice = $"This unsaved Smart Collection preview could not be restored: {exception.Message}";
        }
    }

    private async Task PersistTransientNavigationAsync()
    {
        if (Results is null)
        {
            return;
        }

        TransientPreviewKey ??= Guid.NewGuid().ToString("N");
        SmartCollectionTransientNavigationState state = new(
            EditingId,
            Name,
            SelectedPeople.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            PeopleMatch,
            SelectedTags.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            TagMatch,
            Taken,
            UseLocation,
            South,
            West,
            North,
            East,
            SelectedPlace);

        try
        {
            string json = JsonSerializer.Serialize(state, NavigationJsonOptions);
            await JS.InvokeVoidAsync(
                "sessionStorage.setItem",
                SmartCollectionNavigation.PreviewStorageKey(TransientPreviewKey),
                json);
            await ReplaceWorkspaceUrlAsync(
                SmartCollectionNavigation.BuildTransientWorkspaceUrl(TransientPreviewKey, Results.Offset));
        }
        catch (JSException exception)
        {
            Notice = $"The current preview is available, but its browser-tab return state could not be saved: {exception.Message}";
        }
    }

    private async Task ReplaceSavedWorkspaceUrlAsync()
    {
        if (EditingId is null || Results is null)
        {
            return;
        }

        await ReplaceWorkspaceUrlAsync(
            SmartCollectionNavigation.BuildSavedWorkspaceUrl(EditingId, Results.Offset));
    }

    private async Task ReplaceWorkspaceUrlAsync(string relativeUrl)
    {
        try
        {
            await JS.InvokeVoidAsync("history.replaceState", (object?)null, "", relativeUrl);
        }
        catch (JSException exception)
        {
            Notice = $"Browser history state could not be updated: {exception.Message}";
        }
    }

    private string PhotoHref(SmartCollectionPhotoResponse photo) =>
        SmartCollectionNavigation.BuildPhotoUrl(photo.RevisionId, CurrentWorkspaceReturnUrl);

    private static bool IsValidPreviewKey(string? value) =>
        value is not null && value.Length == 32 && Guid.TryParseExact(value, "N", out _);

    private bool TryBuildDefinitionRequest(out SmartCollectionDefinitionRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "Enter a collection name.";
            return false;
        }

        if (!TryBuildLocation(out SmartCollectionLocationRequest? location))
        {
            return false;
        }

        if (EditingId is not null && location is null)
        {
            location = new SmartCollectionLocationRequest(Place: string.Empty);
        }

        request = new SmartCollectionDefinitionRequest(
            Name.Trim(),
            SelectedPeople.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            PeopleMatch,
            SelectedTags.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            TagMatch,
            location,
            string.IsNullOrWhiteSpace(Taken) ? null : Taken.Trim());
        return true;
    }

    private bool TryBuildQueryRequest(int offset, out SmartCollectionQueryRequest? request)
    {
        request = null;
        if (!TryBuildLocation(out SmartCollectionLocationRequest? location))
        {
            return false;
        }

        request = new SmartCollectionQueryRequest(
            SelectedPeople.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            PeopleMatch,
            SelectedTags.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            TagMatch,
            location,
            string.IsNullOrWhiteSpace(Taken) ? null : Taken.Trim(),
            Math.Max(0, offset),
            PageSize);
        return true;
    }

    private bool TryBuildLocation(out SmartCollectionLocationRequest? location)
    {
        string? place = string.IsNullOrWhiteSpace(SelectedPlace) ? null : SelectedPlace.Trim();
        location = null;
        if (!UseLocation)
        {
            if (place is not null)
            {
                location = new SmartCollectionLocationRequest(Place: place);
            }

            return true;
        }

        if (!TryCoordinate(South, -90, 90, "South latitude", out double south) ||
            !TryCoordinate(West, -180, 180, "West longitude", out double west) ||
            !TryCoordinate(North, -90, 90, "North latitude", out double north) ||
            !TryCoordinate(East, -180, 180, "East longitude", out double east))
        {
            return false;
        }

        if (south > north)
        {
            Error = "South latitude cannot be greater than north latitude.";
            return false;
        }

        if (west > east)
        {
            Error = "West longitude cannot be greater than east longitude.";
            return false;
        }

        location = new SmartCollectionLocationRequest(
            south,
            west,
            north,
            east,
            place ?? string.Empty);
        return true;
    }

    private bool TryCoordinate(string value, double minimum, double maximum, string label, out double parsed)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ||
            !double.IsFinite(parsed) || parsed < minimum || parsed > maximum)
        {
            Error = $"{label} must be between {minimum} and {maximum}.";
            return false;
        }

        return true;
    }

    private async Task RefreshDefinitionsAsync()
    {
        Definitions = await Http.GetFromJsonAsync<SmartCollectionDefinitionResponse[]>("api/smart-collections") ?? [];
    }

    private string PeopleSummary(SmartCollectionFilterResponse filter)
    {
        if (filter.People.Length == 0)
        {
            return "Any people";
        }

        string[] labels = filter.People
            .Select(id => People.FirstOrDefault(person => person.Id == id)?.DisplayName ?? id)
            .ToArray();
        return $"People {filter.PeopleMatch}: {string.Join(", ", labels)}";
    }

    private static string TagsSummary(SmartCollectionFilterResponse filter) => filter.Tags.Length == 0
        ? "Any tags"
        : $"Tags {filter.TagMatch}: {string.Join(", ", filter.Tags)}";

    private static string DateSummary(SmartCollectionFilterResponse filter) => filter.Taken is null
        ? "Any taken date"
        : $"Taken {filter.Taken.From} to {filter.Taken.To}";

    private static string LocationSummary(SmartCollectionFilterResponse filter)
    {
        if (filter.Location is null)
        {
            return "Any location";
        }

        string? place = string.IsNullOrWhiteSpace(filter.Location.Place)
            ? null
            : filter.Location.Place;
        bool hasGps = filter.Location.South.HasValue &&
            filter.Location.West.HasValue &&
            filter.Location.North.HasValue &&
            filter.Location.East.HasValue;

        if (place is not null && hasGps)
        {
            return $"Place {place} · GPS {filter.Location.South!.Value:G6},{filter.Location.West!.Value:G6} to {filter.Location.North!.Value:G6},{filter.Location.East!.Value:G6}";
        }

        if (place is not null)
        {
            return $"Place {place}";
        }

        return hasGps
            ? $"GPS {filter.Location.South!.Value:G6},{filter.Location.West!.Value:G6} to {filter.Location.North!.Value:G6},{filter.Location.East!.Value:G6}"
            : "Any location";
    }

    private static string ToEditableTaken(SmartCollectionDateRangeResponse? range)
    {
        if (range is null)
        {
            return "";
        }

        string from = range.From.Replace('-', '/');
        string to = range.To.Replace('-', '/');
        return string.Equals(from, to, StringComparison.Ordinal) ? from : $"{from}-{to}";
    }

    private static string PhotoDate(SmartCollectionPhotoResponse photo) => photo.TakenAtLocal is DateTime taken
        ? taken.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        : "Taken date unavailable";

    private static string PhotoLocation(SmartCollectionPhotoResponse photo) =>
        photo.Latitude is double latitude && photo.Longitude is double longitude
            ? $"{latitude:F5}, {longitude:F5}"
            : "Location unavailable";

    private static bool IsChecked(ChangeEventArgs args) => args.Value switch
    {
        bool value => value,
        string text when bool.TryParse(text, out bool value) => value,
        _ => false,
    };

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            SmartCollectionErrorResponse? error = await response.Content.ReadFromJsonAsync<SmartCollectionErrorResponse>();
            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return error.Error;
            }
        }
        catch
        {
            // Preserve the request-level fallback when the response has no structured error body.
        }

        return $"{fallback} Status {(int)response.StatusCode}.";
    }

    private enum ResultMode
    {
        None,
        Saved,
        Transient,
    }
}
