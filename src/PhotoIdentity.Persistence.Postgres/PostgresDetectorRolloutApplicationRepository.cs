using System.Data;
using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

public sealed partial class PostgresDetectorRolloutApplicationRepository : IDetectorRolloutApplicationRepository
{
    private readonly PostgresCatalogueDatabase _database;
    private readonly PostgresDetectorRolloutReviewRepository _reviewRepository;

    public PostgresDetectorRolloutApplicationRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _reviewRepository = new(database);
    }

    public async Task<IReadOnlyList<ExistingFaceDetectionAnchor>> GetExistingAnchorsAsync(
        AssetRevisionId assetRevisionId,
        Sha256Digest currentPipelineHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM face_occurrences WHERE asset_revision_id = @revision ORDER BY ordinal, id;";
        command.Parameters.AddWithValue("revision", assetRevisionId.Value);
        List<FaceOccurrenceId> ids = [];
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(FaceOccurrenceId.From(reader.GetGuid(0)));
            }
        }

        List<ExistingFaceDetectionAnchor> anchors = [];
        foreach (FaceOccurrenceId id in ids)
        {
            CatalogueDetectorRolloutOccurrenceAnchor anchor = await ReadOccurrenceAnchorAsync(
                connection, id, currentPipelineHash, cancellationToken)
                ?? throw new DataException($"Face occurrence {id} has no detector geometry and cannot be safely reconciled.");
            anchors.Add(new(id, anchor.BoundingBox, anchor.Landmarks));
        }

        return anchors;
    }
    public async Task<CatalogueDetectorRolloutOccurrenceAnchor?> GetOccurrenceAnchorAsync(
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
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
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
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
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*)
                   FROM detector_reconciliation_plans
                  WHERE processing_run_id = @run_id),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_candidates
                  WHERE processing_run_id = @run_id),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_candidates
                  WHERE processing_run_id = @run_id
                    AND applied_face_occurrence_id IS NOT NULL),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_candidates
                  WHERE processing_run_id = @run_id
                    AND disposition = 'ambiguous'),
                (SELECT COUNT(*)
                   FROM detector_reconciliation_candidates AS candidate
                  WHERE candidate.processing_run_id = @run_id
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
                  WHERE candidate.processing_run_id = @run_id
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
                  WHERE candidate.processing_run_id = @run_id
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
                  WHERE processing_run_id = @run_id);
            """;
        command.Parameters.AddWithValue("@run_id", processingRunId.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new CatalogueDetectorRolloutSummary(
            processingRunId,
            checked((int)reader.GetInt64(0)),
            checked((int)reader.GetInt64(1)),
            checked((int)reader.GetInt64(2)),
            checked((int)reader.GetInt64(3)),
            checked((int)reader.GetInt64(4)),
            checked((int)reader.GetInt64(5)),
            checked((int)reader.GetInt64(6)),
            checked((int)reader.GetInt64(7)));
    }

    public async Task<IReadOnlyList<CatalogueDetectorRolloutPendingReview>> GetPendingReviewsAsync(
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        List<(AssetRevisionId RevisionId, int CandidateIndex)> keys = [];
        using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT asset_revision_id, candidate_index
                FROM detector_reconciliation_candidates
                WHERE processing_run_id = @run_id
                  AND disposition = 'ambiguous'
                  AND applied_face_occurrence_id IS NULL
                ORDER BY asset_revision_id, candidate_index;
                """;
            command.Parameters.AddWithValue("@run_id", processingRunId.Value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add((AssetRevisionId.From(reader.GetGuid(0)), reader.GetInt32(1)));
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

    private static async Task<CatalogueDetectorRolloutOccurrenceAnchor?> ReadOccurrenceAnchorAsync(
        NpgsqlConnection connection,
        FaceOccurrenceId faceOccurrenceId,
        Sha256Digest? currentPipelineHash,
        CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT occurrence.id, occurrence.ordinal,
                   observation.bounding_box_json, observation.landmarks_json
            FROM face_occurrences AS occurrence
            INNER JOIN face_observations AS observation
                ON observation.face_occurrence_id = occurrence.id
            WHERE occurrence.id = @face_occurrence_id
            ORDER BY
                CASE
                    WHEN observation.detector_pipeline_hash IS NULL THEN 0
                    WHEN @pipeline_hash IS NULL OR observation.detector_pipeline_hash <> @pipeline_hash THEN 1
                    ELSE 2
                END,
                observation.observed_at_utc DESC,
                observation.detector_model_id,
                observation.detector_model_hash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@face_occurrence_id", faceOccurrenceId.Value);
        command.Parameters.AddWithValue("@pipeline_hash", NpgsqlDbType.Text, currentPipelineHash?.ToString() ?? (object)DBNull.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CatalogueDetectorRolloutOccurrenceAnchor(
            FaceOccurrenceId.From(reader.GetGuid(0)),
            reader.GetInt32(1),
            DeserializeBoundingBox(reader.GetString(2)),
            DeserializeLandmarks(reader.GetString(3)));
    }

    private static async Task<(Sha256Digest Hash, string ModelId, string ModelHash)?> ReadRunPipelineAsync(
        NpgsqlConnection connection,
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT pipeline.pipeline_hash, pipeline.detector_model_id, pipeline.detector_model_hash
            FROM processing_run_detector_pipelines AS registration
            INNER JOIN detector_pipelines AS pipeline
                ON pipeline.pipeline_hash = registration.pipeline_hash
            WHERE registration.processing_run_id = @run_id;
            """;
        command.Parameters.AddWithValue("@run_id", processingRunId.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (new Sha256Digest(reader.GetString(0)), reader.GetString(1), reader.GetString(2))
            : null;
    }

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

}
