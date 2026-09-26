using System.Buffers.Binary;
using System.Data;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// PostgreSQL-backed face seeding/query facade for integration tests that still use the older
/// concrete fixture shape.
/// </summary>
public sealed class PostgresFaceCatalogueCompatibilityRepository : IFaceInspectionRepository
{
    private readonly PostgresTestCatalogueDatabase _database;
    private readonly PostgresFaceInspectionRepository _writer;

    public PostgresFaceCatalogueCompatibilityRepository(PostgresTestCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _writer = new PostgresFaceInspectionRepository(database.Database);
    }

    public Task SaveInspectionAsync(FaceInspectionWrite inspection, CancellationToken cancellationToken = default) =>
        _writer.SaveInspectionAsync(inspection, cancellationToken);

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

        await _writer.SaveInspectionAsync(
            new FaceInspectionWrite(
                occurrence.Id,
                occurrence.AssetRevisionId,
                occurrence.Ordinal,
                observation.ObservedAtUtc,
                observation.DetectorModelId,
                observation.DetectorModelHash,
                observation.Confidence,
                observation.BoundingBox,
                observation.Landmarks,
                crop.Id,
                crop.Protocol,
                crop.ContentHash,
                crop.StoragePath,
                crop.Width,
                crop.Height,
                embedding.ModelId,
                embedding.ModelHash,
                embedding.Vector),
            cancellationToken);

        CatalogueFaceOccurrence persistedOccurrence = AssertSingle(
            await GetOccurrencesAsync(occurrence.AssetRevisionId, cancellationToken),
            candidate => candidate.Ordinal == occurrence.Ordinal,
            "face occurrence");
        CatalogueFaceObservation persistedObservation =
            await GetObservationAsync(
                persistedOccurrence.Id,
                observation.DetectorModelId,
                observation.DetectorModelHash,
                cancellationToken)
            ?? throw new InvalidOperationException("The detector observation was not available after it was persisted.");
        CatalogueFaceCrop persistedCrop =
            await FindCropAsync(
                persistedOccurrence.Id,
                crop.Protocol,
                crop.ContentHash,
                cancellationToken)
            ?? throw new InvalidOperationException("The face crop was not available after it was persisted.");
        CatalogueFaceEmbedding persistedEmbedding =
            await GetEmbeddingAsync(
                persistedCrop.Id,
                embedding.ModelId,
                embedding.ModelHash,
                cancellationToken)
            ?? throw new InvalidOperationException("The embedding was not available after it was persisted.");
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
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, asset_revision_id, ordinal, created_at_utc
            FROM face_occurrences
            WHERE asset_revision_id = @asset_revision_id
            ORDER BY ordinal, id;
            """;
        command.Parameters.AddWithValue("asset_revision_id", Guid.Parse(assetRevisionId.ToString()));
        List<CatalogueFaceOccurrence> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CatalogueFaceOccurrence(
                FaceOccurrenceId.From(reader.GetGuid(0)),
                AssetRevisionId.From(reader.GetGuid(1)),
                reader.GetInt32(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }
        return result;
    }

    public async Task<CatalogueFaceObservation?> GetObservationAsync(
        FaceOccurrenceId faceOccurrenceId,
        ModelId detectorModelId,
        Sha256Digest detectorModelHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT face_occurrence_id, detector_model_id, detector_model_hash, confidence,
                   bounding_box_json::text, landmarks_json::text, observed_at_utc
            FROM face_observations
            WHERE face_occurrence_id = @face_occurrence_id
              AND detector_model_id = @detector_model_id
              AND detector_model_hash = @detector_model_hash;
            """;
        command.Parameters.AddWithValue("face_occurrence_id", Guid.Parse(faceOccurrenceId.ToString()));
        command.Parameters.AddWithValue("detector_model_id", detectorModelId.ToString());
        command.Parameters.AddWithValue("detector_model_hash", detectorModelHash.ToString());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new CatalogueFaceObservation(
            FaceOccurrenceId.From(reader.GetGuid(0)),
            new ModelId(reader.GetString(1)),
            new Sha256Digest(reader.GetString(2)),
            reader.GetDouble(3),
            DeserializeBoundingBox(reader.GetString(4)),
            DeserializeLandmarks(reader.GetString(5)),
            reader.GetFieldValue<DateTimeOffset>(6));
    }

    public async Task<CatalogueFaceCrop?> FindCropAsync(
        FaceOccurrenceId faceOccurrenceId,
        AlignmentProtocolId protocol,
        Sha256Digest contentHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, face_occurrence_id, crop_protocol, content_sha256, storage_path,
                   width, height, created_at_utc
            FROM face_crops
            WHERE face_occurrence_id = @face_occurrence_id
              AND crop_protocol = @crop_protocol
              AND content_sha256 = @content_sha256;
            """;
        command.Parameters.AddWithValue("face_occurrence_id", Guid.Parse(faceOccurrenceId.ToString()));
        command.Parameters.AddWithValue("crop_protocol", protocol.ToString());
        command.Parameters.AddWithValue("content_sha256", contentHash.ToString());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new CatalogueFaceCrop(
            FaceCropId.From(reader.GetGuid(0)),
            FaceOccurrenceId.From(reader.GetGuid(1)),
            new AlignmentProtocolId(reader.GetString(2)),
            new Sha256Digest(reader.GetString(3)),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetFieldValue<DateTimeOffset>(7));
    }

    public async Task<CatalogueFaceEmbedding?> GetEmbeddingAsync(
        FaceCropId faceCropId,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT face_crop_id, model_id, model_hash, dimensions, l2_norm, vector_blob, created_at_utc
            FROM embeddings
            WHERE face_crop_id = @face_crop_id
              AND model_id = @model_id
              AND model_hash = @model_hash;
            """;
        command.Parameters.AddWithValue("face_crop_id", Guid.Parse(faceCropId.ToString()));
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
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
            FaceCropId.From(reader.GetGuid(0)),
            new ModelId(reader.GetString(1)),
            new Sha256Digest(reader.GetString(2)),
            vector,
            reader.GetFieldValue<DateTimeOffset>(6));
    }

    private static T AssertSingle<T>(IEnumerable<T> values, Func<T, bool> predicate, string description)
    {
        T[] matches = values.Where(predicate).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException($"Expected one persisted {description}, found {matches.Length}.");
    }

    private static NormalizedBoundingBox DeserializeBoundingBox(string value)
    {
        double[] coordinates = JsonSerializer.Deserialize<double[]>(value)
            ?? throw new DataException("Bounding-box JSON was null.");
        return coordinates.Length == 4
            ? new NormalizedBoundingBox(coordinates[0], coordinates[1], coordinates[2], coordinates[3])
            : throw new DataException("Bounding-box JSON must contain four coordinates.");
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
}
