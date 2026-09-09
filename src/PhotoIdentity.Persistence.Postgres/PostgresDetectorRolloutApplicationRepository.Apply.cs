using System.Buffers.Binary;
using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

public sealed partial class PostgresDetectorRolloutApplicationRepository
{
    public Task<FaceOccurrenceId> ApplyUnambiguousInspectionAsync(
        ProcessingRunId processingRunId, AssetRevisionId assetRevisionId, int candidateIndex,
        CatalogueDetectorCandidateInspection inspection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return ApplyAsync(processingRunId, assetRevisionId, candidateIndex, inspection, cancellationToken);
    }

    public Task<FaceOccurrenceId> ApplyReviewedCandidateAsync(
        ProcessingRunId processingRunId, AssetRevisionId assetRevisionId, int candidateIndex,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(processingRunId, assetRevisionId, candidateIndex, null, cancellationToken);

    private async Task<FaceOccurrenceId> ApplyAsync(
        ProcessingRunId run, AssetRevisionId revision, int index,
        CatalogueDetectorCandidateInspection? supplied, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Serialize ordinal allocation for the revision and resolution/application for the candidate.
        await using (NpgsqlCommand guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = "SELECT id FROM asset_revisions WHERE id = @revision FOR UPDATE;";
            guard.Parameters.AddWithValue("revision", revision.Value);
            if (await guard.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new KeyNotFoundException("The candidate asset revision does not exist.");
            }
            guard.CommandText = """
                SELECT candidate_index FROM detector_reconciliation_candidates
                WHERE processing_run_id = @run AND asset_revision_id = @revision AND candidate_index = @index
                FOR UPDATE;
                """;
            guard.Parameters.AddWithValue("run", run.Value);
            guard.Parameters.AddWithValue("index", index);
            if (await guard.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new KeyNotFoundException("The reconciliation candidate does not exist.");
            }
        }

        CatalogueDetectorReconciliationReview review = await PostgresDetectorRolloutReviewRepository.ReadReviewAsync(
            connection, transaction, run, revision, index, cancellationToken)
            ?? throw new DataException("The locked reconciliation candidate disappeared.");
        bool reviewed = supplied is null;
        if (reviewed && review.Candidate.AppliedFaceOccurrenceId is FaceOccurrenceId previouslyApplied)
        {
            await transaction.CommitAsync(cancellationToken);
            return previouslyApplied;
        }
        if (reviewed != (review.Candidate.Disposition == FaceDetectionReconciliationDisposition.Ambiguous))
        {
            throw new InvalidOperationException("Ambiguous candidates require human resolution; unambiguous candidates use their persisted plan.");
        }

        CatalogueDetectorCandidateInspection inspection = supplied ?? review.Inspection
            ?? throw new InvalidOperationException("The candidate has no durable inspection payload.");
        if (review.Candidate.BoundingBox != inspection.BoundingBox || review.Candidate.Landmarks != inspection.Landmarks)
        {
            throw new InvalidOperationException("The inspection geometry differs from the persisted candidate evidence.");
        }
        var pipeline = await ReadRunPipelineAsync(connection, run, cancellationToken)
            ?? throw new InvalidOperationException("The run has no detector-pipeline provenance.");
        if (pipeline.ModelId != inspection.DetectorModelId.ToString() || pipeline.ModelHash != inspection.DetectorModelHash.ToString())
        {
            throw new InvalidOperationException("The inspection detector differs from the registered pipeline.");
        }

        FaceOccurrenceId? target = review.Candidate.AppliedFaceOccurrenceId ?? review.Candidate.ProposedFaceOccurrenceId;
        if (reviewed)
        {
            CatalogueDetectorReconciliationResolution resolution = review.LatestResolution
                ?? throw new InvalidOperationException("The ambiguous candidate has not been resolved.");
            target = resolution.Kind switch
            {
                DetectorReconciliationResolutionKind.ExistingOccurrence => resolution.FaceOccurrenceId
                    ?? throw new DataException("The existing-face resolution has no target."),
                DetectorReconciliationResolutionKind.NewOccurrence => null,
                _ => throw new InvalidOperationException("A deferred candidate cannot be applied."),
            };
            if (target is FaceOccurrenceId selected && !review.Candidate.PossibleFaceOccurrenceIds.Contains(selected))
            {
                throw new InvalidOperationException("The resolved face is not a persisted candidate option.");
            }
        }

        // Recognize a partial application imported from the legacy non-atomic reviewed path.
        if (reviewed && target is null)
        {
            await using NpgsqlCommand recover = connection.CreateCommand();
            recover.Transaction = transaction;
            recover.CommandText = """
                SELECT occurrence.id FROM face_occurrences AS occurrence
                JOIN face_crops AS crop ON crop.face_occurrence_id = occurrence.id
                JOIN face_observations AS observation ON observation.face_occurrence_id = occurrence.id
                WHERE occurrence.asset_revision_id = @revision AND crop.crop_protocol = @protocol
                  AND crop.content_sha256 = @hash AND crop.storage_path = @path
                  AND observation.detector_model_id = @model AND observation.detector_model_hash = @model_hash
                ORDER BY occurrence.ordinal, occurrence.id LIMIT 2;
                """;
            recover.Parameters.AddWithValue("revision", revision.Value);
            recover.Parameters.AddWithValue("protocol", inspection.CropProtocol.ToString());
            recover.Parameters.AddWithValue("hash", inspection.CropContentHash.ToString());
            recover.Parameters.AddWithValue("path", inspection.CropStoragePath);
            recover.Parameters.AddWithValue("model", inspection.DetectorModelId.ToString());
            recover.Parameters.AddWithValue("model_hash", inspection.DetectorModelHash.ToString());
            await using NpgsqlDataReader reader = await recover.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                target = FaceOccurrenceId.From(reader.GetGuid(0));
                if (await reader.ReadAsync(cancellationToken))
                {
                    throw new DataException("Multiple occurrences contain the same rollout payload.");
                }
            }
        }

        await using (NpgsqlCommand occurrence = connection.CreateCommand())
        {
            occurrence.Transaction = transaction;
            occurrence.Parameters.AddWithValue("revision", revision.Value);
            if (target is FaceOccurrenceId existing)
            {
                occurrence.CommandText = "SELECT id FROM face_occurrences WHERE id = @face AND asset_revision_id = @revision;";
                occurrence.Parameters.AddWithValue("face", existing.Value);
                if (await occurrence.ExecuteScalarAsync(cancellationToken) is null)
                {
                    throw new InvalidOperationException("The target face does not belong to the candidate revision.");
                }
            }
            else
            {
                target = FaceOccurrenceId.New();
                occurrence.CommandText = """
                    INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                    SELECT @face, @revision, COALESCE(MAX(ordinal), -1) + 1, @now
                    FROM face_occurrences WHERE asset_revision_id = @revision;
                    """;
                occurrence.Parameters.AddWithValue("face", target.Value.Value);
                occurrence.Parameters.AddWithValue("now", inspection.ObservedAtUtc);
                await occurrence.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await PersistInspectionAsync(connection, transaction, target.Value, pipeline.Hash, inspection, cancellationToken);
        await using (NpgsqlCommand applied = connection.CreateCommand())
        {
            applied.Transaction = transaction;
            applied.CommandText = """
                UPDATE detector_reconciliation_candidates
                SET applied_face_occurrence_id = @face, applied_at_utc = COALESCE(applied_at_utc, @now)
                WHERE processing_run_id = @run AND asset_revision_id = @revision AND candidate_index = @index;
                """;
            applied.Parameters.AddWithValue("face", target.Value.Value);
            applied.Parameters.AddWithValue("now", inspection.ObservedAtUtc);
            applied.Parameters.AddWithValue("run", run.Value);
            applied.Parameters.AddWithValue("revision", revision.Value);
            applied.Parameters.AddWithValue("index", index);
            if (await applied.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new DataException("The locked candidate could not be marked applied.");
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return target.Value;
    }

    private static async Task PersistInspectionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        FaceOccurrenceId face, Sha256Digest pipeline, CatalogueDetectorCandidateInspection inspection,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO face_observations (face_occurrence_id, detector_model_id, detector_model_hash,
                confidence, bounding_box_json, landmarks_json, observed_at_utc, detector_pipeline_hash)
            VALUES (@face, @detector, @detector_hash, @confidence, @box, @landmarks, @now, @pipeline)
            ON CONFLICT (face_occurrence_id, detector_model_id, detector_model_hash) DO UPDATE SET
                confidence = EXCLUDED.confidence, bounding_box_json = EXCLUDED.bounding_box_json,
                landmarks_json = EXCLUDED.landmarks_json, observed_at_utc = EXCLUDED.observed_at_utc,
                detector_pipeline_hash = EXCLUDED.detector_pipeline_hash
            WHERE face_observations.detector_pipeline_hash IS NULL OR face_observations.detector_pipeline_hash = EXCLUDED.detector_pipeline_hash;
            """;
        command.Parameters.AddWithValue("face", face.Value);
        command.Parameters.AddWithValue("detector", inspection.DetectorModelId.ToString());
        command.Parameters.AddWithValue("detector_hash", inspection.DetectorModelHash.ToString());
        command.Parameters.AddWithValue("confidence", inspection.Confidence);
        var box = inspection.BoundingBox;
        var landmarks = inspection.Landmarks;
        command.Parameters.AddWithValue("box", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new[] { box.X, box.Y, box.Width, box.Height }));
        command.Parameters.AddWithValue("landmarks", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new[]
        {
            new[] { landmarks.LeftEye.X, landmarks.LeftEye.Y }, new[] { landmarks.RightEye.X, landmarks.RightEye.Y },
            new[] { landmarks.Nose.X, landmarks.Nose.Y }, new[] { landmarks.MouthLeft.X, landmarks.MouthLeft.Y },
            new[] { landmarks.MouthRight.X, landmarks.MouthRight.Y },
        }));
        command.Parameters.AddWithValue("now", inspection.ObservedAtUtc);
        command.Parameters.AddWithValue("pipeline", pipeline.ToString());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("The face observation belongs to a different detector pipeline.");
        }

        command.CommandText = """
            INSERT INTO face_crops (id, face_occurrence_id, crop_protocol, content_sha256, storage_path, width, height, created_at_utc)
            VALUES (@crop, @face, @protocol, @hash, @path, @width, @height, @now)
            ON CONFLICT (face_occurrence_id, crop_protocol, content_sha256) DO UPDATE SET
                storage_path = EXCLUDED.storage_path, width = EXCLUDED.width, height = EXCLUDED.height
            RETURNING id;
            """;
        command.Parameters.AddWithValue("crop", inspection.CropId.Value);
        command.Parameters.AddWithValue("protocol", inspection.CropProtocol.ToString());
        command.Parameters.AddWithValue("hash", inspection.CropContentHash.ToString());
        command.Parameters.AddWithValue("path", inspection.CropStoragePath);
        command.Parameters.AddWithValue("width", inspection.CropWidth);
        command.Parameters.AddWithValue("height", inspection.CropHeight);
        Guid persistedCrop = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new DataException("The applied crop could not be read."));
        command.CommandText = """
            INSERT INTO embeddings (face_crop_id, model_id, model_hash, dimensions, l2_norm, vector_blob, created_at_utc)
            VALUES (@persisted_crop, @embedder, @embedder_hash, @dimensions, @norm, @vector, @now)
            ON CONFLICT (face_crop_id, model_id, model_hash) DO NOTHING;
            """;
        byte[] vector = new byte[inspection.Embedding.Dimensions * sizeof(float)];
        for (int i = 0; i < inspection.Embedding.Dimensions; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(vector.AsSpan(i * sizeof(float), sizeof(float)), inspection.Embedding.Values[i]);
        }
        command.Parameters.AddWithValue("persisted_crop", persistedCrop);
        command.Parameters.AddWithValue("embedder", inspection.EmbedderModelId.ToString());
        command.Parameters.AddWithValue("embedder_hash", inspection.EmbedderModelHash.ToString());
        command.Parameters.AddWithValue("dimensions", inspection.Embedding.Dimensions);
        command.Parameters.AddWithValue("norm", inspection.Embedding.L2Norm);
        command.Parameters.AddWithValue("vector", vector);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
