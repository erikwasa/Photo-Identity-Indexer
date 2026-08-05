using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PhotoIdentity.Web.Contracts;
using System.Net.Http.Json;
using System.Text.Json;

namespace PhotoIdentity.Web.Pages;

public partial class DetectorComparison
{
    [Parameter] public Guid ComparisonId { get; set; }

    private Dictionary<string, PhotoCorrectionDraft> Drafts { get; } = new(StringComparer.OrdinalIgnoreCase);
    private DetectorComparisonViewState ViewState { get; } = new();
    private DetectorEvaluationComparisonResponse? Comparison { get; set; }
    private ElementReference WorkspaceElement { get; set; }
    private ElementReference ViewportElement { get; set; }
    private ElementReference StageElement { get; set; }
    private ElementReference ImageElement { get; set; }
    private ElementReference DecisionPanelElement { get; set; }
    private int CurrentPhotoIndex { get; set; }
    private DetectorEvaluationComparisonPhotoResponse? CurrentPhoto =>
        Comparison is not null && CurrentPhotoIndex >= 0 && CurrentPhotoIndex < Comparison.ExceptionPhotos.Count
            ? Comparison.ExceptionPhotos[CurrentPhotoIndex]
            : null;
    private string MaterialCategoryValue { get; set; } = string.Empty;
    private string? GateNotes { get; set; }
    private bool Loading { get; set; }
    private bool Busy { get; set; }
    private bool ApplyViewAfterRender { get; set; }
    private string? Error { get; set; }
    private string? Success { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        Loading = true;
        Error = null;
        try { await LoadComparisonAsync(); }
        catch (Exception exception) { Error = exception.Message; }
        finally { Loading = false; }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!ApplyViewAfterRender)
        {
            return;
        }

