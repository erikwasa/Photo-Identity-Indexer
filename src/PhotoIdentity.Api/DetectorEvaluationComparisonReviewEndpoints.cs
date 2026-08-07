using System.Globalization;
using System.Text;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;


public static partial class DetectorEvaluationComparisonEndpoints
{
    private static async Task<IResult> GetComparisonAsync(
        string comparisonId,
        DetectorEvaluationComparisonStore store,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(comparisonId, out Guid parsedComparisonId))
        {
            return Results.BadRequest(new { error = "The detector comparison identifier is invalid." });
        }

        StoredDetectorEvaluationComparison? comparison = await store.GetAsync(parsedComparisonId, cancellationToken);
        return comparison is null ? Results.NotFound() : Results.Ok(ToResponse(comparison));
    }

    private static async Task<IResult> SavePhotoCorrectionAsync(
        string comparisonId,
        string revisionId,
        SaveDetectorEvaluationComparisonPhotoRequest request,
        DetectorEvaluationComparisonStore store,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(comparisonId, out Guid parsedComparisonId))
        {
            return Results.BadRequest(new { error = "The detector comparison identifier is invalid." });
        }

        if (!TryParseIdentifier(revisionId, out Guid parsedRevisionId))
        {
            return Results.BadRequest(new { error = "The candidate revision identifier is invalid." });
        }

        if (request.Matches is null || request.FalseCandidateDetectionIds is null ||
            request.DuplicateCandidateDetectionIds is null || request.NeutralCandidateDetectionIds is null ||
            request.MissedGroundTruthFaceIds is null)
        {
            return Results.BadRequest(new { error = "Every correction collection is required." });
        }

        try
        {
            DetectorEvaluationComparisonCorrectionUpdate update = new(
                request.Matches.Select(match => new StoredDetectorEvaluationManualMatch
                {
                    GroundTruthFaceId = match.GroundTruthFaceId,
                    CandidateDetectionId = match.CandidateDetectionId,
                }).ToArray(),
                request.FalseCandidateDetectionIds,
                request.DuplicateCandidateDetectionIds,
                request.MissedGroundTruthFaceIds,
                request.Notes)
            {
                NeutralCandidateDetectionIds = request.NeutralCandidateDetectionIds,
            };
            StoredDetectorEvaluationComparison? comparison = await store.SavePhotoCorrectionAsync(
                parsedComparisonId,
                parsedRevisionId.ToString("D"),
                update,
                cancellationToken);
            return comparison is null ? Results.NotFound() : Results.Ok(ToResponse(comparison));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
    }

    private static async Task<IResult> SaveGateAssessmentAsync(
        string comparisonId,
        SaveDetectorEvaluationM16GateRequest request,
        DetectorEvaluationComparisonStore store,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(comparisonId, out Guid parsedComparisonId))
        {
            return Results.BadRequest(new { error = "The detector comparison identifier is invalid." });
        }

        StoredDetectorEvaluationComparison? comparison = await store.SaveGateAssessmentAsync(
            parsedComparisonId,
            request.MaterialCategoryFailure,
            request.Notes,
            cancellationToken);
        return comparison is null ? Results.NotFound() : Results.Ok(ToResponse(comparison));
    }

    private static async Task<IResult> ExportComparisonCsvAsync(
        string comparisonId,
        DetectorEvaluationComparisonStore store,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(comparisonId, out Guid parsedComparisonId))
        {
            return Results.BadRequest(new { error = "The detector comparison identifier is invalid." });
        }

        StoredDetectorEvaluationComparison? comparison = await store.GetAsync(parsedComparisonId, cancellationToken);
        if (comparison is null)
        {
            return Results.NotFound();
        }

        DetectorEvaluationComparisonResponse response = ToResponse(comparison);
        StringBuilder csv = new();
        csv.AppendLine("Scope,Group,Photos,Countable Faces,Matched Faces,Missed Faces,Unresolved Ground Truth,Recall,False Detections,Duplicate Detections,Neutral Detections,Unresolved Candidate Detections");
        AppendMetrics(csv, "Overall", "All photos", response.Overall);
        AppendMetrics(csv, "Five-plus-face", "Photos with at least five countable faces", response.FivePlusFaces);
        foreach (DetectorEvaluationComparisonGroupSummaryResponse group in response.SourceGroups)
        {
            AppendMetrics(csv, "Source group", group.Group, group.Metrics);
        }

        foreach (DetectorEvaluationComparisonGroupSummaryResponse group in response.Categories)
        {
            AppendMetrics(csv, "Category", group.Group, group.Metrics);
        }

        csv.AppendLine();
        csv.AppendLine("M16 Gate,Status,Target,Observed,Pass");
        csv.AppendLine(string.Join(',', new[]
        {
            EscapeCsv("Overall recall"),
            EscapeCsv(response.M16Gate.Status),
            EscapeCsv(OverallRecallTarget.ToString("P0", CultureInfo.InvariantCulture)),
            EscapeCsv(response.Overall.Recall.ToString("P2", CultureInfo.InvariantCulture)),
            EscapeCsv(response.M16Gate.OverallRecallPass ? "YES" : "NO"),
        }));
        csv.AppendLine(string.Join(',', new[]
        {
            EscapeCsv("Five-plus-face recall"),
            EscapeCsv(response.M16Gate.Status),
            EscapeCsv(FivePlusRecallTarget.ToString("P0", CultureInfo.InvariantCulture)),
            EscapeCsv(response.FivePlusFaces.Recall.ToString("P2", CultureInfo.InvariantCulture)),
            EscapeCsv(response.M16Gate.FivePlusRecallPass ? "YES" : "NO"),
        }));
        int falseOrDuplicate = response.Overall.FalseDetections + response.Overall.DuplicateDetections;
        csv.AppendLine(string.Join(',', new[]
        {
            EscapeCsv("False or duplicate detections"),
            EscapeCsv(response.M16Gate.Status),
            EscapeCsv($"<= {FalseOrDuplicateLimit}"),
            EscapeCsv(falseOrDuplicate.ToString(CultureInfo.InvariantCulture)),
            EscapeCsv(response.M16Gate.FalseOrDuplicatePass ? "YES" : "NO"),
        }));
        csv.AppendLine(string.Join(',', new[]
        {
            EscapeCsv("Material category failure"),
            EscapeCsv(response.M16Gate.Status),
            EscapeCsv("No"),
            EscapeCsv(response.M16Gate.MaterialCategoryFailure switch
            {
                true => "Yes",
                false => "No",
                null => "Pending",
            }),
            EscapeCsv(response.M16Gate.MaterialCategoryPass switch
            {
                true => "YES",
                false => "NO",
                null => "PENDING",
            }),
        }));

        byte[] bytes = Encoding.UTF8.GetBytes("\uFEFF" + csv);
        return Results.File(
            bytes,
            "text/csv; charset=utf-8",
            $"detector-comparison-{comparison.Id:D}.csv");
    }

}
