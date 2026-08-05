using System.Globalization;
using System.Text;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;


public static partial class DetectorEvaluationComparisonEndpoints
{
    private static DetectorEvaluationM16GateResponse CalculateGate(
        StoredDetectorEvaluationComparison comparison)
    {
        DetectorEvaluationComparisonMetricsResponse overall = CalculateMetrics(comparison.Photos);
        DetectorEvaluationComparisonMetricsResponse fivePlus = CalculateMetrics(
            comparison.Photos.Where(photo => photo.CountableFaces >= 5));
        bool complete = overall.UnresolvedGroundTruthFaces == 0 && overall.UnresolvedCandidateDetections == 0;
        bool overallPass = overall.Recall + 1e-12 >= OverallRecallTarget;
        bool fivePlusPass = fivePlus.Recall + 1e-12 >= FivePlusRecallTarget;
        bool falseOrDuplicatePass =
            overall.FalseDetections + overall.DuplicateDetections <= FalseOrDuplicateLimit;
        bool? materialPass = comparison.MaterialCategoryFailure.HasValue
            ? !comparison.MaterialCategoryFailure.Value
            : null;
        string status = !complete || materialPass is null
            ? "pending"
            : overallPass && fivePlusPass && falseOrDuplicatePass && materialPass.Value
                ? "pass"
                : "fail";
        return new DetectorEvaluationM16GateResponse(
            status,
            complete,
            comparison.MaterialCategoryFailure,
            comparison.GateNotes,
            OverallRecallTarget,
            FivePlusRecallTarget,
            FalseOrDuplicateLimit,
            overallPass,
            fivePlusPass,
            falseOrDuplicatePass,
            materialPass);
    }

    private static bool IsResolved(StoredDetectorEvaluationComparisonPhoto photo)
    {
        int exceptionGroundTruthCount = photo.ExceptionComponents.Sum(component => component.GroundTruthFaceIds.Count);
        int exceptionCandidateCount = photo.ExceptionComponents.Sum(component => component.CandidateDetectionIds.Count);
        int resolvedGroundTruth = photo.Correction.Matches.Count + photo.Correction.MissedGroundTruthFaceIds.Count;
        int resolvedCandidates = photo.Correction.Matches.Count +
                                 photo.Correction.FalseCandidateDetectionIds.Count +
                                 photo.Correction.DuplicateCandidateDetectionIds.Count;
        return exceptionGroundTruthCount == resolvedGroundTruth &&
               exceptionCandidateCount == resolvedCandidates;
    }

    private static BaselinePhotoMetrics CalculateBaselineMetrics(StoredDetectorEvaluationPhoto photo)
    {
        int correct = photo.Detections.Count(detection =>
            DetectorEvaluationDispositions.CountsAsCorrect(detection.Disposition));
        bool everyDetectionClassified = photo.Detections.All(detection =>
            DetectorEvaluationDispositions.IsValid(detection.Disposition));
        bool arithmeticMatches = photo.CountableFaces == correct + photo.MissedFaces.Count;
        return new BaselinePhotoMetrics(everyDetectionClassified && arithmeticMatches);
    }

    private static async Task<IReadOnlyList<CatalogueDetectorEvaluationPhoto>> LoadRunPhotosAsync(
        SqliteDetectorEvaluationRepository repository,
        ProcessingRunId runId,
        CancellationToken cancellationToken)
    {
        CatalogueDetectorEvaluationPhotoPage page = await repository.GetPhotosAsync(
            runId,
            offset: 0,
            limit: 1000,
            cancellationToken);
        if (page.Items.Count != page.Total)
        {
            throw new InvalidOperationException(
                "Detector comparisons currently support at most 1000 photos per processing run.");
        }

        return page.Items;
    }

    private static void AppendMetrics(
        StringBuilder csv,
        string scope,
        string group,
        DetectorEvaluationComparisonMetricsResponse metrics)
    {
        string[] values =
        [
            scope,
            group,
            metrics.PhotoCount.ToString(CultureInfo.InvariantCulture),
            metrics.CountableFaces.ToString(CultureInfo.InvariantCulture),
            metrics.MatchedFaces.ToString(CultureInfo.InvariantCulture),
            metrics.MissedFaces.ToString(CultureInfo.InvariantCulture),
            metrics.UnresolvedGroundTruthFaces.ToString(CultureInfo.InvariantCulture),
            metrics.Recall.ToString("P2", CultureInfo.InvariantCulture),
            metrics.FalseDetections.ToString(CultureInfo.InvariantCulture),
            metrics.DuplicateDetections.ToString(CultureInfo.InvariantCulture),
            metrics.UnresolvedCandidateDetections.ToString(CultureInfo.InvariantCulture),
        ];
        csv.AppendLine(string.Join(',', values.Select(EscapeCsv)));
    }

    private static string EscapeCsv(string value)
    {
        if (!value.ContainsAny([',', '"', '\r', '\n']))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static bool TryParseIdentifier(string value, out Guid parsed) =>
        Guid.TryParse(value, out parsed) && parsed != Guid.Empty;

    private sealed record BaselinePhotoMetrics(bool IsComplete);
}
