using System.Globalization;
using System.Text;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static partial class DetectorEvaluationComparisonEndpoints
{
    private const double DefaultIouThreshold = 0.5;
    private const double OverallRecallTarget = 0.90;
    private const double FivePlusRecallTarget = 0.85;
    private const int FalseOrDuplicateLimit = 10;

    public static IEndpointRouteBuilder MapDetectorEvaluationComparisonEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/detector-evaluation");
        group.MapGet("/ground-truth", GetGroundTruthAsync);
        group.MapPost("/sessions/{sessionId}/ground-truth", FreezeGroundTruthAsync);
        group.MapGet("/comparisons", GetComparisonsAsync);
        group.MapPost("/comparisons", CreateComparisonAsync);
        group.MapGet("/comparisons/{comparisonId}", GetComparisonAsync);
        group.MapPut("/comparisons/{comparisonId}/photos/{revisionId}", SavePhotoCorrectionAsync);
        group.MapPut("/comparisons/{comparisonId}/m16-gate", SaveGateAssessmentAsync);
        group.MapGet("/comparisons/{comparisonId}/export.csv", ExportComparisonCsvAsync);
        return endpoints;
    }

    private static async Task<IResult> GetGroundTruthAsync(
        DetectorEvaluationGroundTruthStore store,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredDetectorGroundTruth> snapshots = await store.ListAsync(cancellationToken);
        return Results.Ok(snapshots.Select(ToGroundTruthSummary).ToArray());
    }

    private static async Task<IResult> FreezeGroundTruthAsync(
        string sessionId,
        DetectorEvaluationSessionStore sessionStore,
        DetectorEvaluationGroundTruthStore groundTruthStore,
        SqliteDetectorEvaluationRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(sessionId, out Guid parsedSessionId))
        {
            return Results.BadRequest(new { error = "The detector-evaluation session identifier is invalid." });
        }

        StoredDetectorGroundTruth? existing = await groundTruthStore.GetAsync(parsedSessionId, cancellationToken);
        if (existing is not null)
        {
            return Results.Ok(ToGroundTruthSummary(existing));
        }

        StoredDetectorEvaluationSession? session = await sessionStore.GetAsync(parsedSessionId, cancellationToken);
        if (session is null)
        {
            return Results.NotFound();
        }

        if (!TryParseIdentifier(session.ProcessingRunId, out Guid parsedRunId))
        {
            return Results.Conflict(new { error = "The baseline session processing run identifier is invalid." });
        }

        try
        {
            IReadOnlyList<CatalogueDetectorEvaluationPhoto> runPhotos = await LoadRunPhotosAsync(
                repository,
                ProcessingRunId.From(parsedRunId),
                cancellationToken);
            Dictionary<string, CatalogueDetectorEvaluationPhoto> runByRevision = runPhotos.ToDictionary(
                photo => photo.RevisionId.ToString(),
                StringComparer.OrdinalIgnoreCase);
            List<StoredDetectorGroundTruthPhoto> frozenPhotos = [];

            foreach (StoredDetectorEvaluationPhoto storedPhoto in session.Photos)
            {
                BaselinePhotoMetrics metrics = CalculateBaselineMetrics(storedPhoto);
                if (!metrics.IsComplete)
                {
                    return Results.Conflict(new
                    {
                        error = $"Baseline photo '{storedPhoto.PhotoName}' is incomplete. Complete every classification and count before freezing ground truth.",
                    });
                }

                if (!runByRevision.TryGetValue(storedPhoto.RevisionId, out CatalogueDetectorEvaluationPhoto? runPhoto))
                {
                    return Results.Conflict(new
                    {
                        error = $"Baseline revision '{storedPhoto.RevisionId}' is not available in the current catalogue. Freeze ground truth before switching to an isolated candidate catalogue.",
                    });
                }

                if (!string.Equals(storedPhoto.RevisionSha256, runPhoto.RevisionHash.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Conflict(new { error = $"Baseline revision hash changed for '{storedPhoto.PhotoName}'." });
                }

                Dictionary<string, CatalogueDetectorEvaluationDetection> detections = runPhoto.Detections.ToDictionary(
                    detection => detection.Id.ToString(),
                    StringComparer.OrdinalIgnoreCase);
                if (detections.Count != storedPhoto.Detections.Count ||
                    storedPhoto.Detections.Any(detection => !detections.ContainsKey(detection.Id)))
                {
                    return Results.Conflict(new
                    {
                        error = $"Persisted baseline detections changed for '{storedPhoto.PhotoName}'.",
                    });
                }

                List<StoredDetectorGroundTruthFace> faces = [];
                foreach (StoredDetectorEvaluationDetection judgement in storedPhoto.Detections
                             .OrderBy(value => value.Id, StringComparer.Ordinal))
                {
                    if (!DetectorEvaluationDispositions.CountsAsCorrect(judgement.Disposition))
                    {
                        continue;
                    }

                    CatalogueDetectorEvaluationDetection detection = detections[judgement.Id];
                    faces.Add(new StoredDetectorGroundTruthFace
                    {
                        Id = judgement.Id,
                        X = detection.BoundingBox.X,
                        Y = detection.BoundingBox.Y,
                        Width = detection.BoundingBox.Width,
                        Height = detection.BoundingBox.Height,
                        IsBackgroundUnknown = judgement.Disposition == DetectorEvaluationDispositions.BackgroundUnknown,
                        Origin = "baseline-detection",
                    });
                }

                faces.AddRange(storedPhoto.MissedFaces
                    .OrderBy(value => value.Id, StringComparer.Ordinal)
                    .Select(missed => new StoredDetectorGroundTruthFace
                    {
                        Id = missed.Id,
                        X = missed.X,
                        Y = missed.Y,
                        Width = missed.Width,
                        Height = missed.Height,
                        IsBackgroundUnknown = false,
                        Origin = "manual-miss",
                    }));

                if (faces.Count != storedPhoto.CountableFaces)
                {
                    return Results.Conflict(new
                    {
                        error = $"Ground truth for '{storedPhoto.PhotoName}' contains {faces.Count} faces but the manifest declares {storedPhoto.CountableFaces}.",
                    });
                }

                frozenPhotos.Add(new StoredDetectorGroundTruthPhoto
                {
                    BaselineRevisionId = storedPhoto.RevisionId,
                    RevisionSha256 = storedPhoto.RevisionSha256,
                    PhotoName = storedPhoto.PhotoName,
                    SampleId = storedPhoto.SampleId,
                    SampleGroup = storedPhoto.SampleGroup,
                    SourceGroup = storedPhoto.SourceGroup,
                    PrimaryCategory = storedPhoto.PrimaryCategory,
                    CountableFaces = storedPhoto.CountableFaces,
                    Faces = faces,
                });
            }

            StoredDetectorGroundTruth snapshot = await groundTruthStore.CreateAsync(
                parsedSessionId,
                session.Name,
                frozenPhotos,
                cancellationToken);
            return Results.Created(
                $"/api/detector-evaluation/ground-truth/{snapshot.BaselineSessionId:D}",
                ToGroundTruthSummary(snapshot));
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
