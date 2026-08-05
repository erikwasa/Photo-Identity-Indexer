using System.Globalization;
using System.Text;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;


public static partial class DetectorEvaluationComparisonEndpoints
{
    private static DetectorEvaluationGroundTruthSummaryResponse ToGroundTruthSummary(
        StoredDetectorGroundTruth snapshot) => new(
            snapshot.BaselineSessionId.ToString("D"),
            snapshot.Name,
            snapshot.FrozenAtUtc,
            snapshot.Photos.Count,
            snapshot.Photos.Sum(photo => photo.Faces.Count));

    private static DetectorEvaluationComparisonSummaryResponse ToSummaryResponse(
        StoredDetectorEvaluationComparison comparison)
    {
        DetectorEvaluationM16GateResponse gate = CalculateGate(comparison);
        int exceptionPhotoCount = comparison.Photos.Count(photo => photo.ExceptionComponents.Count > 0);
        return new DetectorEvaluationComparisonSummaryResponse(
            comparison.Id.ToString("D"),
            comparison.Name,
            comparison.BaselineSessionId.ToString("D"),
            comparison.BaselineName,
            comparison.CandidateProcessingRunId,
            comparison.CreatedAtUtc,
            comparison.UpdatedAtUtc,
            comparison.Photos.Count,
            exceptionPhotoCount,
            comparison.Photos.Count(photo => photo.ExceptionComponents.Count > 0 && IsResolved(photo)),
            gate.Status);
    }

    private static DetectorEvaluationComparisonResponse ToResponse(
        StoredDetectorEvaluationComparison comparison)
    {
        DetectorEvaluationComparisonMetricsResponse overall = CalculateMetrics(comparison.Photos);
        DetectorEvaluationComparisonMetricsResponse fivePlus = CalculateMetrics(
            comparison.Photos.Where(photo => photo.CountableFaces >= 5));
        return new DetectorEvaluationComparisonResponse(
            comparison.Id.ToString("D"),
            comparison.Name,
            comparison.BaselineSessionId.ToString("D"),
            comparison.BaselineName,
            comparison.GroundTruthFrozenAtUtc,
            comparison.CandidateProcessingRunId,
            comparison.IouThreshold,
            comparison.CreatedAtUtc,
            comparison.UpdatedAtUtc,
            overall,
            fivePlus,
            GroupMetrics(comparison.Photos, photo => photo.SourceGroup),
            GroupMetrics(comparison.Photos, photo => photo.PrimaryCategory),
            CalculateGate(comparison),
            comparison.Photos
                .Where(photo => photo.ExceptionComponents.Count > 0)
                .Select(ToPhotoResponse)
                .ToArray());
    }

    private static DetectorEvaluationComparisonPhotoResponse ToPhotoResponse(
        StoredDetectorEvaluationComparisonPhoto photo)
    {
        Dictionary<string, StoredDetectorGroundTruthFace> groundTruth = photo.GroundTruthFaces.ToDictionary(
            face => face.Id,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, StoredDetectorEvaluationCandidateDetection> candidates = photo.CandidateDetections.ToDictionary(
            detection => detection.Id,
            StringComparer.OrdinalIgnoreCase);

        return new DetectorEvaluationComparisonPhotoResponse(
            photo.CandidateRevisionId,
            photo.PhotoName,
            photo.RevisionSha256[..12],
            $"/api/detector-evaluation/photos/{photo.CandidateRevisionId}/content",
            photo.SampleId,
            photo.SampleGroup,
            photo.SourceGroup,
            photo.PrimaryCategory,
            photo.CountableFaces,
            photo.AutomaticMatches.Count,
            photo.ExceptionComponents.Select(component => new DetectorEvaluationComparisonExceptionComponentResponse(
                component.Id,
                component.Kind,
                component.GroundTruthFaceIds.Select(id => ToGroundTruthFaceResponse(groundTruth[id])).ToArray(),
                component.CandidateDetectionIds.Select(id => ToCandidateResponse(candidates[id])).ToArray())).ToArray(),
            new DetectorEvaluationComparisonCorrectionResponse(
                photo.Correction.Matches.Select(match => new DetectorEvaluationComparisonManualMatchResponse(
                    match.GroundTruthFaceId,
                    match.CandidateDetectionId)).ToArray(),
                photo.Correction.FalseCandidateDetectionIds,
                photo.Correction.DuplicateCandidateDetectionIds,
                photo.Correction.MissedGroundTruthFaceIds,
                photo.Correction.Notes),
            IsResolved(photo));
    }

    private static DetectorEvaluationComparisonGroundTruthFaceResponse ToGroundTruthFaceResponse(
        StoredDetectorGroundTruthFace face) => new(
            face.Id,
            new DetectorEvaluationBoundingBoxResponse(face.X, face.Y, face.Width, face.Height),
            face.IsBackgroundUnknown,
            face.Origin);

    private static DetectorEvaluationComparisonCandidateDetectionResponse ToCandidateResponse(
        StoredDetectorEvaluationCandidateDetection detection) => new(
            detection.Id,
            detection.FaceNumber,
            detection.Confidence,
            new DetectorEvaluationBoundingBoxResponse(
                detection.X,
                detection.Y,
                detection.Width,
                detection.Height));

    private static IReadOnlyList<DetectorEvaluationComparisonGroupSummaryResponse> GroupMetrics(
        IEnumerable<StoredDetectorEvaluationComparisonPhoto> photos,
        Func<StoredDetectorEvaluationComparisonPhoto, string> keySelector) =>
        photos.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DetectorEvaluationComparisonGroupSummaryResponse(
                group.Key,
                CalculateMetrics(group)))
            .ToArray();

    private static DetectorEvaluationComparisonMetricsResponse CalculateMetrics(
        IEnumerable<StoredDetectorEvaluationComparisonPhoto> photos)
    {
        StoredDetectorEvaluationComparisonPhoto[] values = photos.ToArray();
        int matched = values.Sum(photo => photo.AutomaticMatches.Count + photo.Correction.Matches.Count);
        int countableFaces = values.Sum(photo => photo.CountableFaces);
        int unresolvedGroundTruth = values.Sum(photo =>
        {
            int exceptionCount = photo.ExceptionComponents.Sum(component => component.GroundTruthFaceIds.Count);
            int resolved = photo.Correction.Matches.Count + photo.Correction.MissedGroundTruthFaceIds.Count;
            return Math.Max(0, exceptionCount - resolved);
        });
        int unresolvedCandidates = values.Sum(photo =>
        {
            int exceptionCount = photo.ExceptionComponents.Sum(component => component.CandidateDetectionIds.Count);
            int resolved = photo.Correction.Matches.Count +
                           photo.Correction.FalseCandidateDetectionIds.Count +
                           photo.Correction.DuplicateCandidateDetectionIds.Count;
            return Math.Max(0, exceptionCount - resolved);
        });
        return new DetectorEvaluationComparisonMetricsResponse(
            values.Length,
            countableFaces,
            matched,
            values.Sum(photo => photo.Correction.MissedGroundTruthFaceIds.Count),
            unresolvedGroundTruth,
            countableFaces == 0 ? 1 : (double)matched / countableFaces,
            values.Sum(photo => photo.Correction.FalseCandidateDetectionIds.Count),
            values.Sum(photo => photo.Correction.DuplicateCandidateDetectionIds.Count),
            unresolvedCandidates);
    }

}
