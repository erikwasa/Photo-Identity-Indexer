using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;

namespace PhotoIdentity.Api;

public static class DetectorEvaluationEndpoints
{
    public static IEndpointRouteBuilder MapDetectorEvaluationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/detector-evaluation");
        group.MapGet("/runs", GetRunsAsync);
        group.MapGet("/photos", GetPhotosAsync);
        group.MapGet("/photos/{revisionId}/content", GetPhotoContentAsync);
        return endpoints;
    }

    private static async Task<IResult> GetRunsAsync(
        SqliteDetectorEvaluationRepository repository,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogueDetectorEvaluationRun> runs = await repository.GetRunsAsync(cancellationToken);
        return Results.Ok(runs.Select(run => new DetectorEvaluationRunResponse(
            run.Id.ToString(),
            run.Status,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.PhotoCount,
            run.DetectionCount)).ToArray());
    }

    private static async Task<IResult> GetPhotosAsync(
        string runId,
        SqliteDetectorEvaluationRepository repository,
        int offset = 0,
        int limit = 8,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(runId, out Guid parsedRunId) || parsedRunId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "The processing run identifier is invalid." });
        }

        try
        {
            CatalogueDetectorEvaluationPhotoPage page = await repository.GetPhotosAsync(
                ProcessingRunId.From(parsedRunId),
                offset,
                limit,
                cancellationToken);
            return Results.Ok(new DetectorEvaluationPhotoPageResponse(
                page.Items.Select(ToResponse).ToArray(),
                page.Offset,
                page.Limit,
                page.Total));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> GetPhotoContentAsync(
        string revisionId,
        CollectionPhotoFileResolver resolver,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(revisionId, out Guid parsedRevisionId) || parsedRevisionId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        CollectionPhotoFile? file = await resolver.ResolveAsync(
            AssetRevisionId.From(parsedRevisionId),
            cancellationToken);
        return file is null
            ? Results.NotFound()
            : Results.File(file.Path, file.ContentType, enableRangeProcessing: true);
    }

    private static DetectorEvaluationPhotoResponse ToResponse(CatalogueDetectorEvaluationPhoto photo) => new(
        photo.RevisionId.ToString(),
        photo.PhotoName,
        photo.MediaType,
        photo.Width,
        photo.Height,
        photo.RevisionHash.ToString()[..12],
        photo.JobStatus,
        $"/api/detector-evaluation/photos/{photo.RevisionId}/content",
        photo.Detections.Select(detection => new DetectorEvaluationDetectionResponse(
            detection.Id.ToString(),
            detection.Ordinal + 1,
            detection.Confidence,
            new DetectorEvaluationBoundingBoxResponse(
                detection.BoundingBox.X,
                detection.BoundingBox.Y,
                detection.BoundingBox.Width,
                detection.BoundingBox.Height))).ToArray());
}
