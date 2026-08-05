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

    private static int ReferenceFaceNumber(DetectorEvaluationComparisonPhotoResponse photo, string groundTruthId)
    {
        IReadOnlyList<DetectorEvaluationComparisonGroundTruthFaceResponse> faces = ExceptionGroundTruthFaces(photo);
        for (int index = 0; index < faces.Count; index++)
        {
            if (string.Equals(faces[index].Id, groundTruthId, StringComparison.OrdinalIgnoreCase)) return index + 1;
        }

        return 0;
    }

    private static string ReferenceFaceLabel(DetectorEvaluationComparisonPhotoResponse photo, string groundTruthId) =>
        $"Reference face {ReferenceFaceNumber(photo, groundTruthId)}";

    private static string GroundTruthOriginLabel(string origin) => origin switch
    {
        "manual-miss" => "manually marked in the baseline review",
        "accepted-detection" => "accepted baseline detection",
        _ => origin.Replace('-', ' '),
    };

    private static string ComponentTitle(DetectorEvaluationComparisonExceptionComponentResponse component) => component.Kind switch
    {
        "unmatched" when component.CandidateDetections.Count > 0 && component.GroundTruthFaces.Count == 0 => "Extra candidate detection",
        "unmatched" when component.CandidateDetections.Count == 0 && component.GroundTruthFaces.Count > 0 => "Reference face without an automatic match",
        "duplicate" => "Possible duplicate detection",
        "ambiguous" => "Possible match needs review",
        _ => "Review required",
    };

    private static string ComponentHelp(DetectorEvaluationComparisonExceptionComponentResponse component) => component.Kind switch
    {
        "unmatched" when component.CandidateDetections.Count > 0 && component.GroundTruthFaces.Count == 0 =>
            "The evaluated detector found this box, but it did not automatically match a reference face. Choose whether it is a real face, a false detection or a duplicate.",
        "unmatched" when component.CandidateDetections.Count == 0 && component.GroundTruthFaces.Count > 0 =>
            "No candidate detection automatically matched this reference face. Check the box only when the evaluated detector truly missed it.",
        "duplicate" => "More than one candidate detection may refer to the same reference face. Match the correct detection and mark any additional detection as a duplicate.",
        "ambiguous" => "Several boxes overlap in a way that cannot be resolved automatically. Match each real candidate detection to the correct reference face.",
        _ => "Resolve every candidate detection and reference face shown below.",
    };

    private static int CompletedDecisionCount(DetectorEvaluationComparisonPhotoResponse photo, PhotoCorrectionDraft draft)
    {
        int matchedGroundTruth = draft.CandidateActions.Values.Count(value => value.StartsWith("match:", StringComparison.Ordinal));
        return draft.CandidateActions.Count + matchedGroundTruth + draft.MissedGroundTruthFaceIds.Count;
    }

    private static int TotalDecisionCount(DetectorEvaluationComparisonPhotoResponse photo) =>
        ExceptionCandidates(photo).Count + ExceptionGroundTruthFaces(photo).Count;

    private static bool IsDraftResolved(DetectorEvaluationComparisonPhotoResponse photo, PhotoCorrectionDraft draft) =>
        CompletedDecisionCount(photo, draft) == TotalDecisionCount(photo);

    private static string ResolutionLabel(DetectorEvaluationComparisonPhotoResponse photo, PhotoCorrectionDraft draft)
    {
        int completed = CompletedDecisionCount(photo, draft);
        int total = TotalDecisionCount(photo);
        return completed == total ? "All decisions complete" : $"{completed} of {total} decisions complete";
    }

    private static string GateDetail(DetectorEvaluationM16GateResponse gate) => gate.Status switch
    {
        "pending" when !gate.IsComparisonComplete => "exception review incomplete",
        "pending" => "material category assessment pending",
        "pass" => "all four decision criteria pass",
        _ => "one or more decision criteria fail",
    };

    private static string GateStatusClass(string status) => status switch
    {
        "pass" => "status-success",
        "fail" => "status-failure",
        _ => "status-pending",
    };

    private static string BoxStyle(DetectorEvaluationBoundingBoxResponse box) =>
        FormattableString.Invariant($"left:{box.X * 100:0.####}%;top:{box.Y * 100:0.####}%;width:{box.Width * 100:0.####}%;height:{box.Height * 100:0.####}%;");

    private sealed class PhotoCorrectionDraft
    {
        public Dictionary<string, string> CandidateActions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MissedGroundTruthFaceIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Notes { get; set; }
    }
}
