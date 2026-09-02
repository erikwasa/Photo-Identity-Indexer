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
/// Persists complete face inspection results over the SQLite catalogue schema.
/// </summary>
public sealed class SqliteFaceCatalogueRepository : IFaceInspectionRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteFaceCatalogueRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task SaveInspectionAsync(
        FaceInspectionWrite inspection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        _ = await SaveInspectionAsync(
            new CatalogueFaceOccurrence(
                inspection.OccurrenceId,
                inspection.AssetRevisionId,
                inspection.Ordinal,
                inspection.ObservedAtUtc),
            new CatalogueFaceObservation(
                inspection.OccurrenceId,
                inspection.DetectorModelId,
                inspection.DetectorModelHash,
                inspection.Confidence,
                inspection.BoundingBox,
                inspection.Landmarks,
                inspection.ObservedAtUtc),
            new CatalogueFaceCrop(
                inspection.CropId,
                inspection.OccurrenceId,
                inspection.CropProtocol,
                inspection.CropContentHash,
                inspection.CropStoragePath,
                inspection.CropWidth,
                inspection.CropHeight,
                inspection.ObservedAtUtc),
            new CatalogueFaceEmbedding(
                inspection.CropId,
                inspection.EmbeddingModelId,
                inspection.EmbeddingModelHash,
                inspection.Embedding,
                inspection.ObservedAtUtc),
            cancellationToken);
    }

    /// <summary>
    /// Writes an occurrence, detector observation, crop and embedding in one transaction.
    /// Existing natural keys are resolved to their persisted occurrence and crop identities.
    /// </summary>
    public async Task<CatalogueFaceInspection> SaveInspectionAsync(
        CatalogueFaceOccurrence occurrence,
        CatalogueFaceObservation observation,
        CatalogueFaceCrop crop,
        CatalogueFaceEmbedding embedding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(crop);
        ArgumentNullException.ThrowIfNull(embedding);

        if (observation.FaceOccurrenceId != occurrence.Id)
        {
            throw new ArgumentException("The observation must belong to the supplied occurrence.", nameof(observation));
        }

        if (crop.FaceOccurrenceId != occurrence.Id)
        {
            throw new ArgumentException("The crop must belong to the supplied occurrence.", nameof(crop));
        }

        if (embedding.FaceCropId != crop.Id)
        {
            throw new ArgumentException("The embedding must belong to the supplied crop.", nameof(embedding));
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await InsertOccurrenceAsync(connection, transaction, occurrence, cancellationToken);
        CatalogueFaceOccurrence persistedOccurrence = await FindOccurrenceAsync(
            connection,
            transaction,
            occurrence.AssetRevisionId,
            occurrence.Ordinal,
            cancellationToken)
            ?? throw new InvalidOperationException("The face occurrence was not available after it was persisted.");

        CatalogueFaceObservation remappedObservation = new(
            persistedOccurrence.Id,
            observation.DetectorModelId,
            observation.DetectorModelHash,
            observation.Confidence,
            observation.BoundingBox,
            observation.Landmarks,
            observation.ObservedAtUtc);
        await UpsertObservationAsync(connection, transaction, remappedObservation, cancellationToken);

        CatalogueFaceCrop remappedCrop = new(
            crop.Id,
            persistedOccurrence.Id,
            crop.Protocol,
            crop.ContentHash,
            crop.StoragePath,
            crop.Width,
            crop.Height,
            crop.CreatedAtUtc);
        await UpsertCropAsync(connection, transaction, remappedCrop, cancellationToken);
        CatalogueFaceCrop persistedCrop = await FindCropAsync(
            connection,
            transaction,
            persistedOccurrence.Id,
            crop.Protocol,
            crop.ContentHash,
            cancellationToken)
            ?? throw new InvalidOperationException("The face crop was not available after it was persisted.");

        CatalogueFaceEmbedding remappedEmbedding = new(
            persistedCrop.Id,
            embedding.ModelId,
            embedding.ModelHash,
            embedding.Vector,
            embedding.CreatedAtUtc);
        await InsertEmbeddingAsync(connection, transaction, remappedEmbedding, cancellationToken);

        CatalogueFaceObservation persistedObservation = await GetObservationAsync(
            connection,
            transaction,
            persistedOccurrence.Id,
            observation.DetectorModelId,
            observation.DetectorModelHash,
            cancellationToken)
            ?? throw new InvalidOperationException("The detector observation was not available after it was persisted.");
        CatalogueFaceEmbedding persistedEmbedding = await GetEmbeddingAsync(
            connection,
            transaction,
            persistedCrop.Id,
            embedding.ModelId,
            embedding.ModelHash,
            cancellationToken)
            ?? throw new InvalidOperationException("The embedding was not available after it was persisted.");

        transaction.Commit();
        return new CatalogueFaceInspection(
            persistedOccurrence,
            persistedObservation,
            persistedCrop,
            persistedEmbedding);
    }

    public async Task<IReadOnlyList<CatalogueFaceOccurrence>> GetOccurrencesAsync(
        AssetRevisionId assetRevisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, asset_revision_id, ordinal, created_at_utc
            FROM face_occurrences
            WHERE asset_revision_id = $asset_revision_id
            ORDER BY ordinal, id;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());

        List<CatalogueFaceOccurrence> occurrences = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            occurrences.Add(ReadOccurrence(reader));
        }

        return occurrences;
    }

    public async Task<CatalogueFaceObservation?> GetObservationAsync(
        FaceOccurrenceId faceOccurrenceId,
        ModelId detectorModelId,
        Sha256Digest detectorModelHash,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await GetObservationAsync(
            connection,
            transaction: null,
            faceOccurrenceId,
            detectorModelId,
            detectorModelHash,
            cancellationToken);
    }

    public async Task<CatalogueFaceCrop?> FindCropAsync(
        FaceOccurrenceId faceOccurrenceId,
        AlignmentProtocolId protocol,
        Sha256Digest contentHash,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await FindCropAsync(
            connection,
            transaction: null,
            faceOccurrenceId,
            protocol,
            contentHash,
            cancellationToken);
    }

    public async Task<CatalogueFaceEmbedding?> GetEmbeddingAsync(
        FaceCropId faceCropId,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await GetEmbeddingAsync(
            connection,
            transaction: null,
            faceCropId,
            modelId,
            modelHash,
            cancellationToken);
    }

    private static async Task InsertOccurrenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogueFaceOccurrence occurrence,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($id, $asset_revision_id, $ordinal, $created_at_utc)
            ON CONFLICT(asset_revision_id, ordinal) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", occurrence.Id.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", occurrence.AssetRevisionId.ToString());
        command.Parameters.AddWithValue("$ordinal", occurrence.Ordinal);
        command.Parameters.AddWithValue("$created_at_utc", Format(occurrence.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CatalogueFaceOccurrence?> FindOccurrenceAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        AssetRevisionId assetRevisionId,
        int ordinal,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, asset_revision_id, ordinal, created_at_utc
            FROM face_occurrences
            WHERE asset_revision_id = $asset_revision_id AND ordinal = $ordinal;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", assetRevisionId.ToString());
        command.Parameters.AddWithValue("$ordinal", ordinal);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOccurrence(reader) : null;
    }

    private static async Task UpsertObservationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogueFaceObservation observation,
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
                observed_at_utc)
                VALUES (
                    $face_occurrence_id,
                    $detector_model_id,
                    $detector_model_hash,
                    $confidence,
                    $bounding_box_json,
                    $landmarks_json,
                    $observed_at_utc)
            ON CONFLICT(face_occurrence_id, detector_model_id, detector_model_hash) DO UPDATE SET
                confidence = excluded.confidence,
                bounding_box_json = excluded.bounding_box_json,
                landmarks_json = excluded.landmarks_json,
                observed_at_utc = excluded.observed_at_utc;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", observation.FaceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$detector_model_id", observation.DetectorModelId.ToString());
        command.Parameters.AddWithValue("$detector_model_hash", observation.DetectorModelHash.ToString());
        command.Parameters.AddWithValue("$confidence", observation.Confidence);
        command.Parameters.AddWithValue("$bounding_box_json", SerializeBoundingBox(observation.BoundingBox));
        command.Parameters.AddWithValue("$landmarks_json", SerializeLandmarks(observation.Landmarks));
        command.Parameters.AddWithValue("$observed_at_utc", Format(observation.ObservedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CatalogueFaceObservation?> GetObservationAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        FaceOccurrenceId faceOccurrenceId,
        ModelId detectorModelId,
        Sha256Digest detectorModelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                face_occurrence_id,
                detector_model_id,
                detector_model_hash,
                confidence,
                bounding_box_json,
                landmarks_json,
                observed_at_utc
            FROM face_observations
            WHERE face_occurrence_id = $face_occurrence_id
              AND detector_model_id = $detector_model_id
              AND detector_model_hash = $detector_model_hash;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$detector_model_id", detectorModelId.ToString());
        command.Parameters.AddWithValue("$detector_model_hash", detectorModelHash.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadObservation(reader) : null;
    }

    private static async Task UpsertCropAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogueFaceCrop crop,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO face_crops (
                id,
                face_occurrence_id,
                crop_protocol,
                content_sha256,
                storage_path,
                width,
                height,
                created_at_utc)
                VALUES (
                    $id,
                    $face_occurrence_id,
                    $crop_protocol,
                    $content_sha256,
                    $storage_path,
                    $width,
                    $height,
                    $created_at_utc)
            ON CONFLICT(face_occurrence_id, crop_protocol, content_sha256) DO UPDATE SET
                storage_path = excluded.storage_path,
                width = excluded.width,
                height = excluded.height;
            """;
        command.Parameters.AddWithValue("$id", crop.Id.ToString());
        command.Parameters.AddWithValue("$face_occurrence_id", crop.FaceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$crop_protocol", crop.Protocol.ToString());
        command.Parameters.AddWithValue("$content_sha256", crop.ContentHash.ToString());
        command.Parameters.AddWithValue("$storage_path", crop.StoragePath);
        command.Parameters.AddWithValue("$width", crop.Width);
        command.Parameters.AddWithValue("$height", crop.Height);
        command.Parameters.AddWithValue("$created_at_utc", Format(crop.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CatalogueFaceCrop?> FindCropAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        FaceOccurrenceId faceOccurrenceId,
        AlignmentProtocolId protocol,
        Sha256Digest contentHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                id,
                face_occurrence_id,
                crop_protocol,
                content_sha256,
                storage_path,
                width,
                height,
                created_at_utc
            FROM face_crops
            WHERE face_occurrence_id = $face_occurrence_id
              AND crop_protocol = $crop_protocol
              AND content_sha256 = $content_sha256;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$crop_protocol", protocol.ToString());
        command.Parameters.AddWithValue("$content_sha256", contentHash.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCrop(reader) : null;
    }

    private static async Task InsertEmbeddingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogueFaceEmbedding embedding,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO embeddings (
                face_crop_id,
                model_id,
                model_hash,
                dimensions,
                l2_norm,
                vector_blob,
                created_at_utc)
                VALUES (
                    $face_crop_id,
                    $model_id,
                    $model_hash,
                    $dimensions,
                    $l2_norm,
                    $vector_blob,
                    $created_at_utc)
            ON CONFLICT(face_crop_id, model_id, model_hash) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$face_crop_id", embedding.FaceCropId.ToString());
        command.Parameters.AddWithValue("$model_id", embedding.ModelId.ToString());
        command.Parameters.AddWithValue("$model_hash", embedding.ModelHash.ToString());
        command.Parameters.AddWithValue("$dimensions", embedding.Vector.Dimensions);
        command.Parameters.AddWithValue("$l2_norm", embedding.Vector.L2Norm);
        command.Parameters.AddWithValue("$vector_blob", SerializeVector(embedding.Vector));
        command.Parameters.AddWithValue("$created_at_utc", Format(embedding.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CatalogueFaceEmbedding?> GetEmbeddingAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        FaceCropId faceCropId,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                face_crop_id,
                model_id,
                model_hash,
                dimensions,
                l2_norm,
                vector_blob,
                created_at_utc
            FROM embeddings
            WHERE face_crop_id = $face_crop_id
              AND model_id = $model_id
              AND model_hash = $model_hash;
            """;
        command.Parameters.AddWithValue("$face_crop_id", faceCropId.ToString());
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadEmbedding(reader) : null;
    }

    private static CatalogueFaceOccurrence ReadOccurrence(SqliteDataReader reader) =>
        new(
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
            AssetRevisionId.From(Guid.Parse(reader.GetString(1))),
            reader.GetInt32(2),
            ParseTimestamp(reader.GetString(3)));

    private static CatalogueFaceObservation ReadObservation(SqliteDataReader reader) =>
        new(
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
            new ModelId(reader.GetString(1)),
            new Sha256Digest(reader.GetString(2)),
            reader.GetDouble(3),
            DeserializeBoundingBox(reader.GetString(4)),
            DeserializeLandmarks(reader.GetString(5)),
            ParseTimestamp(reader.GetString(6)));

    private static CatalogueFaceCrop ReadCrop(SqliteDataReader reader) =>
        new(
            FaceCropId.From(Guid.Parse(reader.GetString(0))),
            FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))),
            new AlignmentProtocolId(reader.GetString(2)),
            new Sha256Digest(reader.GetString(3)),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            ParseTimestamp(reader.GetString(7)));

    private static CatalogueFaceEmbedding ReadEmbedding(SqliteDataReader reader)
    {
        int dimensions = reader.GetInt32(3);
        double storedNorm = reader.GetDouble(4);
        byte[] blob = (byte[])reader.GetValue(5);
        EmbeddingVector vector = DeserializeVector(blob, dimensions);

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

    private static string SerializeBoundingBox(NormalizedBoundingBox boundingBox) =>
        JsonSerializer.Serialize(new[]
        {
            boundingBox.X,
            boundingBox.Y,
            boundingBox.Width,
            boundingBox.Height,
        });

    private static NormalizedBoundingBox DeserializeBoundingBox(string value)
    {
        double[] coordinates = JsonSerializer.Deserialize<double[]>(value)
            ?? throw new DataException("Bounding-box JSON was null.");
        if (coordinates.Length != 4)
        {
            throw new DataException("Bounding-box JSON must contain four coordinates.");
        }

        return new NormalizedBoundingBox(
            coordinates[0],
            coordinates[1],
            coordinates[2],
            coordinates[3]);
    }

    private static string SerializeLandmarks(NormalizedFaceLandmarks landmarks) =>
        JsonSerializer.Serialize(new[]
        {
            new[] { landmarks.LeftEye.X, landmarks.LeftEye.Y },
            new[] { landmarks.RightEye.X, landmarks.RightEye.Y },
            new[] { landmarks.Nose.X, landmarks.Nose.Y },
            new[] { landmarks.MouthLeft.X, landmarks.MouthLeft.Y },
            new[] { landmarks.MouthRight.X, landmarks.MouthRight.Y },
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
}
