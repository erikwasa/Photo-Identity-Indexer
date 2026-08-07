using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Web.Pages;

public partial class DetectorRollout
{
    private readonly Dictionary<string, ReviewDraft> _drafts = new(StringComparer.Ordinal);
    private string RunInput { get; set; } = string.Empty;
    private string Actor { get; set; } = "local-maintainer";
    private DetectorRolloutRunResponse? Summary { get; set; }
    private List<DetectorRolloutPendingReviewResponse> Pending { get; } = [];
    private bool Loading { get; set; }
    private bool Busy { get; set; }
    private string? Error { get; set; }
    private string? Success { get; set; }

    [Parameter]
    public string? RunId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (string.IsNullOrWhiteSpace(RunId))
        {
            Summary = null;
            Pending.Clear();
            _drafts.Clear();
            return;
        }

        Loading = true;
        Error = null;
        try
        {
            await LoadAsync();
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

    private void OpenRun()
    {
        if (Guid.TryParse(RunInput.Trim(), out Guid parsed) && parsed != Guid.Empty)
        {
            Navigation.NavigateTo($"/detector-rollout/{parsed:D}");
        }
        else
        {
            Error = "Enter a valid rollout run ID.";
        }
    }

    private async Task LoadAsync()
    {
        DetectorRolloutRunResponse summary = await Http.GetFromJsonAsync<DetectorRolloutRunResponse>(
            $"/api/detector-rollout/runs/{RunId}")
            ?? throw new InvalidOperationException("The rollout status response was empty.");
        DetectorRolloutPendingReviewResponse[] pending = await Http.GetFromJsonAsync<DetectorRolloutPendingReviewResponse[]>(
            $"/api/detector-rollout/runs/{RunId}/pending")
            ?? [];
        Summary = summary;
        Pending.Clear();
        Pending.AddRange(pending);

        _drafts.Clear();
        foreach (DetectorRolloutPendingReviewResponse item in Pending)
        {
            string key = Key(item);
            string action = item.LatestResolution?.Kind switch
            {
                "existing" when item.LatestResolution.FaceOccurrenceId is string faceId => $"existing:{faceId}",
                "new" => "new",
                "defer" => "defer",
                _ => string.Empty,
            };
            _drafts[key] = new ReviewDraft(action, item.LatestResolution?.Note ?? string.Empty);
        }
    }

    private ReviewDraft DraftFor(DetectorRolloutPendingReviewResponse item) => _drafts[Key(item)];

    private async Task SaveAsync(DetectorRolloutPendingReviewResponse item)
    {
        ReviewDraft draft = DraftFor(item);
        if (string.IsNullOrWhiteSpace(draft.Action))
        {
            Error = "Choose an existing face, new face, or defer before saving.";
            return;
        }

        string action;
        string? faceOccurrenceId = null;
        if (draft.Action.StartsWith("existing:", StringComparison.Ordinal))
        {
            action = "existing";
            faceOccurrenceId = draft.Action["existing:".Length..];
        }
        else
        {
            action = draft.Action;
        }

        await RunBusyAsync(async () =>
        {
            SaveDetectorRolloutResolutionRequest request = new(
                action,
                faceOccurrenceId,
                Actor,
                string.IsNullOrWhiteSpace(draft.Note) ? null : draft.Note.Trim());
            using HttpResponseMessage response = await Http.PostAsJsonAsync(
                $"/api/detector-rollout/runs/{item.ProcessingRunId}/revisions/{item.AssetRevisionId}/candidates/{item.CandidateIndex}/resolve",
                request);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(await ReadErrorAsync(response));
            }

            Success = action == "defer"
                ? "Decision deferred. The candidate remains unresolved and will not be applied."
                : "Occurrence reconciliation saved. Run 'rollout apply' when the review queue is ready.";
            await LoadAsync();
        });
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        Busy = true;
        Error = null;
        Success = null;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private static string Key(DetectorRolloutPendingReviewResponse item) =>
        $"{item.AssetRevisionId}:{item.CandidateIndex}";

    private static string ShortRevision(string value) =>
        value[..Math.Min(12, value.Length)];

    private static string Percent(double value) =>
        (value * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    private static string CandidateStyle(DetectorRolloutBoxResponse box) =>
        $"left:{Percent(box.X)};top:{Percent(box.Y)};width:{Percent(box.Width)};height:{Percent(box.Height)}";

    private static string OptionLabel(DetectorRolloutOptionResponse option) =>
        option.PersonDisplayName is null
            ? $"Face {option.Ordinal + 1} · {option.ReviewState}"
            : $"Face {option.Ordinal + 1} · {option.PersonDisplayName} · {option.ReviewState}";

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out JsonElement error))
            {
                return error.GetString() ?? response.ReasonPhrase ?? "Request failed.";
            }
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(payload)
            ? response.ReasonPhrase ?? "Request failed."
            : payload;
    }

    private sealed class ReviewDraft
    {
        public ReviewDraft(string action, string note)
        {
            Action = action;
            Note = note;
        }

        public string Action { get; set; }
        public string Note { get; set; }
    }
}