        ApplyViewAfterRender = false;
        await ApplyViewAsync(resetWorkspace: true);
    }

    private async Task LoadComparisonAsync(string? preferredRevisionId = null, int? fallbackIndex = null)
    {
        Comparison = await Http.GetFromJsonAsync<DetectorEvaluationComparisonResponse>($"/api/detector-evaluation/comparisons/{ComparisonId:D}");
        Drafts.Clear();
        if (Comparison is null) return;
        MaterialCategoryValue = Comparison.M16Gate.MaterialCategoryFailure switch { true => "true", false => "false", null => string.Empty };
        GateNotes = Comparison.M16Gate.Notes;
        foreach (var photo in Comparison.ExceptionPhotos)
        {
            var draft = new PhotoCorrectionDraft { Notes = photo.Correction.Notes };
            foreach (var match in photo.Correction.Matches) draft.CandidateActions[match.CandidateDetectionId] = $"match:{match.GroundTruthFaceId}";
            foreach (string id in photo.Correction.FalseCandidateDetectionIds) draft.CandidateActions[id] = "false";
            foreach (string id in photo.Correction.DuplicateCandidateDetectionIds) draft.CandidateActions[id] = "duplicate";
            draft.MissedGroundTruthFaceIds.UnionWith(photo.Correction.MissedGroundTruthFaceIds);
            draft.MissedGroundTruthFaceIds.UnionWith(AutomaticMissedGroundTruthFaceIds(photo));
            Drafts[photo.CandidateRevisionId] = draft;
        }

        if (Comparison.ExceptionPhotos.Count == 0)
        {
            CurrentPhotoIndex = 0;
            ResetViewState();
            return;
        }

        int preferredIndex = preferredRevisionId is null
            ? -1
            : Comparison.ExceptionPhotos.FindIndex(photo => string.Equals(photo.CandidateRevisionId, preferredRevisionId, StringComparison.OrdinalIgnoreCase));
        CurrentPhotoIndex = preferredIndex >= 0
            ? preferredIndex
            : Math.Clamp(fallbackIndex ?? CurrentPhotoIndex, 0, Comparison.ExceptionPhotos.Count - 1);
        ResetViewState();
    }

    private void ShowPreviousPhoto()
    {
        if (CurrentPhotoIndex > 0)
        {
            CurrentPhotoIndex--;
            ResetViewState();
        }

        Success = null;
        Error = null;
    }

    private void ShowNextPhoto()
    {
        if (Comparison is not null && CurrentPhotoIndex < Comparison.ExceptionPhotos.Count - 1)
        {
            CurrentPhotoIndex++;
            ResetViewState();
        }

        Success = null;
        Error = null;
    }

    private async Task HandleImageLoadedAsync() => await ApplyViewAsync(resetWorkspace: false);

    private async Task SetZoomAsync(double? scale)
    {
        ViewState.SetZoom(scale);
        await ApplyViewAsync(resetWorkspace: false);
    }

    private async Task ZoomInAsync()
    {
        ViewState.ZoomIn();
        await ApplyViewAsync(resetWorkspace: false);
    }

    private async Task ZoomOutAsync()
    {
        ViewState.ZoomOut();
        await ApplyViewAsync(resetWorkspace: false);
    }

    private void SetActiveReviewKey(string reviewKey) => ViewState.Activate(reviewKey);

    private void ClearActiveReviewKey(string reviewKey) => ViewState.Clear(reviewKey);

    private string ReviewActiveClass(string reviewKey) => ViewState.IsActive(reviewKey) ? "active-review" : string.Empty;

    private async Task FocusDecisionAsync(string reviewKey, string elementId)
    {
        ViewState.Activate(reviewKey);
        try
        {
            await JS.InvokeVoidAsync("detectorComparison.focusDecision", elementId);
        }
        catch (Exception exception)
        {
            Error = $"The review decision could not be focused: {exception.Message}";
        }
    }

    private void ResetViewState()
    {
        ViewState.Reset();
        ApplyViewAfterRender = true;
    }

    private async Task ApplyViewAsync(bool resetWorkspace)
    {
        if (CurrentPhoto is null)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync(
                "detectorComparison.applyZoom",
                ViewportElement,
                StageElement,
                ImageElement,
                ViewState.ZoomScale ?? 0);
            if (resetWorkspace)
            {
                await JS.InvokeVoidAsync(
                    "detectorComparison.resetWorkspace",
                    WorkspaceElement,
                    ViewportElement,
                    DecisionPanelElement);
            }
        }
        catch (Exception exception)
        {
            Error = $"The comparison image view could not be applied: {exception.Message}";
        }
    }

    private async Task SavePhotoAsync(DetectorEvaluationComparisonPhotoResponse photo, PhotoCorrectionDraft draft, bool moveNext)
    {
        int savedIndex = CurrentPhotoIndex;
        string? nextRevisionId = moveNext && Comparison is not null && savedIndex + 1 < Comparison.ExceptionPhotos.Count
            ? Comparison.ExceptionPhotos[savedIndex + 1].CandidateRevisionId
            : photo.CandidateRevisionId;

        await RunBusyAsync(async () =>
        {
            List<DetectorEvaluationComparisonManualMatchRequest> matches = [];
            List<string> falseDetections = [];
            List<string> duplicates = [];
            foreach ((string candidateId, string action) in draft.CandidateActions)
            {
                if (action.StartsWith("match:", StringComparison.Ordinal)) matches.Add(new(action[6..], candidateId));
                else if (action == "false") falseDetections.Add(candidateId);
                else if (action == "duplicate") duplicates.Add(candidateId);
            }
            var request = new SaveDetectorEvaluationComparisonPhotoRequest(matches, falseDetections, duplicates, draft.MissedGroundTruthFaceIds.ToArray(), draft.Notes);
            using HttpResponseMessage response = await Http.PutAsJsonAsync($"/api/detector-evaluation/comparisons/{Comparison!.Id}/photos/{photo.CandidateRevisionId}", request);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response));
            Success = moveNext ? $"Saved {photo.PhotoName}." : $"Corrections saved for {photo.PhotoName}.";
            await LoadComparisonAsync(nextRevisionId, savedIndex);
        });
    }

    private async Task SaveGateAsync()
    {
        if (Comparison is null) return;
        await RunBusyAsync(async () =>
        {
            bool? materialFailure = MaterialCategoryValue switch { "true" => true, "false" => false, _ => null };
            using HttpResponseMessage response = await Http.PutAsJsonAsync($"/api/detector-evaluation/comparisons/{Comparison.Id}/m16-gate", new SaveDetectorEvaluationM16GateRequest(materialFailure, GateNotes));
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response));
            Success = "M16 gate assessment saved.";
            await LoadComparisonAsync(CurrentPhoto?.CandidateRevisionId, CurrentPhotoIndex);
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        Busy = true;
        Error = null;
        Success = null;
        try { await action(); }
        catch (Exception exception) { Error = exception.Message; }
        finally { Busy = false; }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out JsonElement error)) return error.GetString() ?? response.ReasonPhrase ?? "Request failed.";
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(payload) ? response.ReasonPhrase ?? "Request failed." : payload;
    }
}

internal static class DetectorComparisonPhotoListExtensions
{
    public static int FindIndex(this IReadOnlyList<DetectorEvaluationComparisonPhotoResponse> photos, Func<DetectorEvaluationComparisonPhotoResponse, bool> predicate)
    {
        for (int index = 0; index < photos.Count; index++)
        {
            if (predicate(photos[index])) return index;
        }

        return -1;
    }
}
