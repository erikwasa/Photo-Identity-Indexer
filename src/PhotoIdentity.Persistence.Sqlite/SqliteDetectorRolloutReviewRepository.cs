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
/// Durable review state for detector rollout. Candidate payloads are stored before canonical
/// face mutation, and ambiguous identity decisions are append-only human actions.
/// </summary>
public sealed class SqliteDetectorRolloutReviewRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteDetectorRolloutReviewRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueDetectorCandidateInspection> SaveInspectionAsync(
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

        (NormalizedBoundingBox Box, NormalizedFaceLandmarks Landmarks, FaceOccurrenceId? AppliedId) candidate =
            await ReadCandidateGeometryAsync(
                connection,
                transaction,
                processingRunId,
                assetRevisionId,
                candidateIndex,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Reconciliation candidate {candidateIndex} for revision {assetRevisionId} was not found.");
        if (candidate.AppliedId is not null)
        {
            throw new InvalidOperationException("A candidate inspection cannot be replaced after reconciliation has been applied.");
        }

        if (!GeometryEquals(candidate.Box, inspection.BoundingBox) ||
            !LandmarksEqual(candidate.Landmarks, inspection.Landmarks))
        {
            throw new InvalidOperationException(
                "The candidate inspection geometry does not match the persisted reconciliation plan.");
        }

        (string ModelId, string ModelHash) pipeline = await ReadRunPipelineAsync(
            connection,
            transaction,
            processingRunId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Processing run {processingRunId} does not have registered detector-pipeline provenance.");
        if (!string.Equals(pipeline.ModelId, inspection.DetectorModelId.ToString(), StringComparison.Ordinal) ||
            !string.Equals(pipeline.ModelHash, inspection.DetectorModelHash.ToString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The candidate inspection detector does not match the registered pipeline.");
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO detector_reconciliation_candidate_inspections (
                    processing_run_id,
                    asset_revision_id,
                    candidate_index,
                    detector_model_id,
                    detector_model_hash,
                    confidence,
                    crop_id,
                    crop_protocol,
                    crop_content_sha256,
                    crop_storage_path,
                    crop_width,
                    crop_height,
                    embedder_model_id,
                    embedder_model_hash,
                    embedding_dimensions,
                    embedding_l2_norm,
                    embedding_vector_blob,
                    observed_at_utc)
                VALUES (
                    $processing_run_id,
                    $asset_revision_id,
                    $candidate_index,
                    $detector_model_id,
                    $detector_model_hash,
                    $confidence,
                    $crop_id,
                    $crop_protocol,
                    $crop_content_sha256,
                    $crop_storage_path,
                    $crop_width,
                    $crop_height,
                    $embedder_model_id,
                    $embedder_model_hash,
                    $embedding_dimensions,
                    $embedding_l2_norm,
                    $embedding_vector_blob,
                    $observed_at_utc)
                ON CONFLICT(processing_run_id, asset_revision_id, candidate_index) DO NOTHING;
                """;
            AddInspectionParameters(command, processingRunId, assetRevisionId, candidateIndex, inspection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueDetectorCandidateInspection persisted = await ReadInspectionAsync(
            connection,
            transaction,
            processingRunId,
            assetRevisionId,
            candidateIndex,
            cancellationToken)
            ?? throw new InvalidOperationException("The rollout candidate inspection was unavailable after persistence.");
        if (!InspectionsEquivalent(inspection, persisted))
        {
            throw new InvalidOperationException(
                "A different durable inspection payload already exists for this rollout candidate.");
        }

        transaction.Commit();
        return persisted;
    }

    public async Task<CatalogueDetectorCandidateInspection?> GetInspectionAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidateIndex);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadInspectionAsync(
            connection,
            transaction: null,
            processingRunId,
            assetRevisionId,
            candidateIndex,
            cancellationToken);
    }

    public async Task<CatalogueDetectorReconciliationResolution> RecordResolutionAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        DetectorReconciliationResolutionKind kind,
        FaceOccurrenceId? faceOccurrenceId,
        string actor,
        DateTimeOffset createdAtUtc,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidateIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        string canonicalActor = actor.Trim();
        string? canonicalNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DateTimeOffset createdAt = createdAtUtc.ToUniversalTime();

        if (kind == DetectorReconciliationResolutionKind.ExistingOccurrence && faceOccurrenceId is null)
        {
            throw new ArgumentException("An existing-occurrence resolution requires a face occurrence.", nameof(faceOccurrenceId));
        }

        if (kind != DetectorReconciliationResolutionKind.ExistingOccurrence && faceOccurrenceId is not null)
        {
            throw new ArgumentException("Only an existing-occurrence resolution may include a face occurrence.", nameof(faceOccurrenceId));
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        (FaceDetectionReconciliationDisposition Disposition, FaceOccurrenceId? AppliedId) state =
            await ReadCandidateStateAsync(
                connection,
                transaction,
                processingRunId,
                assetRevisionId,
                candidateIndex,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Reconciliation candidate {candidateIndex} for revision {assetRevisionId} was not found.");
        if (state.Disposition != FaceDetectionReconciliationDisposition.Ambiguous)
        {
            throw new InvalidOperationException("Only an ambiguous reconciliation candidate accepts a human resolution.");
        }

        if (state.AppliedId is not null)
        {
            throw new InvalidOperationException("The reconciliation candidate is already applied and cannot be re-resolved.");
        }

        if (kind == DetectorReconciliationResolutionKind.ExistingOccurrence)
        {
            FaceOccurrenceId target = faceOccurrenceId!.Value;
            if (!await IsCandidateOptionAsync(
                    connection,
                    transaction,
                    processingRunId,
                    assetRevisionId,
                    candidateIndex,
                    target,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "The selected existing face is not one of the persisted reconciliation options for this candidate.");
            }

            if (!await OccurrenceBelongsToRevisionAsync(
                    connection,
                    transaction,
                    target,
                    assetRevisionId,
                    cancellationToken))
            {
                throw new InvalidOperationException("The selected existing face belongs to a different asset revision.");
            }
        }

        CatalogueDetectorReconciliationResolution? latest = await ReadLatestResolutionAsync(
            connection,
            transaction,
            processingRunId,
            assetRevisionId,
            candidateIndex,
            cancellationToken);
        if (latest is not null &&
            latest.Kind == kind &&
            latest.FaceOccurrenceId == faceOccurrenceId &&
            string.Equals(latest.Actor, canonicalActor, StringComparison.Ordinal) &&
            string.Equals(latest.Note, canonicalNote, StringComparison.Ordinal))
        {
            transaction.Commit();
            return latest;
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO detector_reconciliation_resolution_actions (
                    processing_run_id,
                    asset_revision_id,
                    candidate_index,
                    action_kind,
                    face_occurrence_id,
                    actor,
                    note,
                    created_at_utc)
                VALUES (
                    $processing_run_id,
                    $asset_revision_id,
                    $candidate_index,
                    $action_kind,
                    $face_occurrence_id,
                    $actor,
                    $note,
                    $created_at_utc);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
            command.Parameters.AddWithValue("$candidate_index", candidateIndex);
            command.Parameters.AddWithValue("$action_kind", ToStorage(kind));
            command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$actor", canonicalActor);
            command.Parameters.AddWithValue("$note", canonicalNote ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$created_at_utc", Format(createdAt));
            long id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            transaction.Commit();
            return new CatalogueDetectorReconciliationResolution(
                id,
                processingRunId,
                assetRevisionId,
                candidateIndex,
                kind,
                faceOccurrenceId,
                canonicalActor,
                canonicalNote,
                createdAt);
        }
    }

    public async Task<IReadOnlyList<CatalogueDetectorReconciliationResolution>> GetResolutionHistoryAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidateIndex);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, action_kind, face_occurrence_id, actor, note, created_at_utc
            FROM detector_reconciliation_resolution_actions
            WHERE processing_run_id = $processing_run_id
              AND asset_revision_id = $asset_revision_id
              AND candidate_index = $candidate_index
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        command.Parameters.AddWithValue("$candidate_index", candidateIndex);
        List<CatalogueDetectorReconciliationResolution> values = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(ReadResolution(reader, processingRunId, assetRevisionId, candidateIndex));
        }

        return values;
    }

    public async Task<CatalogueDetectorReconciliationReview?> GetReviewAsync(
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidateIndex);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        CatalogueDetectorReconciliationCandidate? candidate = await ReadCandidateAsync(
            connection,
            processingRunId,
            assetRevisionId,
            candidateIndex,
            cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        CatalogueDetectorCandidateInspection? inspection = await ReadInspectionAsync(
            connection,
            transaction: null,
            processingRunId,
            assetRevisionId,
            candidateIndex,
            cancellationToken);
        CatalogueDetectorReconciliationResolution? resolution = await ReadLatestResolutionAsync(
            connection,
            transaction: null,
            processingRunId,
            assetRevisionId,
            candidateIndex,
            cancellationToken);
        return new CatalogueDetectorReconciliationReview(candidate, inspection, resolution);
    }

    public async Task<IReadOnlyList<CatalogueDetectorReconciliationReview>> GetPendingAmbiguousAsync(
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
                WHERE processing_run_id = $processing_run_id
                  AND disposition = 'ambiguous'
                  AND applied_face_occurrence_id IS NULL
                ORDER BY asset_revision_id, candidate_index;
                """;
            command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                keys.Add((AssetRevisionId.From(Guid.Parse(reader.GetString(0))), reader.GetInt32(1)));
            }
        }

        List<CatalogueDetectorReconciliationReview> reviews = [];
        foreach ((AssetRevisionId revisionId, int candidateIndex) in keys)
        {
            CatalogueDetectorReconciliationCandidate? candidate = await ReadCandidateAsync(
                connection,
                processingRunId,
                revisionId,
                candidateIndex,
                cancellationToken);
            if (candidate is null)
            {
                continue;
            }

            CatalogueDetectorCandidateInspection? inspection = await ReadInspectionAsync(
                connection,
                transaction: null,
                processingRunId,
                revisionId,
                candidateIndex,
                cancellationToken);
            CatalogueDetectorReconciliationResolution? resolution = await ReadLatestResolutionAsync(
                connection,
                transaction: null,
                processingRunId,
                revisionId,
                candidateIndex,
                cancellationToken);
            reviews.Add(new CatalogueDetectorReconciliationReview(candidate, inspection, resolution));
        }

        return reviews;
    }

    private static void AddInspectionParameters(
        SqliteCommand command,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CatalogueDetectorCandidateInspection inspection)
    {
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        command.Parameters.AddWithValue("$candidate_index", candidateIndex);
        command.Parameters.AddWithValue("$detector_model_id", inspection.DetectorModelId.ToString());
        command.Parameters.AddWithValue("$detector_model_hash", inspection.DetectorModelHash.ToString());
        command.Parameters.AddWithValue("$confidence", inspection.Confidence);
        command.Parameters.AddWithValue("$crop_id", inspection.CropId.ToString());
        command.Parameters.AddWithValue("$crop_protocol", inspection.CropProtocol.ToString());
        command.Parameters.AddWithValue("$crop_content_sha256", inspection.CropContentHash.ToString());
        command.Parameters.AddWithValue("$crop_storage_path", inspection.CropStoragePath);
        command.Parameters.AddWithValue("$crop_width", inspection.CropWidth);
        command.Parameters.AddWithValue("$crop_height", inspection.CropHeight);
        command.Parameters.AddWithValue("$embedder_model_id", inspection.EmbedderModelId.ToString());
        command.Parameters.AddWithValue("$embedder_model_hash", inspection.EmbedderModelHash.ToString());
        command.Parameters.AddWithValue("$embedding_dimensions", inspection.Embedding.Dimensions);
        command.Parameters.AddWithValue("$embedding_l2_norm", inspection.Embedding.L2Norm);
        command.Parameters.AddWithValue("$embedding_vector_blob", SerializeVector(inspection.Embedding));
        command.Parameters.AddWithValue("$observed_at_utc", Format(inspection.ObservedAtUtc));
    }

    private static async Task<CatalogueDetectorCandidateInspection?> ReadInspectionAsync(
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
            SELECT detector_model_id, detector_model_hash, confidence,
                   crop_id, crop_protocol, crop_content_sha256, crop_storage_path, crop_width, crop_height,
                   embedder_model_id, embedder_model_hash,
                   embedding_dimensions, embedding_l2_norm, embedding_vector_blob, observed_at_utc
            FROM detector_reconciliation_candidate_inspections
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

        int dimensions = reader.GetInt32(11);
        double storedNorm = reader.GetDouble(12);
        EmbeddingVector embedding = DeserializeVector((byte[])reader.GetValue(13), dimensions);
        double tolerance = 1e-9 * Math.Max(1, storedNorm);
        if (Math.Abs(embedding.L2Norm - storedNorm) > tolerance)
        {
            throw new DataException("The stored rollout embedding norm does not match its vector data.");
        }

        (NormalizedBoundingBox Box, NormalizedFaceLandmarks Landmarks, FaceOccurrenceId? AppliedId) candidate =
            await ReadCandidateGeometryAsync(
                connection,
                transaction,
                processingRunId,
                assetRevisionId,
                candidateIndex,
                cancellationToken)
            ?? throw new DataException("The rollout candidate disappeared while its inspection was being read.");

        return new CatalogueDetectorCandidateInspection(
            new ModelId(reader.GetString(0)),
            new Sha256Digest(reader.GetString(1)),
            reader.GetDouble(2),
            candidate.Box,
            candidate.Landmarks,
            FaceCropId.From(Guid.Parse(reader.GetString(3))),
            new AlignmentProtocolId(reader.GetString(4)),
            new Sha256Digest(reader.GetString(5)),
            reader.GetString(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            new ModelId(reader.GetString(9)),
            new Sha256Digest(reader.GetString(10)),
            embedding,
            ParseTimestamp(reader.GetString(14)));
    }

    private static async Task<CatalogueDetectorReconciliationCandidate?> ReadCandidateAsync(
        SqliteConnection connection,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT disposition, proposed_face_occurrence_id, bounding_box_json, landmarks_json,
                   applied_face_occurrence_id, applied_at_utc
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

        FaceDetectionReconciliationDisposition disposition = FromStorageDisposition(reader.GetString(0));
        FaceOccurrenceId? proposed = reader.IsDBNull(1)
            ? null
            : FaceOccurrenceId.From(Guid.Parse(reader.GetString(1)));
        NormalizedBoundingBox box = DeserializeBoundingBox(reader.GetString(2));
        NormalizedFaceLandmarks landmarks = DeserializeLandmarks(reader.GetString(3));
        FaceOccurrenceId? applied = reader.IsDBNull(4)
            ? null
            : FaceOccurrenceId.From(Guid.Parse(reader.GetString(4)));
        DateTimeOffset? appliedAt = reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5));
        await reader.DisposeAsync();

        IReadOnlyList<FaceOccurrenceId> options = await ReadOptionsAsync(
            connection,
            processingRunId,
            assetRevisionId,
            candidateIndex,
            cancellationToken);
        return new CatalogueDetectorReconciliationCandidate(
            candidateIndex,
            disposition,
            proposed,
            options,
            box,
            landmarks,
            applied,
            appliedAt);
    }

    private static async Task<IReadOnlyList<FaceOccurrenceId>> ReadOptionsAsync(
        SqliteConnection connection,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
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

    private static async Task<(NormalizedBoundingBox Box, NormalizedFaceLandmarks Landmarks, FaceOccurrenceId? AppliedId)?> ReadCandidateGeometryAsync(
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
            SELECT bounding_box_json, landmarks_json, applied_face_occurrence_id
            FROM detector_reconciliation_candidates
            WHERE processing_run_id = $processing_run_id
              AND asset_revision_id = $asset_revision_id
              AND candidate_index = $candidate_index;
            """;
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        command.Parameters.AddWithValue("$candidate_index", candidateIndex);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (
                DeserializeBoundingBox(reader.GetString(0)),
                DeserializeLandmarks(reader.GetString(1)),
                reader.IsDBNull(2) ? null : FaceOccurrenceId.From(Guid.Parse(reader.GetString(2))))
            : null;
    }

    private static async Task<(FaceDetectionReconciliationDisposition Disposition, FaceOccurrenceId? AppliedId)?> ReadCandidateStateAsync(
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
            SELECT disposition, applied_face_occurrence_id
            FROM detector_reconciliation_candidates
            WHERE processing_run_id = $processing_run_id
              AND asset_revision_id = $asset_revision_id
              AND candidate_index = $candidate_index;
            """;
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        command.Parameters.AddWithValue("$candidate_index", candidateIndex);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (
                FromStorageDisposition(reader.GetString(0)),
                reader.IsDBNull(1) ? null : FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))))
            : null;
    }

    private static async Task<(string ModelId, string ModelHash)?> ReadRunPipelineAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProcessingRunId processingRunId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT pipeline.detector_model_id, pipeline.detector_model_hash
            FROM processing_run_detector_pipelines AS link
            INNER JOIN detector_pipelines AS pipeline ON pipeline.pipeline_hash = link.pipeline_hash
            WHERE link.processing_run_id = $processing_run_id;
            """;
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.GetString(1))
            : null;
    }

    private static async Task<bool> IsCandidateOptionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM detector_reconciliation_candidate_options
            WHERE processing_run_id = $processing_run_id
              AND asset_revision_id = $asset_revision_id
              AND candidate_index = $candidate_index
              AND face_occurrence_id = $face_occurrence_id;
            """;
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        command.Parameters.AddWithValue("$candidate_index", candidateIndex);
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> OccurrenceBelongsToRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        AssetRevisionId assetRevisionId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM face_occurrences
            WHERE id = $face_occurrence_id
              AND asset_revision_id = $asset_revision_id;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<CatalogueDetectorReconciliationResolution?> ReadLatestResolutionAsync(
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
            SELECT id, action_kind, face_occurrence_id, actor, note, created_at_utc
            FROM detector_reconciliation_resolution_actions
            WHERE processing_run_id = $processing_run_id
              AND asset_revision_id = $asset_revision_id
              AND candidate_index = $candidate_index
            ORDER BY id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$processing_run_id", processingRunId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        command.Parameters.AddWithValue("$candidate_index", candidateIndex);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadResolution(reader, processingRunId, assetRevisionId, candidateIndex)
            : null;
    }

    private static CatalogueDetectorReconciliationResolution ReadResolution(
        SqliteDataReader reader,
        ProcessingRunId processingRunId,
        AssetRevisionId assetRevisionId,
        int candidateIndex) =>
        new(
            reader.GetInt64(0),
            processingRunId,
            assetRevisionId,
            candidateIndex,
            FromStorageResolution(reader.GetString(1)),
            reader.IsDBNull(2) ? null : FaceOccurrenceId.From(Guid.Parse(reader.GetString(2))),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            ParseTimestamp(reader.GetString(5)));

    private static bool InspectionsEquivalent(
        CatalogueDetectorCandidateInspection left,
        CatalogueDetectorCandidateInspection right) =>
        left.DetectorModelId == right.DetectorModelId &&
        left.DetectorModelHash == right.DetectorModelHash &&
        left.Confidence.Equals(right.Confidence) &&
        GeometryEquals(left.BoundingBox, right.BoundingBox) &&
        LandmarksEqual(left.Landmarks, right.Landmarks) &&
        left.CropId == right.CropId &&
        left.CropProtocol == right.CropProtocol &&
        left.CropContentHash == right.CropContentHash &&
        string.Equals(left.CropStoragePath, right.CropStoragePath, StringComparison.Ordinal) &&
        left.CropWidth == right.CropWidth &&
        left.CropHeight == right.CropHeight &&
        left.EmbedderModelId == right.EmbedderModelId &&
        left.EmbedderModelHash == right.EmbedderModelHash &&
        left.Embedding.Values.SequenceEqual(right.Embedding.Values) &&
        left.ObservedAtUtc.ToUniversalTime() == right.ObservedAtUtc.ToUniversalTime();

    private static bool GeometryEquals(NormalizedBoundingBox left, NormalizedBoundingBox right) =>
        left.X.Equals(right.X) &&
        left.Y.Equals(right.Y) &&
        left.Width.Equals(right.Width) &&
        left.Height.Equals(right.Height);

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
            throw new DataException("The stored rollout embedding dimensions do not match its vector data.");
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

    private static FaceDetectionReconciliationDisposition FromStorageDisposition(string value) => value switch
    {
        "existing" => FaceDetectionReconciliationDisposition.ExistingOccurrence,
        "new" => FaceDetectionReconciliationDisposition.NewOccurrence,
        "ambiguous" => FaceDetectionReconciliationDisposition.Ambiguous,
        _ => throw new DataException($"Unknown detector reconciliation disposition '{value}'."),
    };

    private static string ToStorage(DetectorReconciliationResolutionKind value) => value switch
    {
        DetectorReconciliationResolutionKind.ExistingOccurrence => "existing",
        DetectorReconciliationResolutionKind.NewOccurrence => "new",
        DetectorReconciliationResolutionKind.Deferred => "defer",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static DetectorReconciliationResolutionKind FromStorageResolution(string value) => value switch
    {
        "existing" => DetectorReconciliationResolutionKind.ExistingOccurrence,
        "new" => DetectorReconciliationResolutionKind.NewOccurrence,
        "defer" => DetectorReconciliationResolutionKind.Deferred,
        _ => throw new DataException($"Unknown detector reconciliation resolution '{value}'."),
    };

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
