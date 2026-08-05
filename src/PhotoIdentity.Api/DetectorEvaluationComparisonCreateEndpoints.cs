using System.Globalization;
using System.Text;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;


public static partial class DetectorEvaluationComparisonEndpoints
{
    private static async Task<IResult> GetComparisonsAsync(
        DetectorEvaluationComparisonStore store,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredDetectorEvaluationComparison> comparisons = await store.ListAsync(cancellationToken);
        return Results.Ok(comparisons.Select(ToSummaryResponse).ToArray());
    }

    private static async Task<IResult> CreateComparisonAsync(
        CreateDetectorEvaluationComparisonRequest request,
        DetectorEvaluationGroundTruthStore groundTruthStore,
        DetectorEvaluationComparisonStore comparisonStore,
        SqliteDetectorEvaluationRepository repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "A comparison name is required." });
        }

        if (!TryParseIdentifier(request.BaselineSessionId, out Guid baselineSessionId))
        {
            return Results.BadRequest(new { error = "The baseline session identifier is invalid." });
        }

        if (!TryParseIdentifier(request.CandidateProcessingRunId, out Guid candidateRunId))
        {
            return Results.BadRequest(new { error = "The candidate processing run identifier is invalid." });
        }

        double iouThreshold = request.IouThreshold ?? DefaultIouThreshold;
        if (iouThreshold is <= 0 or > 1)
        {
            return Results.BadRequest(new { error = "The IoU threshold must be greater than zero and at most one." });
        }

        StoredDetectorGroundTruth? groundTruth = await groundTruthStore.GetAsync(baselineSessionId, cancellationToken);
        if (groundTruth is null)
        {
            return Results.Conflict(new
            {
                error = "The selected baseline session has not been frozen into reusable face-level ground truth.",
            });
        }

        try
        {
            IReadOnlyList<CatalogueDetectorEvaluationPhoto> candidatePhotos = await LoadRunPhotosAsync(
                repository,
                ProcessingRunId.From(candidateRunId),
                cancellationToken);
            if (candidatePhotos.Count == 0)
            {
                return Results.BadRequest(new { error = "The selected candidate processing run contains no photos." });
            }

            Dictionary<string, CatalogueDetectorEvaluationPhoto> candidateByName = new(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogueDetectorEvaluationPhoto photo in candidatePhotos)
            {
                if (!candidateByName.TryAdd(photo.PhotoName, photo))
                {
                    return Results.BadRequest(new
                    {
                        error = $"The candidate run contains duplicate staged filename '{photo.PhotoName}'.",
                    });
                }
            }

            string[] missingCandidatePhotos = groundTruth.Photos
                .Where(photo => !candidateByName.ContainsKey(photo.PhotoName))
                .Select(photo => photo.PhotoName)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] unexpectedCandidatePhotos = candidateByName.Keys
                .Where(name => groundTruth.Photos.All(photo =>
                    !string.Equals(photo.PhotoName, name, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingCandidatePhotos.Length > 0 || unexpectedCandidatePhotos.Length > 0)
            {
                return Results.BadRequest(new
                {
                    error = "The candidate run does not contain exactly the frozen evaluation set.",
                    missingFromCandidate = missingCandidatePhotos,
                    absentFromGroundTruth = unexpectedCandidatePhotos,
                });
            }

            List<StoredDetectorEvaluationComparisonPhoto> comparisonPhotos = [];
            foreach (StoredDetectorGroundTruthPhoto groundTruthPhoto in groundTruth.Photos)
            {
                CatalogueDetectorEvaluationPhoto candidatePhoto = candidateByName[groundTruthPhoto.PhotoName];
                if (!string.Equals(
                        groundTruthPhoto.RevisionSha256,
                        candidatePhoto.RevisionHash.ToString(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new
                    {
                        error = $"Candidate source revision does not match the frozen SHA-256 for '{groundTruthPhoto.PhotoName}'.",
                    });
                }

                StoredDetectorEvaluationCandidateDetection[] candidates = candidatePhoto.Detections
                    .Select(detection => new StoredDetectorEvaluationCandidateDetection
                    {
                        Id = detection.Id.ToString(),
                        FaceNumber = detection.Ordinal + 1,
                        Confidence = detection.Confidence,
                        X = detection.BoundingBox.X,
                        Y = detection.BoundingBox.Y,
                        Width = detection.BoundingBox.Width,
                        Height = detection.BoundingBox.Height,
                    })
                    .ToArray();
                comparisonPhotos.Add(DetectorEvaluationMatching.BuildPhoto(
                    groundTruthPhoto,
                    candidatePhoto.RevisionId.ToString(),
                    candidates,
                    iouThreshold));
            }

            StoredDetectorEvaluationComparison comparison = await comparisonStore.CreateAsync(
                new DetectorEvaluationComparisonSeed(
                    request.Name,
                    baselineSessionId,
                    groundTruth.Name,
                    groundTruth.FrozenAtUtc,
                    candidateRunId.ToString("D"),
                    iouThreshold,
                    comparisonPhotos),
                cancellationToken);
            return Results.Created(
                $"/api/detector-evaluation/comparisons/{comparison.Id:D}",
                ToResponse(comparison));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }

}
