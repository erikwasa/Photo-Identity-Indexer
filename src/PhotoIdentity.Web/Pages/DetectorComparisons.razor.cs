using Microsoft.AspNetCore.Components;
using PhotoIdentity.Web.Contracts;
using System.Net.Http.Json;
using System.Text.Json;

namespace PhotoIdentity.Web.Pages;

public partial class DetectorComparisons
{
    private List<DetectorEvaluationRunResponse> Runs { get; } = [];
    private List<DetectorEvaluationSessionSummaryResponse> Sessions { get; } = [];
    private List<DetectorEvaluationGroundTruthSummaryResponse> GroundTruthSnapshots { get; } = [];
    private List<DetectorEvaluationComparisonSummaryResponse> Comparisons { get; } = [];
    private string SelectedBaselineSessionId { get; set; } = string.Empty;
    private string SelectedCandidateRunId { get; set; } = string.Empty;
    private string ComparisonName { get; set; } = string.Empty;
    private double IouThreshold { get; set; } = 0.5;
    private bool Loading { get; set; }
    private bool Busy { get; set; }
    private string? Error { get; set; }
    private string? Success { get; set; }

    private DetectorEvaluationSessionSummaryResponse? SelectedBaselineSession =>
        Sessions.FirstOrDefault(session => string.Equals(session.Id, SelectedBaselineSessionId, StringComparison.OrdinalIgnoreCase));

    private bool CanCreateComparison =>
        !string.IsNullOrWhiteSpace(ComparisonName) &&
        !string.IsNullOrWhiteSpace(SelectedCandidateRunId) &&
        GroundTruthFor(SelectedBaselineSessionId) is not null &&
        IouThreshold is > 0 and <= 1;

    protected override async Task OnInitializedAsync()
    {
        Loading = true;
        try
        {
            await LoadWorkspaceAsync();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    private async Task LoadWorkspaceAsync()
    {
        var runs = await Http.GetFromJsonAsync<DetectorEvaluationRunResponse[]>("/api/detector-evaluation/runs");
        var sessions = await Http.GetFromJsonAsync<DetectorEvaluationSessionSummaryResponse[]>("/api/detector-evaluation/sessions");
        var snapshots = await Http.GetFromJsonAsync<DetectorEvaluationGroundTruthSummaryResponse[]>("/api/detector-evaluation/ground-truth");
        var comparisons = await Http.GetFromJsonAsync<DetectorEvaluationComparisonSummaryResponse[]>("/api/detector-evaluation/comparisons");
        Replace(Runs, runs);
        Replace(Sessions, sessions);
        Replace(GroundTruthSnapshots, snapshots);
        Replace(Comparisons, comparisons);
        SelectedCandidateRunId = Runs.FirstOrDefault()?.Id ?? string.Empty;
        SelectedBaselineSessionId = GroundTruthSnapshots.FirstOrDefault()?.BaselineSessionId ?? Sessions.FirstOrDefault()?.Id ?? string.Empty;
    }

    private async Task FreezeGroundTruthAsync()
    {
        if (SelectedBaselineSession is null) return;
        await RunBusyAsync(async () =>
        {
            using HttpResponseMessage response = await Http.PostAsync($"/api/detector-evaluation/sessions/{SelectedBaselineSession.Id}/ground-truth", null);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response));
            Success = "Reusable face-level ground truth was frozen. The candidate catalogue can now be opened without the baseline run.";
            await LoadWorkspaceAsync();
        });
    }

    private async Task CreateComparisonAsync()
    {
        if (!CanCreateComparison) return;
        await RunBusyAsync(async () =>
        {
            var request = new CreateDetectorEvaluationComparisonRequest(ComparisonName.Trim(), SelectedBaselineSessionId, SelectedCandidateRunId, IouThreshold);
            using HttpResponseMessage response = await Http.PostAsJsonAsync("/api/detector-evaluation/comparisons", request);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(response));
            var created = await response.Content.ReadFromJsonAsync<DetectorEvaluationComparisonResponse>()
                ?? throw new InvalidOperationException("The comparison response was empty.");
            Navigation.NavigateTo($"/detector-comparisons/{created.Id}");
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

    private DetectorEvaluationGroundTruthSummaryResponse? GroundTruthFor(string sessionId) =>
        GroundTruthSnapshots.FirstOrDefault(snapshot => string.Equals(snapshot.BaselineSessionId, sessionId, StringComparison.OrdinalIgnoreCase));

    private static string RunLabel(DetectorEvaluationRunResponse run) =>
        $"{run.StartedAtUtc.ToLocalTime():g} · {run.PhotoCount} photos · {run.DetectionCount} detections · {run.Status}";

    private void OpenComparison(string comparisonId) => Navigation.NavigateTo($"/detector-comparisons/{comparisonId}");

    private static void Replace<T>(List<T> target, IEnumerable<T>? values)
    {
        target.Clear();
        if (values is not null) target.AddRange(values);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out JsonElement error))
                return error.GetString() ?? response.ReasonPhrase ?? "Request failed.";
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(payload) ? response.ReasonPhrase ?? "Request failed." : payload;
    }
}
