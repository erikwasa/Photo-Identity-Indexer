using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Query and reviewed-application boundary for detector rollout orchestration.
/// Human-resolved ambiguous candidates are applied from their durable candidate payload;
/// candidate order is never used as face identity.
/// </summary>
public sealed class SqliteDetectorRolloutApplicationRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly SqliteDetectorRolloutReviewRepository _reviewRepository;
    private readonly SqliteFaceCatalogueRepository _faceRepository;

    public SqliteDetectorRolloutApplicationRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _reviewRepository = new SqliteDetectorRolloutReviewRepository(database);
        _faceRepository = new SqliteFaceCatalogueRepository(database);
    }

    public async Task<IReadOnlyList<ExistingFaceDetectionAnchor>> GetExistingAnchorsAsync(
        AssetRevisionId assetRevisionId,
        Sha256Digest currentPipelineHash,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CatalogueFaceOccurrence> occurrences = await _faceRepository.GetOccurrencesAsync(
            assetRevisionId,
            cancellationToken);
        if (occurrences.Count == 0)
        {
            return [];
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        List<ExistingFaceDetectionAnchor> anchors = new(occurrences.Count);
        foreach (CatalogueFaceOccurrence occurrence in occurrences)
        {
            CatalogueDetectorRolloutOccurrenceAnchor? anchor = await ReadOccurrenceAnchorAsync(
                connection,
                occurrence.Id,
                currentPipelineHash,
                cancellationToken);
            if (anchor is null)
            {
                throw new DataException(
                    $"Face occurrence {occurrence.Id} has no detector geometry and cannot be safely reconciled.");
            }

            anchors.Add(new ExistingFaceDetectionAnchor(
                anchor.FaceOccurrenceId,
                anchor.BoundingBox,
                anchor.Landmarks));
        }

        return anchors;
    }

    public async Task<CatalogueDetectorRolloutOccurrenceAnchor?> GetOccurrenceAnchorAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadOccurrenceAnchorAsync(
            connection,
            faceOccurrenceId,
            currentPipelineHash: null,
            cancellationToken);
    }

    public async Task<Sha256Digest> GetPipelineHashAsync(
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        (Sha256Digest Hash, string ModelId, string ModelHash) pipeline = await ReadRunPipelineAsync(
            connection,
            processingRunId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Processing run {processingRunId} does not have detector-rollout pipeline provenance.");
        return pipeline.Hash;
    }

    public async Task<CatalogueDetectorRolloutSummary> GetSummaryAsync(
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*)
                   FROM detector_reconciliation_plans
                  WHERE processing_run_id = $run_id),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_candidates
                  WHERE processing_run_id = $run_id),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_candidates
                  WHERE processing_run_id = $run_id
                    AND applied_face_occurrence_id IS NOT NULL),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_candidates
                  WHERE processing_run_id = $run_id
                    AND disposition = 'ambiguous'),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_candidates AS candidate
                  WHERE candidate.processing_run_id = $run_id
                    AND candidate.disposition = 'ambiguous'
                    AND candidate.applied_face_occurrence_id IS NULL
                    AND NOT EXISTS (
                        SELECT 1
                        FROM detector_reconciliation_resolution_actions AS action
                        WHERE action.processing_run_id = candidate.processing_run_id
                          AND action.asset_revision_id = candidate.asset_revision_id
                          AND action.candidate_index = candidate.candidate_index)),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_candidates AS candidate
                  WHERE candidate.processing_run_id = $run_id
                    AND candidate.disposition = 'ambiguous'
                    AND candidate.applied_face_occurrence_id IS NULL
                    AND COALESCE((
                        SELECT action.action_kind
                        FROM detector_reconciliation_resolution_actions AS action
                        WHERE action.processing_run_id = candidate.processing_run_id
                          AND action.asset_revision_id = candidate.asset_revision_id
                          AND action.candidate_index = candidate.candidate_index
                        ORDER BY action.id DESC
                        LIMIT 1), '') IN ('existing', 'new')),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_candidates AS candidate
                  WHERE candidate.processing_run_id = $run_id
                    AND candidate.disposition = 'ambiguous'
                    AND candidate.applied_face_occurrence_id IS NULL
                    AND COALESCE((
                        SELECT action.action_kind
                        FROM detector_reconciliation_resolution_actions AS action
                        WHERE action.processing_run_id = candidate.processing_run_id
                          AND action.asset_revision_id = candidate.asset_revision_id
                          AND action.candidate_index = candidate.candidate_index
                        ORDER BY action.id DESC
                        LIMIT 1), '') = 'defer'),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_unmatched_existing
                  WHERE processing_run_id = $run_id);
            """;
        command.Parameters.AddWithValue("$run_id", processingRunId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new CatalogueDetectorRolloutSummary(
            processingRunId,
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7));
    }

    public async Task<IReadOnlyList<CatalogueDetectorRolloutPendingReview>> GetPendingReviewsAsync(
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        List<(AssetRevisionId RevisionId, int CandidateIndex)> keys = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT asset_revision_id, candidate_index
                FROM detector_reconciliation_candidates
                WHERE processing_run_id = $run_id
                  AND disposition = 'ambiguous'
                  AND applied_face_occurrence_id IS NULL
                ORDER BY asset_revision_id, candidate_index;
                """;
            command.Parameters.AddWithValue("$run_id", processingRunId.ToString());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add((AssetRevisionId.From(Guid.Parse(reader.GetString(0))), reader.GetInt32(1)));
            }
        }

        List<CatalogueDetectorRolloutPendingReview> values = new(keys.Count);
        foreach ((AssetRevisionId revisionId, int candidateIndex) in keys)
        {
            CatalogueDetectorReconciliationReview? review = await _reviewRepository.GetReviewAsync(
                processingRunId,
                revisionId,
                candidateIndex,
                cancellationToken);
            if (review is not null)
            {
                values.Add(new CatalogueDetectorRolloutPendingReview(processingRunId, revisionId, review));
            }
        }

        return values;
    }

    public async Task<CatalogueDetectorRolloutApplyResult> ApplyResolvedAsync(
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CatalogueDetectorRolloutPendingReview> pending =
            await GetPendingReviewsAsync(processingRunId, cancellationToken);
        int applied = 0;
        int deferred = 0;
        int awaiting = 0;

        foreach (CatalogueDetectorRolloutPendingReview value in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CatalogueDetectorReconciliationResolution? resolution = value.Review.LatestResolution;
            if (resolution is null)
            {
                awaiting++;
                continue;
            }

            if (resolution.Kind == DetectorReconciliationResolutionKind.Deferred)
            {
                deferred++;
                continue;
            }

            _ = await ApplyReviewedCandidateAsync(
                processingRunId,
                value.AssetRevisionId,
                value.Review.Candidate.CandidateIndex,
                cancellationToken);
            applied++;
        }

        return new CatalogueDetectorRolloutApplyResult(
            processingRunId,
            pending.Count,
            applied,
            deferred,
            awaiting);
    }

    public async Task<FaceOccurrenceId> ApplyReviewedCandidateAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidateIndex);
        CatalogueDetectorReconciliationReview review = await _reviewRepository.GetReviewAsync(
            processingRunId,
            assetRevisionId,
            candidateIndex,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Reconciliation candidate {candidateIndex} for revision {assetRevisionId} was not found.");

        if (review.Candidate.AppliedFaceOccurrenceId is FaceOccurrenceId alreadyApplied)
        {
            return alreadyApplied;
        }

        if (review.Candidate.Disposition != FaceDetectionReconciliationDisposition.Ambiguous)
        {
            throw new InvalidOperationException("Only an ambiguous candidate is applied through human resolution.");
        }

        CatalogueDetectorCandidateInspection inspection = review.Inspection
            ?? throw new InvalidOperationException("The reviewed candidate does not have a durable inspection payload.");
        CatalogueDetectorReconciliationResolution resolution = review.LatestResolution
            ?? throw new InvalidOperationException("The ambiguous candidate has not been resolved by a human.");
        if (resolution.Kind == DetectorReconciliationResolutionKind.Deferred)
        {
            throw new InvalidOperationException("A deferred reconciliation remains unresolved and cannot be applied.");
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        (Sha256Digest Hash, string ModelId, string ModelHash) pipeline = await ReadRunPipelineAsync(
            connection,
            processingRunId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Processing run {processingRunId} does not have registered detector-pipeline provenance.");
        if (!string.Equals(pipeline.ModelId, inspection.DetectorModelId.ToString(), StringComparison.Ordinal) ||
            !string.Equals(pipeline.ModelHash, inspection.DetectorModelHash.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The persisted candidate detector does not match the registered rollout pipeline.");
        }

        CatalogueFaceOccurrence occurrence;
        if (resolution.Kind == DetectorReconciliationResolutionKind.ExistingOccurrence)
        {
            FaceOccurrenceId target = resolution.FaceOccurrenceId
                ?? throw new DataException("The existing-occurrence resolution does not contain a face occurrence.");
            if (!review.Candidate.PossibleFaceOccurrenceIds.Contains(target))
            {
                throw new InvalidOperationException("The resolved existing face is not a persisted ambiguity option.");
            }

            occurrence = await ReadOccurrenceAsync(connection, target, cancellationToken)
                ?? throw new DataException("The resolved existing face occurrence no longer exists.");
            if (occurrence.AssetRevisionId != assetRevisionId)
            {
                throw new InvalidOperationException("The resolved existing face belongs to a different asset revision.");
            }
        }
        else if (resolution.Kind == DetectorReconciliationResolutionKind.NewOccurrence)
        {
            occurrence = await FindRecoveredNewOccurrenceAsync(
                connection,
                assetRevisionId,
                inspection,
                cancellationToken)
                ?? await AllocateNewOccurrenceAsync(
                    connection,
                    assetRevisionId,
                    inspection.ObservedAtUtc,
                    cancellationToken);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(resolution.Kind));
        }

        await GuardPipelineObservationAsync(
            connection,
            occurrence.Id,
            inspection,
            pipeline.Hash,
            cancellationToken);

        CatalogueFaceInspection persisted = await _faceRepository.SaveInspectionAsync(
            occurrence,
            new CatalogueFaceObservation(
                occurrence.Id,
                inspection.DetectorModelId,
                inspection.DetectorModelHash,
                inspection.Confidence,
                inspection.BoundingBox,
                inspection.Landmarks,
                inspection.ObservedAtUtc),
            new CatalogueFaceCrop(
                inspection.CropId,
                occurrence.Id,
                inspection.CropProtocol,
                inspection.CropContentHash,
                inspection.CropStoragePath,
                inspection.CropWidth,
                inspection.CropHeight,
                inspection.ObservedAtUtc),
            new CatalogueFaceEmbedding(
                inspection.CropId,
                inspection.EmbedderModelId,
                inspection.EmbedderModelHash,
                inspection.Embedding,
                inspection.ObservedAtUtc),
            cancellationToken);

        if (persisted.Occurrence.Id != occurrence.Id)
        {
            throw new InvalidOperationException(
                "The reviewed rollout application resolved to a different occurrence than the explicit human decision.");
        }

        await MarkAppliedAsync(
            persisted.Occurrence.Id,
            pipeline.Hash,
            inspection,
            processingRunId,
            assetRevisionId,
            candidateIndex,
            cancellationToken);
        return persisted.Occurrence.Id;
    }

    private async Task MarkAppliedAsync(
        FaceOccurrenceId occurrenceId,
        Sha256Digest pipelineHash,
        CatalogueDetectorCandidateInspection inspection,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand observation = connection.CreateCommand())
        {
            observation.Transaction = transaction;
            observation.CommandText = """
                UPDATE face_observations
                SET detector_pipeline_hash = $pipeline_hash
                WHERE face_occurrence_id = $face_occurrence_id
                  AND detector_model_id = $model_id
                  AND detector_model_hash = $model_hash
                  AND (detector_pipeline_hash IS NULL OR detector_pipeline_hash = $pipeline_hash);
                """;
            observation.Parameters.AddWithValue("$pipeline_hash", pipelineHash.ToString());
            observation.Parameters.AddWithValue("$face_occurrence_id", occurrenceId.ToString());
            observation.Parameters.AddWithValue("$model_id", inspection.DetectorModelId.ToString());
            observation.Parameters.AddWithValue("$model_hash", inspection.DetectorModelHash.ToString());
            int changed = await observation.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 1)
            {
                throw new InvalidOperationException("The reviewed detector observation could not be bound to its rollout pipeline.");
            }
        }

        using (SqliteCommand candidate = connection.CreateCommand())
        {
            candidate.Transaction = transaction;
            candidate.CommandText = """
                UPDATE detector_reconciliation_candidates
                SET applied_face_occurrence_id = $face_occurrence_id,
                    applied_at_utc = COALESCE(applied_at_utc, $applied_at_utc)
                WHERE processing_run_id = $processing_run_id
                  AND asset_revision_id = $asset_revision_id
                  AND candidate_index = $candidate_index
                  AND disposition = 'ambiguous'
                  AND (applied_face_occurrence_id IS NULL OR applied_face_occurrence_id = $face_occurrence_id);
                """;
            candidate.Parameters.AddWithValue("$face_occurrence_id", occurrenceId.ToString());
            candidate.Parameters.AddWithValue("$applied_at_utc", Format(inspection.ObservedAtUtc));
            candidate.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            candidate.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
            candidate.Parameters.AddWithValue("$candidate_index", candidateIndex);
            int changed = await candidate.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 1)
            {
                throw new InvalidOperationException("The reviewed reconciliation candidate was already applied differently.");
            }
        }

        transaction.Commit();
    }

    private static async Task GuardPipelineObservationAsync(
        SqliteConnection connection,
        FaceOccurrenceId occurrenceId,
        CatalogueDetectorCandidateInspection inspection,
        Sha256Digest pipelineHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT detector_pipeline_hash
            FROM face_observations
            WHERE face_occurrence_id = $face_occurrence_id
              AND detector_model_id = $model_id
              AND detector_model_hash = $model_hash;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", occurrenceId.ToString());
        command.Parameters.AddWithValue("$model_id", inspection.DetectorModelId.ToString());
        command.Parameters.AddWithValue("$model_hash", inspection.DetectorModelHash.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is string existingPipeline &&
            !string.Equals(existingPipeline, pipelineHash.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The target occurrence already has the same detector model bytes recorded under a different pipeline identity.");
        }
    }

    private static async Task<CatalogueFaceOccurrence?> FindRecoveredNewOccurrenceAsync(
        SqliteConnection connection,
        AssetRevisionId assetRevisionId,
        CatalogueDetectorCandidateInspection inspection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT occurrence.id, occurrence.asset_revision_id, occurrence.ordinal, occurrence.created_at_utc
            FROM face_occurrences AS occurrence
            INNER JOIN face_crops AS crop
                ON crop.face_occurrence_id = occurrence.id
            INNER JOIN face_observations AS observation
                ON observation.face_occurrence_id = occurrence.id
            WHERE occurrence.asset_revision_id = $asset_revision_id
              AND crop.crop_protocol = $crop_protocol
              AND crop.content_sha256 = $crop_hash
              AND crop.storage_path = $storage_path
              AND observation.detector_model_id = $model_id
              AND observation.detector_model_hash = $model_hash
            ORDER BY occurrence.ordinal, occurrence.id;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        command.Parameters.AddWithValue("$crop_protocol", inspection.CropProtocol.ToString());
        command.Parameters.AddWithValue("$crop_hash", inspection.CropContentHash.ToString());
        command.Parameters.AddWithValue("$storage_path", inspection.CropStoragePath);
        command.Parameters.AddWithValue("$model_id", inspection.DetectorModelId.ToString());
        command.Parameters.AddWithValue("$model_hash", inspection.DetectorModelHash.ToString());
        List<CatalogueFaceOccurrence> matches = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(ReadOccurrence(reader));
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new DataException("Multiple face occurrences contain the same persisted rollout candidate payload."),
        };
    }

    private static async Task<CatalogueFaceOccurrence> AllocateNewOccurrenceAsync(
        SqliteConnection connection,
        AssetRevisionId assetRevisionId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(MAX(ordinal), -1) + 1
            FROM face_occurrences
            WHERE asset_revision_id = $asset_revision_id;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        int ordinal = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return new CatalogueFaceOccurrence(
            FaceOccurrenceId.New(),
            assetRevisionId,
            ordinal,
            createdAtUtc);
    }

    private static async Task<CatalogueFaceOccurrence?> ReadOccurrenceAsync(
        SqliteConnection connection,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, asset_revision_id, ordinal, created_at_utc
            FROM face_occurrences
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", faceOccurrenceId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOccurrence(reader) : null;
    }

    private static async Task<CatalogueDetectorRolloutOccurrenceAnchor?> ReadOccurrenceAnchorAsync(
        SqliteConnection connection,
        FaceOccurrenceId faceOccurrenceId,
        Sha256Digest? currentPipelineHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT occurrence.id, occurrence.ordinal,
                   observation.bounding_box_json, observation.landmarks_json
            FROM face_occurrences AS occurrence
            INNER JOIN face_observations AS observation
                ON observation.face_occurrence_id = occurrence.id
            WHERE occurrence.id = $face_occurrence_id
            ORDER BY
                CASE
                    WHEN observation.detector_pipeline_hash IS NULL THEN 0
                    WHEN $pipeline_hash IS NULL OR observation.detector_pipeline_hash <> $pipeline_hash THEN 1
                    ELSE 2
                END,
                observation.observed_at_utc DESC,
                observation.detector_model_id,
                observation.detector_model_hash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$pipeline_hash", currentPipelineHash?.ToString() ?? (object)DBNull.Value);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CatalogueDetectorRolloutOccurrenceAnchor(
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
            reader.GetInt32(1),
            DeserializeBoundingBox(reader.GetString(2)),
            DeserializeLandmarks(reader.GetString(3)));
    }

    private static async Task<(Sha256Digest Hash, string ModelId, string ModelHash)?> ReadRunPipelineAsync(
        SqliteConnection connection,
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT pipeline.pipeline_hash, pipeline.detector_model_id, pipeline.detector_model_hash
            FROM processing_run_detector_pipelines AS registration
            INNER JOIN detector_pipelines AS pipeline
                ON pipeline.pipeline_hash = registration.pipeline_hash
            WHERE registration.processing_run_id = $run_id;
            """;
        command.Parameters.AddWithValue("$run_id", processingRunId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (new Sha256Digest(reader.GetString(0)), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static CatalogueFaceOccurrence ReadOccurrence(SqliteDataReader reader) =>
        new(
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
            AssetRevisionId.From(Guid.Parse(reader.GetString(1))),
            reader.GetInt32(2),
            ParseTimestamp(reader.GetString(3)));

    private static NormalizedBoundingBox DeserializeBoundingBox(string value)
    {
        double[] coordinates = JsonSerializer.Deserialize<double[]>(value)
            ?? throw new DataException("Bounding-box JSON was null.");
        if (coordinates.Length != 4)
        {
            throw new DataException("Bounding-box JSON must contain four coordinates.");
        }

        return new NormalizedBoundingBox(coordinates[0], coordinates[1], coordinates[2], coordinates[3]);
    }

    private static NormalizedFaceLandmarks DeserializeLandmarks(string value)
    {
        double[][] points = JsonSerializer.Deserialize<double[][]>(value)
            ?? throw new DataException("Landmark JSON was null.");
        if (points.Length != 5 || points.Any(point => point.Length != 2))
        {
            throw new DataException("Landmark JSON must contain five two-dimensional points.");
        }

        return new NormalizedFaceLandmarks(
            new NormalizedPoint(points[0][0], points[0][1]),
            new NormalizedPoint(points[1][0], points[1][1]),
            new NormalizedPoint(points[2][0], points[2][1]),
            new NormalizedPoint(points[3][0], points[3][1]),
            new NormalizedPoint(points[4][0], points[4][1]));
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
