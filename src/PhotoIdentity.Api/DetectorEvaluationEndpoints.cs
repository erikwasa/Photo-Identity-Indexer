using System.Globalization;
using System.Text;
using PhotoIdentity.Core.Geometry;
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
        group.MapGet("/sessions", GetSessionsAsync);
        group.MapPost("/sessions", CreateSessionAsync);
        group.MapGet("/sessions/{sessionId}", GetSessionAsync);
        group.MapPut("/sessions/{sessionId}/photos/{revisionId}", SavePhotoReviewAsync);
        group.MapGet("/sessions/{sessionId}/export.csv", ExportSessionCsvAsync);
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
        if (!TryParseIdentifier(runId, out Guid parsedRunId))
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
        if (!TryParseIdentifier(revisionId, out Guid parsedRevisionId))
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

    private static async Task<IResult> GetSessionsAsync(
        DetectorEvaluationSessionStore store,
        string? runId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(runId) && !TryParseIdentifier(runId, out _))
        {
            return Results.BadRequest(new { error = "The processing run identifier is invalid." });
        }

        IReadOnlyList<StoredDetectorEvaluationSession> sessions = await store.ListAsync(runId, cancellationToken);
        return Results.Ok(sessions.Select(ToSummaryResponse).ToArray());
    }

    private static async Task<IResult> CreateSessionAsync(
        CreateDetectorEvaluationSessionRequest request,
        SqliteDetectorEvaluationRepository repository,
        DetectorEvaluationSessionStore store,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "A session name is required." });
        }

        if (!TryParseIdentifier(request.ProcessingRunId, out Guid parsedRunId))
        {
            return Results.BadRequest(new { error = "The processing run identifier is invalid." });
        }

        if (request.Photos is null || request.Photos.Count == 0)
        {
            return Results.BadRequest(new { error = "The manifest must contain at least one photo." });
        }

        try
        {
            IReadOnlyList<CatalogueDetectorEvaluationPhoto> runPhotos = await LoadRunPhotosAsync(
                repository,
                ProcessingRunId.From(parsedRunId),
                cancellationToken);
            if (runPhotos.Count == 0)
            {
                return Results.BadRequest(new { error = "The selected processing run contains no photos." });
            }

            Dictionary<string, CatalogueDetectorEvaluationPhoto> runByName = new(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogueDetectorEvaluationPhoto photo in runPhotos)
            {
                if (!runByName.TryAdd(photo.PhotoName, photo))
                {
                    return Results.BadRequest(new
                    {
                        error = $"The processing run contains duplicate staged filename '{photo.PhotoName}'. Use unique staged filenames before importing a manifest.",
                    });
                }
            }

            Dictionary<string, DetectorEvaluationManifestEntryRequest> manifestByName = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> sampleIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (DetectorEvaluationManifestEntryRequest entry in request.Photos)
            {
                string? validationError = ValidateManifestEntry(entry);
                if (validationError is not null)
                {
                    return Results.BadRequest(new { error = validationError });
                }

                if (!manifestByName.TryAdd(entry.ImageName.Trim(), entry))
                {
                    return Results.BadRequest(new { error = $"Manifest image '{entry.ImageName}' appears more than once." });
                }

                if (!sampleIds.Add(entry.SampleId.Trim()))
                {
                    return Results.BadRequest(new { error = $"Manifest sample ID '{entry.SampleId}' appears more than once." });
                }
            }

            string[] missingManifestRows = runByName.Keys
                .Where(name => !manifestByName.ContainsKey(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] unknownManifestRows = manifestByName.Keys
                .Where(name => !runByName.ContainsKey(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingManifestRows.Length > 0 || unknownManifestRows.Length > 0)
            {
                return Results.BadRequest(new
                {
                    error = "The manifest image names do not exactly match the selected processing run.",
                    missingFromManifest = missingManifestRows,
                    absentFromRun = unknownManifestRows,
                });
            }

            List<DetectorEvaluationPhotoSeed> seeds = [];
            foreach (CatalogueDetectorEvaluationPhoto photo in runPhotos)
            {
                DetectorEvaluationManifestEntryRequest entry = manifestByName[photo.PhotoName];
                string revisionSha256 = photo.RevisionHash.ToString();
                if (!string.IsNullOrWhiteSpace(entry.SourceSha256) &&
                    !string.Equals(entry.SourceSha256.Trim(), revisionSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new
                    {
                        error = $"Manifest SHA-256 does not match the processed revision for '{entry.ImageName}'.",
                    });
                }

                seeds.Add(new DetectorEvaluationPhotoSeed(
                    photo.RevisionId.ToString(),
                    revisionSha256,
                    photo.PhotoName,
                    entry.SampleId.Trim(),
                    entry.SampleGroup.Trim(),
                    entry.SourceGroup.Trim(),
                    entry.PrimaryCategory.Trim(),
                    entry.CountableFaces,
                    photo.Detections.Select(detection => detection.Id.ToString()).ToArray()));
            }

            StoredDetectorEvaluationSession session = await store.CreateAsync(
                new DetectorEvaluationSessionSeed(request.Name, parsedRunId.ToString("D"), seeds),
                cancellationToken);
            return Results.Created(
                $"/api/detector-evaluation/sessions/{session.Id:D}",
                ToSummaryResponse(session));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static async Task<IResult> GetSessionAsync(
        string sessionId,
        SqliteDetectorEvaluationRepository repository,
        DetectorEvaluationSessionStore store,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(sessionId, out Guid parsedSessionId))
        {
            return Results.BadRequest(new { error = "The detector-evaluation session identifier is invalid." });
        }

        StoredDetectorEvaluationSession? session = await store.GetAsync(parsedSessionId, cancellationToken);
        if (session is null)
        {
            return Results.NotFound();
        }

        return await BuildSessionResultAsync(session, repository, cancellationToken);
    }

    private static async Task<IResult> SavePhotoReviewAsync(
        string sessionId,
        string revisionId,
        SaveDetectorEvaluationPhotoReviewRequest request,
        SqliteDetectorEvaluationRepository repository,
        DetectorEvaluationSessionStore store,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(sessionId, out Guid parsedSessionId))
        {
            return Results.BadRequest(new { error = "The detector-evaluation session identifier is invalid." });
        }

        if (!TryParseIdentifier(revisionId, out Guid parsedRevisionId))
        {
            return Results.BadRequest(new { error = "The asset revision identifier is invalid." });
        }

        if (request.DetectionJudgements is null || request.MissedFaces is null)
        {
            return Results.BadRequest(new { error = "Detection judgements and missed faces are required." });
        }

        try
        {
            DetectorEvaluationPhotoReviewUpdate update = new(
                request.DetectionJudgements.Select(value => new DetectorEvaluationDetectionJudgementUpdate(
                    value.DetectionId,
                    string.IsNullOrWhiteSpace(value.Disposition) ? null : value.Disposition.Trim())).ToArray(),
                request.MissedFaces.Select(value => new DetectorEvaluationMissedFaceUpdate(
                    value.Id,
                    new NormalizedBoundingBox(
                        value.BoundingBox.X,
                        value.BoundingBox.Y,
                        value.BoundingBox.Width,
                        value.BoundingBox.Height))).ToArray(),
                request.MissReason,
                request.Notes);

            StoredDetectorEvaluationSession? session = await store.SavePhotoAsync(
                parsedSessionId,
                parsedRevisionId.ToString("D"),
                update,
                cancellationToken);
            return session is null
                ? Results.NotFound()
                : await BuildSessionResultAsync(session, repository, cancellationToken);
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

    private static async Task<IResult> ExportSessionCsvAsync(
        string sessionId,
        DetectorEvaluationSessionStore store,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(sessionId, out Guid parsedSessionId))
        {
            return Results.BadRequest(new { error = "The detector-evaluation session identifier is invalid." });
        }

        StoredDetectorEvaluationSession? session = await store.GetAsync(parsedSessionId, cancellationToken);
        if (session is null)
        {
            return Results.NotFound();
        }

        StringBuilder csv = new();
        csv.AppendLine(
            "Sample ID,Image Name,Sample Group,Primary Category,Countable Faces,Correct Detections,Missed Faces,False Detections,Duplicate Detections,Likely Background / Unknown Detections (optional),Miss Reason,Notes,Row Check,Source Group");
        foreach (StoredDetectorEvaluationPhoto photo in session.Photos)
        {
            DetectorEvaluationPhotoMetrics metrics = CalculateMetrics(photo);
            string[] values =
            [
                photo.SampleId,
                photo.PhotoName,
                photo.SampleGroup,
                photo.PrimaryCategory,
                photo.CountableFaces.ToString(CultureInfo.InvariantCulture),
                metrics.CorrectDetections.ToString(CultureInfo.InvariantCulture),
                photo.MissedFaces.Count.ToString(CultureInfo.InvariantCulture),
                metrics.FalseDetections.ToString(CultureInfo.InvariantCulture),
                metrics.DuplicateDetections.ToString(CultureInfo.InvariantCulture),
                metrics.BackgroundUnknownDetections.ToString(CultureInfo.InvariantCulture),
                photo.MissReason ?? string.Empty,
                photo.Notes ?? string.Empty,
                metrics.IsComplete ? "OK" : "INCOMPLETE",
                photo.SourceGroup,
            ];
            csv.AppendLine(string.Join(',', values.Select(EscapeCsv)));
        }

        byte[] bytes = Encoding.UTF8.GetBytes("\uFEFF" + csv);
        return Results.File(
            bytes,
            "text/csv; charset=utf-8",
            $"detector-evaluation-{session.Id:D}.csv");
    }

    private static async Task<IResult> BuildSessionResultAsync(
        StoredDetectorEvaluationSession session,
        SqliteDetectorEvaluationRepository repository,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(session.ProcessingRunId, out Guid parsedRunId))
        {
            return Results.Conflict(new { error = "The stored session processing run identifier is invalid." });
        }

        IReadOnlyList<CatalogueDetectorEvaluationPhoto> runPhotos = await LoadRunPhotosAsync(
            repository,
            ProcessingRunId.From(parsedRunId),
            cancellationToken);
        Dictionary<string, CatalogueDetectorEvaluationPhoto> runByRevision = runPhotos.ToDictionary(
            photo => photo.RevisionId.ToString(),
            StringComparer.OrdinalIgnoreCase);

        List<DetectorEvaluationSessionPhotoResponse> responses = [];
        foreach (StoredDetectorEvaluationPhoto storedPhoto in session.Photos)
        {
            if (!runByRevision.TryGetValue(storedPhoto.RevisionId, out CatalogueDetectorEvaluationPhoto? photo))
            {
                return Results.Conflict(new
                {
                    error = $"Processed revision '{storedPhoto.RevisionId}' is no longer available in the selected run.",
                });
            }

            if (!string.Equals(storedPhoto.RevisionSha256, photo.RevisionHash.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(new
                {
                    error = $"Processed revision hash changed for '{storedPhoto.PhotoName}'.",
                });
            }

            Dictionary<string, StoredDetectorEvaluationDetection> storedDetections = storedPhoto.Detections
                .ToDictionary(detection => detection.Id, StringComparer.OrdinalIgnoreCase);
            if (storedDetections.Count != photo.Detections.Count ||
                photo.Detections.Any(detection => !storedDetections.ContainsKey(detection.Id.ToString())))
            {
                return Results.Conflict(new
                {
                    error = $"Persisted detections changed for '{storedPhoto.PhotoName}'. Start a new evaluation session for the changed run.",
                });
            }

            DetectorEvaluationPhotoMetrics metrics = CalculateMetrics(storedPhoto);
            responses.Add(new DetectorEvaluationSessionPhotoResponse(
                storedPhoto.RevisionId,
                storedPhoto.PhotoName,
                photo.MediaType,
                photo.Width,
                photo.Height,
                storedPhoto.RevisionSha256[..12],
                $"/api/detector-evaluation/photos/{storedPhoto.RevisionId}/content",
                storedPhoto.SampleId,
                storedPhoto.SampleGroup,
                storedPhoto.SourceGroup,
                storedPhoto.PrimaryCategory,
                storedPhoto.CountableFaces,
                photo.Detections.Select(detection =>
                {
                    StoredDetectorEvaluationDetection stored = storedDetections[detection.Id.ToString()];
                    return new DetectorEvaluationSessionDetectionResponse(
                        detection.Id.ToString(),
                        detection.Ordinal + 1,
                        detection.Confidence,
                        new DetectorEvaluationBoundingBoxResponse(
                            detection.BoundingBox.X,
                            detection.BoundingBox.Y,
                            detection.BoundingBox.Width,
                            detection.BoundingBox.Height),
                        stored.Disposition);
                }).ToArray(),
                storedPhoto.MissedFaces.Select(missed => new DetectorEvaluationMissedFaceResponse(
                    missed.Id,
                    new DetectorEvaluationBoundingBoxResponse(
                        missed.X,
                        missed.Y,
                        missed.Width,
                        missed.Height))).ToArray(),
                storedPhoto.MissReason,
                storedPhoto.Notes,
                metrics.CorrectDetections,
                metrics.BackgroundUnknownDetections,
                metrics.FalseDetections,
                metrics.DuplicateDetections,
                metrics.IsComplete));
        }

        return Results.Ok(new DetectorEvaluationSessionResponse(
            session.Id.ToString("D"),
            session.Name,
            session.ProcessingRunId,
            session.CreatedAtUtc,
            session.UpdatedAtUtc,
            responses.Count(photo => photo.IsComplete),
            responses));
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
                "Detector-evaluation sessions currently support at most 1000 photos per processing run.");
        }

        return page.Items;
    }

    private static string? ValidateManifestEntry(DetectorEvaluationManifestEntryRequest entry)
    {
        if (string.IsNullOrWhiteSpace(entry.SampleId))
        {
            return "Every manifest row must have a Sample ID.";
        }

        if (string.IsNullOrWhiteSpace(entry.ImageName))
        {
            return $"Manifest sample '{entry.SampleId}' is missing Image Name.";
        }

        if (string.IsNullOrWhiteSpace(entry.SourceGroup))
        {
            return $"Manifest sample '{entry.SampleId}' is missing Source Group.";
        }

        if (string.IsNullOrWhiteSpace(entry.PrimaryCategory))
        {
            return $"Manifest sample '{entry.SampleId}' is missing Primary Category.";
        }

        if (entry.CountableFaces < 0)
        {
            return $"Manifest sample '{entry.SampleId}' has a negative Countable Faces value.";
        }

        if (!string.IsNullOrWhiteSpace(entry.SourceSha256) &&
            (entry.SourceSha256.Trim().Length != 64 ||
             entry.SourceSha256.Trim().Any(value => !Uri.IsHexDigit(value))))
        {
            return $"Manifest sample '{entry.SampleId}' has an invalid Source SHA-256 value.";
        }

        return null;
    }

    private static DetectorEvaluationSessionSummaryResponse ToSummaryResponse(
        StoredDetectorEvaluationSession session) => new(
            session.Id.ToString("D"),
            session.Name,
            session.ProcessingRunId,
            session.CreatedAtUtc,
            session.UpdatedAtUtc,
            session.Photos.Count,
            session.Photos.Count(photo => CalculateMetrics(photo).IsComplete));

    private static DetectorEvaluationPhotoMetrics CalculateMetrics(StoredDetectorEvaluationPhoto photo)
    {
        int correct = photo.Detections.Count(detection =>
            DetectorEvaluationDispositions.CountsAsCorrect(detection.Disposition));
        int background = photo.Detections.Count(detection =>
            detection.Disposition == DetectorEvaluationDispositions.BackgroundUnknown);
        int falseDetections = photo.Detections.Count(detection =>
            detection.Disposition == DetectorEvaluationDispositions.FalseDetection);
        int duplicates = photo.Detections.Count(detection =>
            detection.Disposition == DetectorEvaluationDispositions.Duplicate);
        bool everyDetectionClassified = photo.Detections.All(detection =>
            DetectorEvaluationDispositions.IsValid(detection.Disposition));
        bool arithmeticMatches = photo.CountableFaces == correct + photo.MissedFaces.Count;
        return new DetectorEvaluationPhotoMetrics(
            correct,
            background,
            falseDetections,
            duplicates,
            everyDetectionClassified && arithmeticMatches);
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

    private static bool TryParseIdentifier(string value, out Guid parsed) =>
        Guid.TryParse(value, out parsed) && parsed != Guid.Empty;

    private static string EscapeCsv(string value)
    {
        if (!value.ContainsAny([',', '"', '\r', '\n']))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed record DetectorEvaluationPhotoMetrics(
        int CorrectDetections,
        int BackgroundUnknownDetections,
        int FalseDetections,
        int DuplicateDetections,
        bool IsComplete);
}
