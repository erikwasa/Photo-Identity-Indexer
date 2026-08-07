using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class DetectorRolloutEndpoints
{
    public static IEndpointRouteBuilder MapDetectorRolloutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/detector-rollout");
        group.MapGet("/runs/{runId}", GetRunAsync);
        group.MapGet("/runs/{runId}/pending", GetPendingAsync);
        group.MapGet("/revisions/{revisionId}/image", GetRevisionImageAsync);
        group.MapGet(
            "/runs/{runId}/revisions/{revisionId}/candidates/{candidateIndex:int}/image",
            GetCandidateImageAsync);
        group.MapPost(
            "/runs/{runId}/revisions/{revisionId}/candidates/{candidateIndex:int}/resolve",
            ResolveAsync);
        return endpoints;
    }

    private static async Task<IResult> GetRunAsync(
        string runId,
        SqliteProcessingRepository processingRepository,
        SqliteDetectorRolloutApplicationRepository rolloutRepository,
        CancellationToken cancellationToken)
    {
        if (!TryRunId(runId, out ProcessingRunId parsedRunId))
        {
            return BadRequest("The rollout run identifier is invalid.");
        }

        try
        {
            ProcessingRunSummary processing = await processingRepository.GetRunSummaryAsync(parsedRunId, cancellationToken);
            CatalogueDetectorRolloutSummary rollout = await rolloutRepository.GetSummaryAsync(parsedRunId, cancellationToken);
            bool complete = rollout.CandidateCount == rollout.AppliedCount &&
                            rollout.AwaitingReviewCount == 0 &&
                            rollout.ReadyToApplyCount == 0 &&
                            rollout.DeferredCount == 0 &&
                            processing.FailedJobs == 0;
            return Results.Ok(new DetectorRolloutRunResponse(
                parsedRunId.ToString(),
                processing.Status.ToString().ToLowerInvariant(),
                processing.TotalJobs,
                processing.SucceededJobs,
                processing.FailedJobs,
                rollout.CandidateCount,
                rollout.AppliedCount,
                rollout.AmbiguousCount,
                rollout.AwaitingReviewCount,
                rollout.ReadyToApplyCount,
                rollout.DeferredCount,
                rollout.UnmatchedExistingCount,
                complete));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> GetPendingAsync(
        string runId,
        SqliteDetectorRolloutApplicationRepository rolloutRepository,
        SqliteReviewRepository reviewRepository,
        CancellationToken cancellationToken)
    {
        if (!TryRunId(runId, out ProcessingRunId parsedRunId))
        {
            return BadRequest("The rollout run identifier is invalid.");
        }

        IReadOnlyList<CatalogueDetectorRolloutPendingReview> pending =
            await rolloutRepository.GetPendingReviewsAsync(parsedRunId, cancellationToken);
        List<DetectorRolloutPendingReviewResponse> responses = new(pending.Count);
        foreach (CatalogueDetectorRolloutPendingReview value in pending)
        {
            List<DetectorRolloutOptionResponse> options = [];
            foreach (FaceOccurrenceId optionId in value.Review.Candidate.PossibleFaceOccurrenceIds)
            {
                CatalogueDetectorRolloutOccurrenceAnchor? anchor =
                    await rolloutRepository.GetOccurrenceAnchorAsync(optionId, cancellationToken);
                CatalogueReviewFace? face = await reviewRepository.GetFaceAsync(optionId, cancellationToken);
                if (anchor is null || face is null)
                {
                    continue;
                }

                options.Add(new DetectorRolloutOptionResponse(
                    optionId.ToString(),
                    anchor.Ordinal,
                    ToBox(anchor.BoundingBox),
                    $"/api/review/faces/{optionId}/image",
                    face.State,
                    face.Person?.DisplayName));
            }

            responses.Add(new DetectorRolloutPendingReviewResponse(
                parsedRunId.ToString(),
                value.AssetRevisionId.ToString(),
                value.Review.Candidate.CandidateIndex,
                ToBox(value.Review.Candidate.BoundingBox),
                $"/api/detector-rollout/revisions/{value.AssetRevisionId}/image",
                $"/api/detector-rollout/runs/{parsedRunId}/revisions/{value.AssetRevisionId}/candidates/{value.Review.Candidate.CandidateIndex}/image",
                options,
                value.Review.LatestResolution is null ? null : ToResolution(value.Review.LatestResolution)));
        }

        return Results.Ok(responses);
    }

    private static async Task<IResult> GetRevisionImageAsync(
        string revisionId,
        CollectionPhotoFileResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!TryRevisionId(revisionId, out AssetRevisionId parsedRevisionId))
        {
            return BadRequest("The asset revision identifier is invalid.");
        }

        CollectionPhotoFile? file = await resolver.ResolveAsync(parsedRevisionId, cancellationToken);
        return file is null
            ? Results.NotFound()
            : Results.File(file.Path, file.ContentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> GetCandidateImageAsync(
        string runId,
        string revisionId,
        int candidateIndex,
        SqliteDetectorRolloutReviewRepository reviewRepository,
        DetectorRolloutCropFileResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!TryRunId(runId, out ProcessingRunId parsedRunId) ||
            !TryRevisionId(revisionId, out AssetRevisionId parsedRevisionId) ||
            candidateIndex < 0)
        {
            return BadRequest("The rollout candidate key is invalid.");
        }

        CatalogueDetectorCandidateInspection? inspection = await reviewRepository.GetInspectionAsync(
            parsedRunId,
            parsedRevisionId,
            candidateIndex,
            cancellationToken);
        if (inspection is null)
        {
            return Results.NotFound();
        }

        string? path = await resolver.ResolveAsync(parsedRunId, inspection.CropStoragePath, cancellationToken);
        return path is null ? Results.NotFound() : Results.File(path, "image/png", enableRangeProcessing: true);
    }

    private static async Task<IResult> ResolveAsync(
        string runId,
        string revisionId,
        int candidateIndex,
        SaveDetectorRolloutResolutionRequest request,
        SqliteDetectorRolloutReviewRepository reviewRepository,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryRunId(runId, out ProcessingRunId parsedRunId) ||
            !TryRevisionId(revisionId, out AssetRevisionId parsedRevisionId) ||
            candidateIndex < 0)
        {
            return BadRequest("The rollout candidate key is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.Action))
        {
            return BadRequest("A resolution action is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Actor))
        {
            return BadRequest("A review actor is required.");
        }

        DetectorReconciliationResolutionKind kind;
        FaceOccurrenceId? faceOccurrenceId = null;
        switch (request.Action.Trim().ToLowerInvariant())
        {
            case "existing":
                kind = DetectorReconciliationResolutionKind.ExistingOccurrence;
                if (!TryFaceOccurrenceId(request.FaceOccurrenceId, out FaceOccurrenceId parsedFaceId))
                {
                    return BadRequest("An existing-face resolution requires a valid face occurrence identifier.");
                }
                faceOccurrenceId = parsedFaceId;
                break;
            case "new":
                kind = DetectorReconciliationResolutionKind.NewOccurrence;
                break;
            case "defer":
                kind = DetectorReconciliationResolutionKind.Deferred;
                break;
            default:
                return BadRequest("Resolution action must be 'existing', 'new' or 'defer'.");
        }

        try
        {
            CatalogueDetectorReconciliationResolution resolution = await reviewRepository.RecordResolutionAsync(
                parsedRunId,
                parsedRevisionId,
                candidateIndex,
                kind,
                faceOccurrenceId,
                request.Actor,
                timeProvider.GetUtcNow(),
                request.Note,
                cancellationToken);
            return Results.Ok(ToResolution(resolution));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }

    private static DetectorRolloutBoxResponse ToBox(NormalizedBoundingBox box) =>
        new(box.X, box.Y, box.Width, box.Height);

    private static DetectorRolloutResolutionResponse ToResolution(
        CatalogueDetectorReconciliationResolution resolution) =>
        new(
            resolution.Kind switch
            {
                DetectorReconciliationResolutionKind.ExistingOccurrence => "existing",
                DetectorReconciliationResolutionKind.NewOccurrence => "new",
                DetectorReconciliationResolutionKind.Deferred => "defer",
                _ => throw new ArgumentOutOfRangeException(nameof(resolution.Kind)),
            },
            resolution.FaceOccurrenceId?.ToString(),
            resolution.Actor,
            resolution.Note,
            resolution.CreatedAtUtc);

    private static bool TryRunId(string value, out ProcessingRunId id)
    {
        id = default;
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }
        id = ProcessingRunId.From(parsed);
        return true;
    }

    private static bool TryRevisionId(string value, out AssetRevisionId id)
    {
        id = default;
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }
        id = AssetRevisionId.From(parsed);
        return true;
    }

    private static bool TryFaceOccurrenceId(string? value, out FaceOccurrenceId id)
    {
        id = default;
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
        {
            return false;
        }
        id = FaceOccurrenceId.From(parsed);
        return true;
    }

    private static IResult BadRequest(string message) => Results.BadRequest(new { error = message });
}
