using System.Buffers.Binary;
using System.Data;
using System.Text.Json;
using Npgsql;
using PhotoIdentity.Core.Clustering;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Reads a bounded, exact-model reviewed sample for local clustering and suggestion-quality evaluation.
/// No source paths, person names, crops, or other presentation metadata leave this repository.
/// </summary>
public sealed class PostgresProvisionalClusterEvaluationRepository : IProvisionalClusterEvaluationRepository
{
    private const int MaximumExportFaces = 20000;
    private readonly PostgresCatalogueDatabase _database;

    public PostgresProvisionalClusterEvaluationRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<ProvisionalClusterEvaluationFace>> ReadReviewedSampleAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        int maximumFaces = 5000,
        bool includeUnknown = true,
        CancellationToken cancellationToken = default)
    {
        if (maximumFaces is < 1 or > MaximumExportFaces)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFaces),
                $"Evaluation sample size must be between 1 and {MaximumExportFaces}.");
        }

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_action AS (
                SELECT
                    review_actions.face_occurrence_id,
                    review_actions.action_kind,
                    review_actions.person_id,
                    review_actions.created_at_utc,
                    ROW_NUMBER() OVER (
                        PARTITION BY review_actions.face_occurrence_id
                        ORDER BY review_actions.id DESC) AS row_number
                FROM review_actions
                WHERE review_actions.action_kind IN ('assign', 'unknown', 'reject')
                  AND review_actions.reversed_at_utc IS NULL
            ),
            matching_embeddings AS (
                SELECT
                    face_crops.face_occurrence_id,
                    embeddings.dimensions,
                    embeddings.l2_norm,
                    embeddings.vector_blob,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_crops.face_occurrence_id
                        ORDER BY embeddings.created_at_utc DESC, embeddings.id DESC) AS row_number
                FROM face_crops
                INNER JOIN embeddings
                    ON embeddings.face_crop_id = face_crops.id
                WHERE embeddings.model_id = @model_id
                  AND embeddings.model_hash = @model_hash
            ),
            latest_observation AS (
                SELECT
                    face_observations.face_occurrence_id,
                    face_observations.confidence,
                    face_observations.bounding_box_json,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_observations.face_occurrence_id
                        ORDER BY
                            face_observations.observed_at_utc DESC,
                            face_observations.detector_model_id,
                            face_observations.detector_model_hash) AS row_number
                FROM face_observations
            )
            SELECT
                face_occurrences.id,
                face_occurrences.asset_revision_id,
                asset_revisions.content_sha256,
                latest_action.action_kind,
                COALESCE(reviewed_person.merged_into_person_id, latest_action.person_id) AS canonical_person_id,
                matching_embeddings.dimensions,
                matching_embeddings.l2_norm,
                matching_embeddings.vector_blob,
                latest_observation.confidence,
                latest_observation.bounding_box_json,
                asset_revisions.width,
                asset_revisions.height,
                latest_action.created_at_utc,
                (reviewed_person.merged_into_person_id IS NOT NULL) AS reviewed_person_was_merged
            FROM matching_embeddings
            INNER JOIN face_occurrences
                ON face_occurrences.id = matching_embeddings.face_occurrence_id
            INNER JOIN asset_revisions
                ON asset_revisions.id = face_occurrences.asset_revision_id
            INNER JOIN latest_action
                ON latest_action.face_occurrence_id = face_occurrences.id
               AND latest_action.row_number = 1
            LEFT JOIN people AS reviewed_person
                ON reviewed_person.id = latest_action.person_id
            LEFT JOIN latest_observation
                ON latest_observation.face_occurrence_id = face_occurrences.id
               AND latest_observation.row_number = 1
            WHERE matching_embeddings.row_number = 1
              AND (
                    latest_action.action_kind = 'assign'
                    OR (@include_unknown AND latest_action.action_kind = 'unknown'))
            ORDER BY face_occurrences.id
            LIMIT @maximum_faces;
            """;
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
        command.Parameters.AddWithValue("include_unknown", includeUnknown);
        command.Parameters.AddWithValue("maximum_faces", maximumFaces);

        List<ProvisionalClusterEvaluationFace> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string reviewState = reader.GetString(3) switch
            {
                CatalogueReviewActionKinds.Assign => CatalogueReviewStates.Assigned,
                CatalogueReviewActionKinds.Unknown => CatalogueReviewStates.Unknown,
                string unexpected => throw new DataException(
                    $"Unexpected review state '{unexpected}' in cluster-evaluation sample."),
            };
            PersonId? personId = reader.IsDBNull(4)
                ? null
                : PersonId.From(reader.GetGuid(4));
            if (reviewState == CatalogueReviewStates.Assigned && personId is null)
            {
                throw new DataException("Assigned evaluation face is missing its canonical person identifier.");
            }

            double? detectorConfidence = reader.IsDBNull(8) ? null : reader.GetDouble(8);
            string? boundingBoxJson = reader.IsDBNull(9) ? null : reader.GetString(9);
            int? photoWidth = reader.IsDBNull(10) ? null : reader.GetInt32(10);
            int? photoHeight = reader.IsDBNull(11) ? null : reader.GetInt32(11);
            DateTimeOffset reviewedAtUtc = reader.GetFieldValue<DateTimeOffset>(12);

            result.Add(new ProvisionalClusterEvaluationFace(
                FaceOccurrenceId.From(reader.GetGuid(0)),
                AssetRevisionId.From(reader.GetGuid(1)),
                new Sha256Digest(reader.GetString(2)),
                reviewState,
                personId,
                ReadVector(reader, 5, 6, 7),
                detectorConfidence,
                TryReadFaceAreaFraction(boundingBoxJson, photoWidth, photoHeight),
                reviewedAtUtc,
                reader.GetBoolean(13)));
        }

        return result;
    }

    private static double? TryReadFaceAreaFraction(
        string? boundingBoxJson,
        int? photoWidth,
        int? photoHeight)
    {
        if (string.IsNullOrWhiteSpace(boundingBoxJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(boundingBoxJson);
            double[] coordinates;
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                JsonElement[] elements = document.RootElement.EnumerateArray().ToArray();
                if (elements.Length != 4 || elements.Any(element => element.ValueKind != JsonValueKind.Number))
                {
                    return null;
                }

                coordinates = elements.Select(element => element.GetDouble()).ToArray();
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object
                && TryGetNumber(document.RootElement, "x", out double x)
                && TryGetNumber(document.RootElement, "y", out double y)
                && TryGetNumber(document.RootElement, "width", out double width)
                && TryGetNumber(document.RootElement, "height", out double height))
            {
                coordinates = [x, y, width, height];
            }
            else
            {
                return null;
            }

            double widthValue = coordinates[2];
            double heightValue = coordinates[3];
            if (!double.IsFinite(widthValue) || !double.IsFinite(heightValue)
                || widthValue <= 0d || heightValue <= 0d)
            {
                return null;
            }

            bool normalized =
                coordinates[0] >= 0d && coordinates[1] >= 0d
                && widthValue <= 1d && heightValue <= 1d
                && coordinates[0] <= 1d && coordinates[1] <= 1d
                && coordinates[0] + widthValue <= 1d
                && coordinates[1] + heightValue <= 1d;
            if (!normalized)
            {
                if (photoWidth is not > 0 || photoHeight is not > 0)
                {
                    return null;
                }

                widthValue /= photoWidth.Value;
                heightValue /= photoHeight.Value;
            }

            double area = widthValue * heightValue;
            return double.IsFinite(area) && area is > 0d and <= 1d ? area : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryGetNumber(JsonElement element, string propertyName, out double value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value);
    }

    private static EmbeddingVector ReadVector(
        NpgsqlDataReader reader,
        int dimensionsOrdinal,
        int normOrdinal,
        int blobOrdinal)
    {
        int dimensions = reader.GetInt32(dimensionsOrdinal);
        double storedNorm = reader.GetDouble(normOrdinal);
        byte[] bytes = reader.GetFieldValue<byte[]>(blobOrdinal);
        if (dimensions <= 0 || bytes.Length != checked(dimensions * sizeof(float)))
        {
            throw new DataException("The stored embedding dimensions do not match its vector data.");
        }

        float[] values = new float[dimensions];
        for (int index = 0; index < values.Length; index++)
        {
            int bits = BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(index * sizeof(float), sizeof(float)));
            values[index] = BitConverter.Int32BitsToSingle(bits);
        }

        EmbeddingVector vector = new(values);
        double tolerance = 1e-9 * Math.Max(1, storedNorm);
        if (Math.Abs(vector.L2Norm - storedNorm) > tolerance)
        {
            throw new DataException("The stored embedding norm does not match its vector data.");
        }

        return vector;
    }
}
