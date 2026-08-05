using Microsoft.AspNetCore.Components;
using PhotoIdentity.Web.Contracts;
using System.Globalization;

namespace PhotoIdentity.Web.Pages;

public partial class DetectorComparison
{
    private static RenderFragment MetricCard(string label, double recall, int matched, int countable) => builder =>
    {
        builder.OpenElement(0, "article");
        builder.OpenElement(1, "span"); builder.AddContent(2, label); builder.CloseElement();
        builder.OpenElement(3, "strong"); builder.AddContent(4, recall.ToString("P1", CultureInfo.CurrentCulture)); builder.CloseElement();
        builder.OpenElement(5, "small"); builder.AddContent(6, $"{matched} of {countable} faces"); builder.CloseElement();
        builder.CloseElement();
    };

    private static RenderFragment SummaryTable(string title, IReadOnlyList<DetectorEvaluationComparisonGroupSummaryResponse> groups) => builder =>
    {
        int sequence = 0;
        builder.OpenElement(sequence++, "article");
        builder.OpenElement(sequence++, "h2"); builder.AddContent(sequence++, title); builder.CloseElement();
        builder.OpenElement(sequence++, "div"); builder.AddAttribute(sequence++, "class", "comparison-table-wrap");
        builder.OpenElement(sequence++, "table");
        builder.OpenElement(sequence++, "thead");
        builder.AddMarkupContent(sequence++, "<tr><th>Group</th><th>Recall</th><th>Matched</th><th>Missed</th><th>False</th><th>Duplicate</th><th>Pending</th></tr>");
        builder.CloseElement();
        builder.OpenElement(sequence++, "tbody");
        foreach (var group in groups)
        {
            builder.OpenElement(sequence++, "tr");
            Cell(group.Group);
            Cell(group.Metrics.Recall.ToString("P1", CultureInfo.CurrentCulture));
            Cell(group.Metrics.MatchedFaces.ToString(CultureInfo.CurrentCulture));
            Cell(group.Metrics.MissedFaces.ToString(CultureInfo.CurrentCulture));
            Cell(group.Metrics.FalseDetections.ToString(CultureInfo.CurrentCulture));
            Cell(group.Metrics.DuplicateDetections.ToString(CultureInfo.CurrentCulture));
            Cell((group.Metrics.UnresolvedGroundTruthFaces + group.Metrics.UnresolvedCandidateDetections).ToString(CultureInfo.CurrentCulture));
            builder.CloseElement();
        }
        builder.CloseElement(); builder.CloseElement(); builder.CloseElement(); builder.CloseElement();
        void Cell(string value) { builder.OpenElement(sequence++, "td"); builder.AddContent(sequence++, value); builder.CloseElement(); }
    };

    private static IReadOnlyList<DetectorEvaluationComparisonGroundTruthFaceResponse> ExceptionGroundTruthFaces(DetectorEvaluationComparisonPhotoResponse photo) =>
        photo.ExceptionComponents.SelectMany(component => component.GroundTruthFaces)
            .DistinctBy(face => face.Id, StringComparer.OrdinalIgnoreCase).OrderBy(face => face.Id, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<DetectorEvaluationComparisonCandidateDetectionResponse> ExceptionCandidates(DetectorEvaluationComparisonPhotoResponse photo) =>
        photo.ExceptionComponents.SelectMany(component => component.CandidateDetections)
            .DistinctBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase).OrderBy(candidate => candidate.FaceNumber).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).ToArray();

    private static string CandidateAction(PhotoCorrectionDraft draft, string candidateId) =>
        draft.CandidateActions.TryGetValue(candidateId, out string? action) ? action : string.Empty;

    private static void SetCandidateAction(PhotoCorrectionDraft draft, string candidateId, string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) { draft.CandidateActions.Remove(candidateId); return; }
        draft.CandidateActions[candidateId] = action;
        if (action.StartsWith("match:", StringComparison.Ordinal)) draft.MissedGroundTruthFaceIds.Remove(action[6..]);
    }

    private static void SetMissed(PhotoCorrectionDraft draft, string groundTruthId, bool missed)
    {
        if (missed) draft.MissedGroundTruthFaceIds.Add(groundTruthId);
        else draft.MissedGroundTruthFaceIds.Remove(groundTruthId);
    }

    private static bool IsGroundTruthMatched(PhotoCorrectionDraft draft, string groundTruthId) =>
        draft.CandidateActions.Values.Any(action => string.Equals(action, $"match:{groundTruthId}", StringComparison.OrdinalIgnoreCase));

    private static string ResolutionLabel(DetectorEvaluationComparisonPhotoResponse photo, PhotoCorrectionDraft draft)
    {
        int matchedGroundTruth = draft.CandidateActions.Values.Count(value => value.StartsWith("match:", StringComparison.Ordinal));
        return $"Candidate nodes {draft.CandidateActions.Count}/{ExceptionCandidates(photo).Count} · ground-truth nodes {matchedGroundTruth + draft.MissedGroundTruthFaceIds.Count}/{ExceptionGroundTruthFaces(photo).Count}";
    }

    private static string GateDetail(DetectorEvaluationM16GateResponse gate) => gate.Status switch
    {
        "pending" when !gate.IsComparisonComplete => "exception review incomplete",
        "pending" => "material category assessment pending",
        "pass" => "all four decision criteria pass",
        _ => "one or more decision criteria fail",
    };

    private static string BoxStyle(DetectorEvaluationBoundingBoxResponse box) =>
        FormattableString.Invariant($"left:{box.X * 100:0.####}%;top:{box.Y * 100:0.####}%;width:{box.Width * 100:0.####}%;height:{box.Height * 100:0.####}%;");

    private static string ShortId(string id) => id.Length <= 8 ? id : id[..8];

    private sealed class PhotoCorrectionDraft
    {
        public Dictionary<string, string> CandidateActions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MissedGroundTruthFaceIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Notes { get; set; }
    }
}
