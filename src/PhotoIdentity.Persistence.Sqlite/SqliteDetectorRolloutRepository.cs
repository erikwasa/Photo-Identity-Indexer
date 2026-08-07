using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Persistence boundary for detector replacement. Unlike the ordinary inspection writer,
/// this repository never uses a candidate ordinal as evidence that two detections are the same face.
/// </summary>
public sealed class SqliteDetectorRolloutRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteDetectorRolloutRepository(SqliteCatalogueDatabase database)
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

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand command = connection.CreateCommand())
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
                    $pipeline_hash,
                    $detector_model_id,
                    $detector_model_hash,
                    $canonical_definition,
                    $recorded_at_utc)
                ON CONFLICT(pipeline_hash) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$pipeline_hash", pipelineHash.ToString());
            command.Parameters.AddWithValue("$detector_model_id", definition.DetectorModelId.ToString());
            command.Parameters.AddWithValue("$detector_model_hash", definition.DetectorModelHash.ToString());
            command.Parameters.AddWithValue("$canonical_definition", canonicalDefinition);
            command.Parameters.AddWithValue("$recorded_at_utc", Format(recordedAt));
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

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO processing_run_detector_pipelines (
                    processing_run_id,
                    pipeline_hash,
                    recorded_at_utc)
                VALUES ($processing_run_id, $pipeline_hash, $recorded_at_utc)
                ON CONFLICT(processing_run_id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            command.Parameters.AddWithValue("$pipeline_hash", pipelineHash.ToString());
            command.Parameters.AddWithValue("$recorded_at_utc", Format(recordedAt));
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

        transaction.Commit();
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
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

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

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO detector_reconciliation_plans (
                    processing_run_id,
                    asset_revision_id,
                    pipeline_hash,
                    planned_at_utc)
                VALUES ($processing_run_id, $asset_revision_id, $pipeline_hash, $planned_at_utc)
                ON CONFLICT(processing_run_id, asset_revision_id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
            command.Parameters.AddWithValue("$pipeline_hash", pipelineHash.ToString());
            command.Parameters.AddWithValue("$planned_at_utc", Format(plannedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (FaceDetectionReconciliationDecision decision in plan.CandidateDecisions)
        {
            CandidateFaceDetectionAnchor anchor = anchors[decision.CandidateIndex];
            using SqliteCommand command = connection.CreateCommand();
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
                    $processing_run_id,
                    $asset_revision_id,
                    $candidate_index,
                    $disposition,
                    $proposed_face_occurrence_id,
                    $bounding_box_json,
                    $landmarks_json)
                ON CONFLICT(processing_run_id, asset_revision_id, candidate_index) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
            command.Parameters.AddWithValue("$candidate_index", decision.CandidateIndex);
            command.Parameters.AddWithValue("$disposition", ToStorage(decision.Disposition));
            command.Parameters.AddWithValue(
                "$proposed_face_occurrence_id",
                decision.ExistingFaceOccurrenceId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$bounding_box_json", SerializeBoundingBox(anchor.BoundingBox));
            command.Parameters.AddWithValue("$landmarks_json", SerializeLandmarks(anchor.Landmarks));
            await command.ExecuteNonQueryAsync(cancellationToken);

            foreach (FaceOccurrenceId possible in decision.PossibleExistingFaceOccurrenceIds.Distinct())
            {
                using SqliteCommand option = connection.CreateCommand();
                option.Transaction = transaction;
                option.CommandText = """
                    INSERT OR IGNORE INTO detector_reconciliation_candidate_options (
                        processing_run_id,
                        asset_revision_id,
                        candidate_index,
                        face_occurrence_id)
                    VALUES ($processing_run_id, $asset_revision_id, $candidate_index, $face_occurrence_id);
                    """;
                option.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
                option.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
                option.Parameters.AddWithValue("$candidate_index", decision.CandidateIndex);
                option.Parameters.AddWithValue("$face_occurrence_id", possible.ToString());
                await option.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        foreach (FaceOccurrenceId unmatched in plan.ExistingOccurrencesWithoutCandidate.Distinct())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO detector_reconciliation_unmatched_existing (
                    processing_run_id,
                    asset_revision_id,
                    face_occurrence_id)
                VALUES ($processing_run_id, $asset_revision_id, $face_occurrence_id);
                """;
            command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
            command.Parameters.AddWithValue("$face_occurrence_id", unmatched.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueDetectorReconciliationPlan persisted = await ReadPlanAsync(
            connection,
            transaction,
            processingRunId,
            assetRevisionId,
            cancellationToken)
            ?? throw new InvalidOperationException("The reconciliation plan was unavailable after persistence.");

        CatalogueDetectorReconciliationPlan expected = ToCataloguePlan(
            processingRunId,
            assetRevisionId,
            pipelineHash,
            plannedAt,
            anchors,
            plan);
        if (!PlansEquivalent(expected, persisted))
        {
            throw new InvalidOperationException(
                "A different reconciliation plan is already persisted for this processing run and asset revision.");
        }

        transaction.Commit();
        return persisted;
    }

    public async Task<CatalogueDetectorReconciliationPlan?> GetPlanAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadPlanAsync(connection, transaction: null, processingRunId, assetRevisionId, cancellationToken);
    }

    /// <summary>
    /// Applies only an unambiguous persisted reconciliation decision. Ambiguous candidates are deliberately rejected
    /// until the human-review slice records a resolution. New faces receive an ordinal above all existing ordinals;
    /// candidate order is never reused as identity.
    /// </summary>
    public async Task<CatalogueFaceInspection> ApplyUnambiguousInspectionAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CatalogueDetectorCandidateInspection inspection,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidateIndex);
        ArgumentNullException.ThrowIfNull(inspection);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        (Sha256Digest PipelineHash, string ModelId, string ModelHash) pipeline =
            await ReadRunPipelineAsync(connection, transaction, processingRunId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Processing run {processingRunId} does not have registered detector-pipeline provenance.");
        if (!string.Equals(pipeline.ModelId, inspection.DetectorModelId.ToString(), StringComparison.Ordinal) ||
            !string.Equals(pipeline.ModelHash, inspection.DetectorModelHash.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The candidate inspection detector does not match the registered pipeline.");
        }

        PersistedCandidate candidate = await ReadCandidateAsync(
            connection,
            transaction,
            processingRunId,
            assetRevisionId,
            candidateIndex,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Reconciliation candidate {candidateIndex} for revision {assetRevisionId} was not found.");
        if (candidate.Disposition == FaceDetectionReconciliationDisposition.Ambiguous)
        {
            throw new InvalidOperationException(
                "Ambiguous detector reconciliation requires explicit human resolution before catalogue mutation.");
        }

        if (!GeometryEquals(candidate.BoundingBox, inspection.BoundingBox) ||
            !LandmarksEqual(candidate.Landmarks, inspection.Landmarks))
        {
            throw new InvalidOperationException(
                "The candidate inspection geometry does not match the persisted reconciliation evidence.");
        }

        CatalogueFaceOccurrence occurrence;
        if (candidate.AppliedFaceOccurrenceId is not null)
        {
            occurrence = await ReadOccurrenceByIdAsync(
                connection,
                transaction,
                candidate.AppliedFaceOccurrenceId.Value,
                cancellationToken)
                ?? throw new DataException("The applied reconciliation occurrence no longer exists.");
        }
        else if (candidate.Disposition == FaceDetectionReconciliationDisposition.ExistingOccurrence)
        {
            FaceOccurrenceId target = candidate.ProposedFaceOccurrenceId
                ?? throw new DataException("An existing-occurrence reconciliation is missing its target occurrence.");
            occurrence = await ReadOccurrenceByIdAsync(connection, transaction, target, cancellationToken)
                ?? throw new DataException("The proposed existing face occurrence no longer exists.");
        }
        else
        {
            occurrence = await CreateNewOccurrenceAsync(
                connection,
                transaction,
                assetRevisionId,
                inspection.ObservedAtUtc,
                cancellationToken);
        }

        if (occurrence.AssetRevisionId != assetRevisionId)
        {
            throw new InvalidOperationException("The reconciled occurrence belongs to a different asset revision.");
        }

        await UpsertObservationAsync(
            connection,
            transaction,
            occurrence.Id,
            pipeline.PipelineHash,
            inspection,
            cancellationToken);
        await UpsertCropAsync(connection, transaction, occurrence.Id, inspection, cancellationToken);
        CatalogueFaceCrop persistedCrop = await ReadCropAsync(
            connection,
            transaction,
            occurrence.Id,
            inspection.CropProtocol,
            inspection.CropContentHash,
            cancellationToken)
            ?? throw new InvalidOperationException("The reconciled face crop was unavailable after persistence.");
        await InsertEmbeddingAsync(connection, transaction, persistedCrop.Id, inspection, cancellationToken);
        CatalogueFaceEmbedding persistedEmbedding = await ReadEmbeddingAsync(
            connection,
            transaction,
            persistedCrop.Id,
            inspection.EmbedderModelId,
            inspection.EmbedderModelHash,
            cancellationToken)
            ?? throw new InvalidOperationException("The reconciled embedding was unavailable after persistence.");

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE detector_reconciliation_candidates
                SET applied_face_occurrence_id = $face_occurrence_id,
                    applied_at_utc = COALESCE(applied_at_utc, $applied_at_utc)
                WHERE processing_run_id = $processing_run_id
                  AND asset_revision_id = $asset_revision_id
                  AND candidate_index = $candidate_index
                  AND (applied_face_occurrence_id IS NULL OR applied_face_occurrence_id = $face_occurrence_id);
                """;
            command.Parameters.AddWithValue("$face_occurrence_id", occurrence.Id.ToString());
            command.Parameters.AddWithValue("$applied_at_utc", Format(inspection.ObservedAtUtc));
            command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
            command.Parameters.AddWithValue("$candidate_index", candidateIndex);
            int changed = await command.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 1)
            {
                throw new InvalidOperationException("The reconciliation candidate was already applied to a different occurrence.");
            }
        }

        transaction.Commit();

        CatalogueFaceObservation observation = new(
            occurrence.Id,
            inspection.DetectorModelId,
            inspection.DetectorModelHash,
            inspection.Confidence,
            inspection.BoundingBox,
            inspection.Landmarks,
            inspection.ObservedAtUtc);
        return new CatalogueFaceInspection(occurrence, observation, persistedCrop, persistedEmbedding);
    }

    private static async Task<(string ModelId, string ModelHash, string CanonicalDefinition, DateTimeOffset RecordedAt)?> ReadPipelineAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Sha256Digest pipelineHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT detector_model_id, detector_model_hash, canonical_definition, recorded_at_utc
            FROM detector_pipelines
            WHERE pipeline_hash = $pipeline_hash;
            """;
        command.Parameters.AddWithValue("$pipeline_hash", pipelineHash.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.GetString(1), reader.GetString(2), ParseTimestamp(reader.GetString(3)))
            : null;
    }

    private static async Task<Sha256Digest?> ReadRunPipelineHashAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT pipeline_hash
            FROM processing_run_detector_pipelines
            WHERE processing_run_id = $processing_run_id;
            """;
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text ? new Sha256Digest(text) : null;
    }

    private static async Task<(Sha256Digest PipelineHash, string ModelId, string ModelHash)?> ReadRunPipelineAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT link.pipeline_hash, pipeline.detector_model_id, pipeline.detector_model_hash
            FROM processing_run_detector_pipelines AS link
            INNER JOIN detector_pipelines AS pipeline ON pipeline.pipeline_hash = link.pipeline_hash
            WHERE link.processing_run_id = $processing_run_id;
            """;
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (new Sha256Digest(reader.GetString(0)), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static async Task<CatalogueDetectorReconciliationPlan?> ReadPlanAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        CancellationToken cancellationToken)
    {
        Sha256Digest pipelineHash;
        DateTimeOffset plannedAt;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT pipeline_hash, planned_at_utc
                FROM detector_reconciliation_plans
                WHERE processing_run_id = $processing_run_id
                  AND asset_revision_id = $asset_revision_id;
                """;
            command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            pipelineHash = new Sha256Digest(reader.GetString(0));
            plannedAt = ParseTimestamp(reader.GetString(1));
        }

        List<CatalogueDetectorReconciliationCandidate> candidates = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT candidate_index, disposition, proposed_face_occurrence_id,
                       bounding_box_json, landmarks_json, applied_face_occurrence_id, applied_at_utc
                FROM detector_reconciliation_candidates
                WHERE processing_run_id = $processing_run_id
                  AND asset_revision_id = $asset_revision_id
                ORDER BY candidate_index;
                """;
            command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int candidateIndex = reader.GetInt32(0);
                IReadOnlyList<FaceOccurrenceId> options = await ReadCandidateOptionsAsync(
                    connection,
                    transaction,
                    processingRunId,
                    assetRevisionId,
                    candidateIndex,
                    cancellationToken);
                candidates.Add(new CatalogueDetectorReconciliationCandidate(
                    candidateIndex,
                    FromStorage(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : FaceOccurrenceId.From(Guid.Parse(reader.GetString(2))),
                    options,
                    DeserializeBoundingBox(reader.GetString(3)),
                    DeserializeLandmarks(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : FaceOccurrenceId.From(Guid.Parse(reader.GetString(5))),
                    reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6))));
            }
        }

        List<FaceOccurrenceId> unmatched = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT face_occurrence_id
                FROM detector_reconciliation_unmatched_existing
                WHERE processing_run_id = $processing_run_id
                  AND asset_revision_id = $asset_revision_id
                ORDER BY face_occurrence_id;
                """;
            command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                unmatched.Add(FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))));
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

    private static async Task<IReadOnlyList<FaceOccurrenceId>> ReadCandidateOptionsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT face_occurrence_id
            FROM detector_reconciliation_candidate_options
            WHERE processing_run_id = $processing_run_id
              AND asset_revision_id = $asset_revision_id
              AND candidate_index = $candidate_index
            ORDER BY face_occurrence_id;
            """;
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        command.Parameters.AddWithValue("$candidate_index", candidateIndex);
        List<FaceOccurrenceId> values = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))));
        }

        return values;
    }

    private static async Task<PersistedCandidate?> ReadCandidateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT disposition, proposed_face_occurrence_id, bounding_box_json, landmarks_json,
                   applied_face_occurrence_id
            FROM detector_reconciliation_candidates
            WHERE processing_run_id = $processing_run_id
              AND asset_revision_id = $asset_revision_id
              AND candidate_index = $candidate_index;
            """;
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        command.Parameters.AddWithValue("$candidate_index", candidateIndex);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PersistedCandidate(
            FromStorage(reader.GetString(0)),
            reader.IsDBNull(1) ? null : FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))),
            DeserializeBoundingBox(reader.GetString(2)),
            DeserializeLandmarks(reader.GetString(3)),
            reader.IsDBNull(4) ? null : FaceOccurrenceId.From(Guid.Parse(reader.GetString(4))));
    }

    private static async Task<CatalogueFaceOccurrence> CreateNewOccurrenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRevisionId assetRevisionId,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        int nextOrdinal;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COALESCE(MAX(ordinal), -1) + 1
                FROM face_occurrences
                WHERE asset_revision_id = $asset_revision_id;
                """;
            command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
            nextOrdinal = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        CatalogueFaceOccurrence occurrence = new(
            FaceOccurrenceId.New(),
            assetRevisionId,
            nextOrdinal,
            createdAtUtc);
        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
            VALUES ($id, $asset_revision_id, $ordinal, $created_at_utc);
            """;
        insert.Parameters.AddWithValue("$id", occurrence.Id.ToString());
        insert.Parameters.AddWithValue("$asset_revision_id", occurrence.AssetRevisionId.ToString());
        insert.Parameters.AddWithValue("$ordinal", occurrence.Ordinal);
        insert.Parameters.AddWithValue("$created_at_utc", Format(occurrence.CreatedAtUtc));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return occurrence;
    }

    private static async Task<CatalogueFaceOccurrence?> ReadOccurrenceByIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId id,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, asset_revision_id, ordinal, created_at_utc
            FROM face_occurrences
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CatalogueFaceOccurrence(
                FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
                AssetRevisionId.From(Guid.Parse(reader.GetString(1))),
                reader.GetInt32(2),
                ParseTimestamp(reader.GetString(3)))
            : null;
    }

    private static async Task UpsertObservationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId occurrenceId,
        Sha256Digest pipelineHash,
        CatalogueDetectorCandidateInspection inspection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO face_observations (
                face_occurrence_id,
                detector_model_id,
                detector_model_hash,
                confidence,
                bounding_box_json,
                landmarks_json,
                observed_at_utc,
                detector_pipeline_hash)
            VALUES (
                $face_occurrence_id,
                $detector_model_id,
                $detector_model_hash,
                $confidence,
                $bounding_box_json,
                $landmarks_json,
                $observed_at_utc,
                $detector_pipeline_hash)
            ON CONFLICT(face_occurrence_id, detector_model_id, detector_model_hash) DO UPDATE SET
                confidence = excluded.confidence,
                bounding_box_json = excluded.bounding_box_json,
                landmarks_json = excluded.landmarks_json,
                observed_at_utc = excluded.observed_at_utc,
                detector_pipeline_hash = excluded.detector_pipeline_hash
            WHERE face_observations.detector_pipeline_hash IS NULL
               OR face_observations.detector_pipeline_hash = excluded.detector_pipeline_hash;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", occurrenceId.ToString());
        command.Parameters.AddWithValue("$detector_model_id", inspection.DetectorModelId.ToString());
        command.Parameters.AddWithValue("$detector_model_hash", inspection.DetectorModelHash.ToString());
        command.Parameters.AddWithValue("$confidence", inspection.Confidence);
        command.Parameters.AddWithValue("$bounding_box_json", SerializeBoundingBox(inspection.BoundingBox));
        command.Parameters.AddWithValue("$landmarks_json", SerializeLandmarks(inspection.Landmarks));
        command.Parameters.AddWithValue("$observed_at_utc", Format(inspection.ObservedAtUtc));
        command.Parameters.AddWithValue("$detector_pipeline_hash", pipelineHash.ToString());
        int changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed != 1)
        {
            throw new InvalidOperationException(
                "An observation for the same face and model bytes already belongs to a different detector pipeline; " +
                "rollout will not overwrite that provenance.");
        }
    }

    private static async Task UpsertCropAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId occurrenceId,
        CatalogueDetectorCandidateInspection inspection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO face_crops (
                id, face_occurrence_id, crop_protocol, content_sha256, storage_path,
                width, height, created_at_utc)
            VALUES (
                $id, $face_occurrence_id, $crop_protocol, $content_sha256, $storage_path,
                $width, $height, $created_at_utc)
            ON CONFLICT(face_occurrence_id, crop_protocol, content_sha256) DO UPDATE SET
                storage_path = excluded.storage_path,
                width = excluded.width,
                height = excluded.height;
            """;
        command.Parameters.AddWithValue("$id", inspection.CropId.ToString());
        command.Parameters.AddWithValue("$face_occurrence_id", occurrenceId.ToString());
        command.Parameters.AddWithValue("$crop_protocol", inspection.CropProtocol.ToString());
        command.Parameters.AddWithValue("$content_sha256", inspection.CropContentHash.ToString());
        command.Parameters.AddWithValue("$storage_path", inspection.CropStoragePath);
        command.Parameters.AddWithValue("$width", inspection.CropWidth);
        command.Parameters.AddWithValue("$height", inspection.CropHeight);
        command.Parameters.AddWithValue("$created_at_utc", Format(inspection.ObservedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CatalogueFaceCrop?> ReadCropAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId occurrenceId,
        AlignmentProtocolId protocol,
        Sha256Digest contentHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, face_occurrence_id, crop_protocol, content_sha256, storage_path,
                   width, height, created_at_utc
            FROM face_crops
            WHERE face_occurrence_id = $face_occurrence_id
              AND crop_protocol = $crop_protocol
              AND content_sha256 = $content_sha256;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", occurrenceId.ToString());
        command.Parameters.AddWithValue("$crop_protocol", protocol.ToString());
        command.Parameters.AddWithValue("$content_sha256", contentHash.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CatalogueFaceCrop(
                FaceCropId.From(Guid.Parse(reader.GetString(0))),
                FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))),
                new AlignmentProtocolId(reader.GetString(2)),
                new Sha256Digest(reader.GetString(3)),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                ParseTimestamp(reader.GetString(7)))
            : null;
    }

    private static async Task InsertEmbeddingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceCropId cropId,
        CatalogueDetectorCandidateInspection inspection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO embeddings (
                face_crop_id, model_id, model_hash, dimensions, l2_norm, vector_blob, created_at_utc)
            VALUES (
                $face_crop_id, $model_id, $model_hash, $dimensions, $l2_norm, $vector_blob, $created_at_utc)
            ON CONFLICT(face_crop_id, model_id, model_hash) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$face_crop_id", cropId.ToString());
        command.Parameters.AddWithValue("$model_id", inspection.EmbedderModelId.ToString());
        command.Parameters.AddWithValue("$model_hash", inspection.EmbedderModelHash.ToString());
        command.Parameters.AddWithValue("$dimensions", inspection.Embedding.Dimensions);
        command.Parameters.AddWithValue("$l2_norm", inspection.Embedding.L2Norm);
        command.Parameters.AddWithValue("$vector_blob", SerializeVector(inspection.Embedding));
        command.Parameters.AddWithValue("$created_at_utc", Format(inspection.ObservedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CatalogueFaceEmbedding?> ReadEmbeddingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceCropId cropId,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT face_crop_id, model_id, model_hash, dimensions, l2_norm, vector_blob, created_at_utc
            FROM embeddings
            WHERE face_crop_id = $face_crop_id
              AND model_id = $model_id
              AND model_hash = $model_hash;
            """;
        command.Parameters.AddWithValue("$face_crop_id", cropId.ToString());
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        int dimensions = reader.GetInt32(3);
        double storedNorm = reader.GetDouble(4);
        EmbeddingVector vector = DeserializeVector((byte[])reader.GetValue(5), dimensions);
        double tolerance = 1e-9 * Math.Max(1, storedNorm);
        if (Math.Abs(vector.L2Norm - storedNorm) > tolerance)
        {
            throw new DataException("The stored embedding norm does not match its vector data.");
        }

        return new CatalogueFaceEmbedding(
            FaceCropId.From(Guid.Parse(reader.GetString(0))),
            new ModelId(reader.GetString(1)),
            new Sha256Digest(reader.GetString(2)),
            vector,
            ParseTimestamp(reader.GetString(6)));
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
            expected.PlannedAtUtc != actual.PlannedAtUtc ||
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

    private static byte[] SerializeVector(EmbeddingVector vector)
    {
        ReadOnlySpan<float> values = vector.Values;
        byte[] bytes = new byte[checked(values.Length * sizeof(float))];
        for (int index = 0; index < values.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(index * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(values[index]));
        }

        return bytes;
    }

    private static EmbeddingVector DeserializeVector(byte[] bytes, int dimensions)
    {
        if (dimensions <= 0 || bytes.Length != checked(dimensions * sizeof(float)))
        {
            throw new DataException("The stored embedding dimensions do not match its vector data.");
        }

        float[] values = new float[dimensions];
        for (int index = 0; index < dimensions; index++)
        {
            int bits = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(index * sizeof(float), sizeof(float)));
            values[index] = BitConverter.Int32BitsToSingle(bits);
        }

        return new EmbeddingVector(values);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record PersistedCandidate(
        FaceDetectionReconciliationDisposition Disposition,
        FaceOccurrenceId? ProposedFaceOccurrenceId,
        NormalizedBoundingBox BoundingBox,
        NormalizedFaceLandmarks Landmarks,
        FaceOccurrenceId? AppliedFaceOccurrenceId);
}
