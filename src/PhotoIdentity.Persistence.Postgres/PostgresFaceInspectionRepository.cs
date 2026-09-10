using System.Buffers.Binary;
using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Persists one normal inspection result atomically without taking ownership of detector-rollout
/// observations that carry explicit pipeline provenance.
/// </summary>
public sealed class PostgresFaceInspectionRepository : IFaceInspectionRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresFaceInspectionRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task SaveInspectionAsync(
        FaceInspectionWrite inspection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand guard = connection.CreateCommand())
        {
            guard.Transaction = transaction;
            guard.CommandText = "SELECT id FROM asset_revisions WHERE id = @revision FOR UPDATE;";
            guard.Parameters.AddWithValue("revision", inspection.AssetRevisionId.Value);
            if (await guard.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new KeyNotFoundException($"Asset revision {inspection.AssetRevisionId} was not found.");
            }
        }

        Guid persistedOccurrenceId;
        await using (NpgsqlCommand occurrence = connection.CreateCommand())
        {
            occurrence.Transaction = transaction;
            occurrence.CommandText = """
                INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES (@id, @revision, @ordinal, @created_at)
                ON CONFLICT (asset_revision_id, ordinal) DO NOTHING;

                SELECT id
                FROM face_occurrences
                WHERE asset_revision_id = @revision AND ordinal = @ordinal;
                """;
            occurrence.Parameters.AddWithValue("id", inspection.OccurrenceId.Value);
            occurrence.Parameters.AddWithValue("revision", inspection.AssetRevisionId.Value);
            occurrence.Parameters.AddWithValue("ordinal", inspection.Ordinal);
            occurrence.Parameters.AddWithValue("created_at", inspection.ObservedAtUtc);
            persistedOccurrenceId = (Guid)(await occurrence.ExecuteScalarAsync(cancellationToken)
                ?? throw new DataException("The face occurrence was not available after it was persisted."));
        }

        await using (NpgsqlCommand observation = connection.CreateCommand())
        {
            observation.Transaction = transaction;
            observation.CommandText = """
                INSERT INTO face_observations (
                    face_occurrence_id, detector_model_id, detector_model_hash, confidence,
                    bounding_box_json, landmarks_json, observed_at_utc, detector_pipeline_hash)
                VALUES (
                    @face, @detector, @detector_hash, @confidence,
                    @box, @landmarks, @observed_at, NULL)
                ON CONFLICT (face_occurrence_id, detector_model_id, detector_model_hash) DO UPDATE SET
                    confidence = EXCLUDED.confidence,
                    bounding_box_json = EXCLUDED.bounding_box_json,
                    landmarks_json = EXCLUDED.landmarks_json,
                    observed_at_utc = EXCLUDED.observed_at_utc
                WHERE face_observations.detector_pipeline_hash IS NULL;
                """;
            observation.Parameters.AddWithValue("face", persistedOccurrenceId);
            observation.Parameters.AddWithValue("detector", inspection.DetectorModelId.ToString());
            observation.Parameters.AddWithValue("detector_hash", inspection.DetectorModelHash.ToString());
            observation.Parameters.AddWithValue("confidence", inspection.Confidence);
            observation.Parameters.AddWithValue(
                "box",
                NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(new[]
                {
                    inspection.BoundingBox.X,
                    inspection.BoundingBox.Y,
                    inspection.BoundingBox.Width,
                    inspection.BoundingBox.Height,
                }));
            observation.Parameters.AddWithValue(
                "landmarks",
                NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(new[]
                {
                    new[] { inspection.Landmarks.LeftEye.X, inspection.Landmarks.LeftEye.Y },
                    new[] { inspection.Landmarks.RightEye.X, inspection.Landmarks.RightEye.Y },
                    new[] { inspection.Landmarks.Nose.X, inspection.Landmarks.Nose.Y },
                    new[] { inspection.Landmarks.MouthLeft.X, inspection.Landmarks.MouthLeft.Y },
                    new[] { inspection.Landmarks.MouthRight.X, inspection.Landmarks.MouthRight.Y },
                }));
            observation.Parameters.AddWithValue("observed_at", inspection.ObservedAtUtc);
            if (await observation.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "The face observation is owned by detector-rollout pipeline provenance and cannot be overwritten by normal inspection.");
            }
        }

        Guid persistedCropId;
        await using (NpgsqlCommand crop = connection.CreateCommand())
        {
            crop.Transaction = transaction;
            crop.CommandText = """
                INSERT INTO face_crops (
                    id, face_occurrence_id, crop_protocol, content_sha256,
                    storage_path, width, height, created_at_utc)
                VALUES (
                    @id, @face, @protocol, @hash,
                    @path, @width, @height, @created_at)
                ON CONFLICT (face_occurrence_id, crop_protocol, content_sha256) DO UPDATE SET
                    storage_path = EXCLUDED.storage_path,
                    width = EXCLUDED.width,
                    height = EXCLUDED.height
                RETURNING id;
                """;
            crop.Parameters.AddWithValue("id", inspection.CropId.Value);
            crop.Parameters.AddWithValue("face", persistedOccurrenceId);
            crop.Parameters.AddWithValue("protocol", inspection.CropProtocol.ToString());
            crop.Parameters.AddWithValue("hash", inspection.CropContentHash.ToString());
            crop.Parameters.AddWithValue("path", inspection.CropStoragePath);
            crop.Parameters.AddWithValue("width", inspection.CropWidth);
            crop.Parameters.AddWithValue("height", inspection.CropHeight);
            crop.Parameters.AddWithValue("created_at", inspection.ObservedAtUtc);
            persistedCropId = (Guid)(await crop.ExecuteScalarAsync(cancellationToken)
                ?? throw new DataException("The face crop was not available after it was persisted."));
        }

        await using (NpgsqlCommand embedding = connection.CreateCommand())
        {
            embedding.Transaction = transaction;
            embedding.CommandText = """
                INSERT INTO embeddings (
                    face_crop_id, model_id, model_hash, dimensions,
                    l2_norm, vector_blob, created_at_utc)
                VALUES (
                    @crop, @model, @model_hash, @dimensions,
                    @norm, @vector, @created_at)
                ON CONFLICT (face_crop_id, model_id, model_hash) DO NOTHING;
                """;
            byte[] vector = new byte[inspection.Embedding.Dimensions * sizeof(float)];
            for (int index = 0; index < inspection.Embedding.Dimensions; index++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    vector.AsSpan(index * sizeof(float), sizeof(float)),
                    inspection.Embedding.Values[index]);
            }

            embedding.Parameters.AddWithValue("crop", persistedCropId);
            embedding.Parameters.AddWithValue("model", inspection.EmbeddingModelId.ToString());
            embedding.Parameters.AddWithValue("model_hash", inspection.EmbeddingModelHash.ToString());
            embedding.Parameters.AddWithValue("dimensions", inspection.Embedding.Dimensions);
            embedding.Parameters.AddWithValue("norm", inspection.Embedding.L2Norm);
            embedding.Parameters.AddWithValue("vector", vector);
            embedding.Parameters.AddWithValue("created_at", inspection.ObservedAtUtc);
            await embedding.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
