using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Persistence boundary for detector replacement. Unlike the ordinary inspection writer,
/// this repository never uses a candidate ordinal as evidence that two detections are the same face.
/// </summary>
public sealed class PostgresDetectorReconciliationPlanRepository : IDetectorReconciliationPlanRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresDetectorReconciliationPlanRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueDetectorPipelineRegistration> RegisterPipelineAsync(
        ProcessingRunId processingRunId,
        DetectorPipelineDefinition definition,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Sha256Digest pipelineHash = definition.ComputeHash();
        string canonicalDefinition = definition.ToCanonicalText();
        DateTimeOffset recordedAt = recordedAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO detector_pipelines (
                    pipeline_hash,
                    detector_model_id,
                    detector_model_hash,
                    canonical_definition,
                    recorded_at_utc)
                VALUES (
                    @pipeline_hash,
                    @detector_model_id,
                    @detector_model_hash,
                    @canonical_definition,
                    @recorded_at_utc)
                ON CONFLICT(pipeline_hash) DO NOTHING;
                """;
            command.Parameters.AddWithValue("@pipeline_hash", pipelineHash.ToString());
            command.Parameters.AddWithValue("@detector_model_id", definition.DetectorModelId.ToString());
            command.Parameters.AddWithValue("@detector_model_hash", definition.DetectorModelHash.ToString());
            command.Parameters.AddWithValue("@canonical_definition", canonicalDefinition);
            command.Parameters.AddWithValue("@recorded_at_utc", recordedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        (string ModelId, string ModelHash, string CanonicalDefinition, DateTimeOffset RecordedAt) persisted =
            await ReadPipelineAsync(connection, transaction, pipelineHash, cancellationToken)
            ?? throw new InvalidOperationException("The detector pipeline was unavailable after registration.");
        if (!string.Equals(persisted.ModelId, definition.DetectorModelId.ToString(), StringComparison.Ordinal) ||
            !string.Equals(persisted.ModelHash, definition.DetectorModelHash.ToString(), StringComparison.Ordinal) ||
            !string.Equals(persisted.CanonicalDefinition, canonicalDefinition, StringComparison.Ordinal))
        {
            throw new DataException("The stored detector-pipeline hash resolves to different canonical provenance.");
        }

        using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO processing_run_detector_pipelines (
                    processing_run_id,
                    pipeline_hash,
                    recorded_at_utc)
                VALUES (@processing_run_id, @pipeline_hash, @recorded_at_utc)
                ON CONFLICT(processing_run_id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("@processing_run_id", processingRunId.Value);
            command.Parameters.AddWithValue("@pipeline_hash", pipelineHash.ToString());
            command.Parameters.AddWithValue("@recorded_at_utc", recordedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        Sha256Digest linkedHash = await ReadRunPipelineHashAsync(
            connection,
            transaction,
            processingRunId,
            cancellationToken)
            ?? throw new InvalidOperationException("The processing run was not linked to its detector pipeline.");
        if (linkedHash != pipelineHash)
        {
            throw new InvalidOperationException(
                $"Processing run {processingRunId} is already bound to detector pipeline {linkedHash}; " +
                $"it cannot be rebound to {pipelineHash}.");
        }

        await transaction.CommitAsync(cancellationToken);
        return new CatalogueDetectorPipelineRegistration(
            processingRunId,
            pipelineHash,
            canonicalDefinition,
            persisted.RecordedAt);
    }

    public async Task<CatalogueDetectorReconciliationPlan> SavePlanAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        Sha256Digest pipelineHash,
        IReadOnlyList<CandidateFaceDetectionAnchor> candidateFaces,
        FaceDetectionReconciliationPlan plan,
        DateTimeOffset plannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateFaces);
        ArgumentNullException.ThrowIfNull(plan);

        Dictionary<int, CandidateFaceDetectionAnchor> anchors = candidateFaces.ToDictionary(value => value.CandidateIndex);
        int[] decisionIndices = plan.CandidateDecisions.Select(value => value.CandidateIndex).Order().ToArray();
        int[] anchorIndices = anchors.Keys.Order().ToArray();
        if (!decisionIndices.SequenceEqual(anchorIndices))
        {
            throw new ArgumentException("Candidate anchors must exactly match the reconciliation-plan candidate indices.");
        }

        DateTimeOffset plannedAt = plannedAtUtc.ToUniversalTime();
        CatalogueDetectorReconciliationPlan expected = ToCataloguePlan(
            processingRunId, assetRevisionId, pipelineHash, plannedAt, anchors, plan);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        Sha256Digest linkedHash = await ReadRunPipelineHashAsync(
            connection,
            transaction,
            processingRunId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Processing run {processingRunId} does not have registered detector-pipeline provenance.");
        if (linkedHash != pipelineHash)
        {
            throw new InvalidOperationException(
                $"Reconciliation plan pipeline {pipelineHash} does not match run pipeline {linkedHash}.");
        }

        bool inserted;
        using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO detector_reconciliation_plans (
                    processing_run_id,
                    asset_revision_id,
                    pipeline_hash,
                    planned_at_utc)
                VALUES (@processing_run_id, @asset_revision_id, @pipeline_hash, @planned_at_utc)
                ON CONFLICT(processing_run_id, asset_revision_id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("@processing_run_id", processingRunId.Value);
            command.Parameters.AddWithValue("@asset_revision_id", assetRevisionId.Value);
            command.Parameters.AddWithValue("@pipeline_hash", pipelineHash.ToString());
            command.Parameters.AddWithValue("@planned_at_utc", plannedAt);
            inserted = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }

        if (!inserted)
        {
            CatalogueDetectorReconciliationPlan existing = await ReadPlanAsync(
                connection, transaction, processingRunId, assetRevisionId, cancellationToken)
                ?? throw new DataException("The persisted reconciliation plan disappeared during replay.");
            if (!PlansEquivalent(expected, existing))
            {
                throw new InvalidOperationException(
                    "A different reconciliation plan is already persisted for this processing run and asset revision.");
            }

            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        foreach (FaceDetectionReconciliationDecision decision in plan.CandidateDecisions)
        {
            CandidateFaceDetectionAnchor anchor = anchors[decision.CandidateIndex];
            using NpgsqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO detector_reconciliation_candidates (
                    processing_run_id,
                    asset_revision_id,
                    candidate_index,
                    disposition,
                    proposed_face_occurrence_id,
                    bounding_box_json,
                    landmarks_json)
                VALUES (
                    @processing_run_id,
                    @asset_revision_id,
                    @candidate_index,
                    @disposition,
                    @proposed_face_occurrence_id,
                    @bounding_box_json,
                    @landmarks_json)
                ON CONFLICT(processing_run_id, asset_revision_id, candidate_index) DO NOTHING;
                """;
            command.Parameters.AddWithValue("@processing_run_id", processingRunId.Value);
            command.Parameters.AddWithValue("@asset_revision_id", assetRevisionId.Value);
            command.Parameters.AddWithValue("@candidate_index", decision.CandidateIndex);
            command.Parameters.AddWithValue("@disposition", ToStorage(decision.Disposition));
            command.Parameters.AddWithValue(
                "@proposed_face_occurrence_id", NpgsqlDbType.Uuid,
                (object?)decision.ExistingFaceOccurrenceId?.Value ?? DBNull.Value);
            command.Parameters.AddWithValue("@bounding_box_json", NpgsqlDbType.Jsonb, SerializeBoundingBox(anchor.BoundingBox));
            command.Parameters.AddWithValue("@landmarks_json", NpgsqlDbType.Jsonb, SerializeLandmarks(anchor.Landmarks));
            await command.ExecuteNonQueryAsync(cancellationToken);

            foreach (FaceOccurrenceId possible in decision.PossibleExistingFaceOccurrenceIds.Distinct())
            {
                using NpgsqlCommand option = connection.CreateCommand();
                option.Transaction = transaction;
                option.CommandText = """
                    INSERT INTO detector_reconciliation_candidate_options (
                        processing_run_id,
                        asset_revision_id,
                        candidate_index,
                        face_occurrence_id)
                    VALUES (@processing_run_id, @asset_revision_id, @candidate_index, @face_occurrence_id) ON CONFLICT DO NOTHING;
                    """;
                option.Parameters.AddWithValue("@processing_run_id", processingRunId.Value);
                option.Parameters.AddWithValue("@asset_revision_id", assetRevisionId.Value);
                option.Parameters.AddWithValue("@candidate_index", decision.CandidateIndex);
                option.Parameters.AddWithValue("@face_occurrence_id", possible.Value);
                await option.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        foreach (FaceOccurrenceId unmatched in plan.ExistingOccurrencesWithoutCandidate.Distinct())
        {
            using NpgsqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO detector_reconciliation_unmatched_existing (
                    processing_run_id,
                    asset_revision_id,
                    face_occurrence_id)
                VALUES (@processing_run_id, @asset_revision_id, @face_occurrence_id) ON CONFLICT DO NOTHING;
                """;
            command.Parameters.AddWithValue("@processing_run_id", processingRunId.Value);
            command.Parameters.AddWithValue("@asset_revision_id", assetRevisionId.Value);
            command.Parameters.AddWithValue("@face_occurrence_id", unmatched.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueDetectorReconciliationPlan persisted = await ReadPlanAsync(
            connection,
            transaction,
            processingRunId,
            assetRevisionId,
            cancellationToken)
            ?? throw new InvalidOperationException("The reconciliation plan was unavailable after persistence.");

        if (!PlansEquivalent(expected, persisted))
        {
            throw new InvalidOperationException(
                "A different reconciliation plan is already persisted for this processing run and asset revision.");
        }

        await transaction.CommitAsync(cancellationToken);
        return persisted;
    }

    public async Task<CatalogueDetectorReconciliationPlan?> GetPlanAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        CatalogueDetectorReconciliationPlan? plan = await ReadPlanAsync(
            connection, transaction, processingRunId, assetRevisionId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return plan;
    }

    private static async Task<(string ModelId, string ModelHash, string CanonicalDefinition, DateTimeOffset RecordedAt)?> ReadPipelineAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Sha256Digest pipelineHash,
        CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT detector_model_id, detector_model_hash, canonical_definition, recorded_at_utc
            FROM detector_pipelines
            WHERE pipeline_hash = @pipeline_hash;
            """;
        command.Parameters.AddWithValue("@pipeline_hash", pipelineHash.ToString());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3))
            : null;
    }

    private static async Task<Sha256Digest?> ReadRunPipelineHashAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT pipeline_hash
            FROM processing_run_detector_pipelines
            WHERE processing_run_id = @processing_run_id;
            """;
        command.Parameters.AddWithValue("@processing_run_id", processingRunId.Value);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text ? new Sha256Digest(text) : null;
    }

    private static async Task<CatalogueDetectorReconciliationPlan?> ReadPlanAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        CancellationToken cancellationToken)
    {
        Sha256Digest pipelineHash;
        DateTimeOffset plannedAt;
        using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT pipeline_hash, planned_at_utc
                FROM detector_reconciliation_plans
                WHERE processing_run_id = @processing_run_id
                  AND asset_revision_id = @asset_revision_id;
                """;
            command.Parameters.AddWithValue("@processing_run_id", processingRunId.Value);
            command.Parameters.AddWithValue("@asset_revision_id", assetRevisionId.Value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            pipelineHash = new Sha256Digest(reader.GetString(0));
            plannedAt = reader.GetFieldValue<DateTimeOffset>(1);
        }

        List<CatalogueDetectorReconciliationCandidate> candidates = [];
        using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT candidate_index, disposition, proposed_face_occurrence_id,
                       bounding_box_json, landmarks_json, applied_face_occurrence_id, applied_at_utc,
                       ARRAY(SELECT face_occurrence_id FROM detector_reconciliation_candidate_options AS option
                             WHERE option.processing_run_id = candidate.processing_run_id
                               AND option.asset_revision_id = candidate.asset_revision_id
                               AND option.candidate_index = candidate.candidate_index
                             ORDER BY face_occurrence_id)
                FROM detector_reconciliation_candidates AS candidate
                WHERE processing_run_id = @processing_run_id
                  AND asset_revision_id = @asset_revision_id
                ORDER BY candidate_index;
                """;
            command.Parameters.AddWithValue("@processing_run_id", processingRunId.Value);
            command.Parameters.AddWithValue("@asset_revision_id", assetRevisionId.Value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int candidateIndex = reader.GetInt32(0);
                FaceOccurrenceId[] options = reader.GetFieldValue<Guid[]>(7).Select(FaceOccurrenceId.From).ToArray();
                candidates.Add(new CatalogueDetectorReconciliationCandidate(
                    candidateIndex,
                    FromStorage(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : FaceOccurrenceId.From(reader.GetGuid(2)),
                    options,
                    DeserializeBoundingBox(reader.GetString(3)),
                    DeserializeLandmarks(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : FaceOccurrenceId.From(reader.GetGuid(5)),
                    reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)));
            }
        }

        List<FaceOccurrenceId> unmatched = [];
        using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT face_occurrence_id
                FROM detector_reconciliation_unmatched_existing
                WHERE processing_run_id = @processing_run_id
                  AND asset_revision_id = @asset_revision_id
                ORDER BY face_occurrence_id;
                """;
            command.Parameters.AddWithValue("@processing_run_id", processingRunId.Value);
            command.Parameters.AddWithValue("@asset_revision_id", assetRevisionId.Value);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                unmatched.Add(FaceOccurrenceId.From(reader.GetGuid(0)));
            }
        }

        return new CatalogueDetectorReconciliationPlan(
            processingRunId,
            assetRevisionId,
            pipelineHash,
            plannedAt,
            candidates,
            unmatched);
    }

    private static CatalogueDetectorReconciliationPlan ToCataloguePlan(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        Sha256Digest pipelineHash,
        DateTimeOffset plannedAt,
        IReadOnlyDictionary<int, CandidateFaceDetectionAnchor> anchors,
        FaceDetectionReconciliationPlan plan) =>
        new(
            processingRunId,
            assetRevisionId,
            pipelineHash,
            plannedAt,
            plan.CandidateDecisions
                .OrderBy(value => value.CandidateIndex)
                .Select(decision => new CatalogueDetectorReconciliationCandidate(
                    decision.CandidateIndex,
                    decision.Disposition,
                    decision.ExistingFaceOccurrenceId,
                    decision.PossibleExistingFaceOccurrenceIds
                        .OrderBy(value => value.ToString(), StringComparer.Ordinal)
                        .ToArray(),
                    anchors[decision.CandidateIndex].BoundingBox,
                    anchors[decision.CandidateIndex].Landmarks,
                    null,
                    null))
                .ToArray(),
            plan.ExistingOccurrencesWithoutCandidate
                .OrderBy(value => value.ToString(), StringComparer.Ordinal)
                .ToArray());

    private static bool PlansEquivalent(
        CatalogueDetectorReconciliationPlan expected,
        CatalogueDetectorReconciliationPlan actual)
    {
        if (expected.ProcessingRunId != actual.ProcessingRunId ||
            expected.AssetRevisionId != actual.AssetRevisionId ||
            expected.PipelineHash != actual.PipelineHash ||
            expected.PlannedAtUtc.UtcTicks / 10 != actual.PlannedAtUtc.UtcTicks / 10 ||
            expected.Candidates.Count != actual.Candidates.Count ||
            !expected.ExistingOccurrencesWithoutCandidate.SequenceEqual(actual.ExistingOccurrencesWithoutCandidate))
        {
            return false;
        }

        for (int index = 0; index < expected.Candidates.Count; index++)
        {
            CatalogueDetectorReconciliationCandidate left = expected.Candidates[index];
            CatalogueDetectorReconciliationCandidate right = actual.Candidates[index];
            if (left.CandidateIndex != right.CandidateIndex ||
                left.Disposition != right.Disposition ||
                left.ProposedFaceOccurrenceId != right.ProposedFaceOccurrenceId ||
                !left.PossibleFaceOccurrenceIds.SequenceEqual(right.PossibleFaceOccurrenceIds) ||
                !GeometryEquals(left.BoundingBox, right.BoundingBox) ||
                !LandmarksEqual(left.Landmarks, right.Landmarks) ||
                right.AppliedFaceOccurrenceId is not null ||
                right.AppliedAtUtc is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static string ToStorage(FaceDetectionReconciliationDisposition disposition) => disposition switch
    {
        FaceDetectionReconciliationDisposition.ExistingOccurrence => "existing",
        FaceDetectionReconciliationDisposition.NewOccurrence => "new",
        FaceDetectionReconciliationDisposition.Ambiguous => "ambiguous",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
    };

    private static FaceDetectionReconciliationDisposition FromStorage(string value) => value switch
    {
        "existing" => FaceDetectionReconciliationDisposition.ExistingOccurrence,
        "new" => FaceDetectionReconciliationDisposition.NewOccurrence,
        "ambiguous" => FaceDetectionReconciliationDisposition.Ambiguous,
        _ => throw new DataException($"Unsupported detector reconciliation disposition '{value}'."),
    };

    private static string SerializeBoundingBox(NormalizedBoundingBox value) =>
        JsonSerializer.Serialize(new[] { value.X, value.Y, value.Width, value.Height });

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

    private static string SerializeLandmarks(NormalizedFaceLandmarks value) =>
        JsonSerializer.Serialize(new[]
        {
            new[] { value.LeftEye.X, value.LeftEye.Y },
            new[] { value.RightEye.X, value.RightEye.Y },
            new[] { value.Nose.X, value.Nose.Y },
            new[] { value.MouthLeft.X, value.MouthLeft.Y },
            new[] { value.MouthRight.X, value.MouthRight.Y },
        });

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

    private static bool GeometryEquals(NormalizedBoundingBox left, NormalizedBoundingBox right) =>
        left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height;

    private static bool LandmarksEqual(NormalizedFaceLandmarks left, NormalizedFaceLandmarks right) =>
        left.LeftEye == right.LeftEye &&
        left.RightEye == right.RightEye &&
        left.Nose == right.Nose &&
        left.MouthLeft == right.MouthLeft &&
        left.MouthRight == right.MouthRight;

}
