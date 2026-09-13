using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Performs a bounded exact cosine scan for discovery without changing canonical review state.
/// </summary>
public sealed class PostgresSimilarFaceRepository : ISimilarFaceRepository
{
    private const int MaximumResults = 200;

    private const string ReviewFaceCtes = """
        WITH latest_action AS (
            SELECT
                review_actions.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY id DESC) AS row_number
            FROM review_actions
            WHERE action_kind IN ('assign', 'unknown', 'reject')
              AND reversed_at_utc IS NULL
        ),
        latest_crop AS (
            SELECT
                face_crops.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY created_at_utc DESC, id DESC) AS row_number
            FROM face_crops
        ),
        latest_observation AS (
            SELECT
                face_observations.*,
                ROW_NUMBER() OVER (
                    PARTITION BY face_occurrence_id
                    ORDER BY observed_at_utc DESC, detector_model_id, detector_model_hash) AS row_number
            FROM face_observations
        )
        """;

    private const string ReviewFaceColumns = """
        face_occurrences.id,
        face_occurrences.ordinal,
        face_occurrences.created_at_utc,
        assets.source_key,
        COALESCE(asset_revisions.media_type, 'application/octet-stream'),
        asset_revisions.width,
        asset_revisions.height,
        asset_revisions.content_sha256,
        latest_crop.storage_path,
        latest_observation.confidence,
        latest_action.id,
        latest_action.action_kind,
        latest_action.person_id,
        people.display_name,
        latest_observation.bounding_box_json,
        face_occurrences.asset_revision_id
        """;

    private const string ReviewFaceFrom = """
        FROM face_occurrences
        INNER JOIN asset_revisions
            ON asset_revisions.id = face_occurrences.asset_revision_id
        INNER JOIN assets
            ON assets.id = asset_revisions.asset_id
        LEFT JOIN latest_crop
            ON latest_crop.face_occurrence_id = face_occurrences.id
           AND latest_crop.row_number = 1
        LEFT JOIN latest_observation
            ON latest_observation.face_occurrence_id = face_occurrences.id
           AND latest_observation.row_number = 1
        LEFT JOIN latest_action
            ON latest_action.face_occurrence_id = face_occurrences.id
           AND latest_action.row_number = 1
        LEFT JOIN people
            ON people.id = latest_action.person_id
        """;

    private readonly PostgresCatalogueDatabase _database;

    public PostgresSimilarFaceRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueSimilarFaceQueryResult?> FindSimilarAsync(
        FaceOccurrenceId sourceFaceId,
        ModelId modelId,
        Sha256Digest modelHash,
        bool includeUnknown = false,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaximumResults)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"Similar-face result count must be between 1 and {MaximumResults}.");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);

        EmbeddingVector? sourceVector = await ReadSourceEmbeddingAsync(
            connection,
            sourceFaceId,
            modelId,
            modelHash,
            cancellationToken);
        if (sourceVector is null)
        {
            return null;
        }

        List<CandidateScore> candidates = await ReadCandidateScoresAsync(
            connection,
            sourceFaceId,
            sourceVector,
            modelId,
            modelHash,
            includeUnknown,
            cancellationToken);

        CandidateScore[] top = candidates
            .OrderByDescending(candidate => candidate.Similarity)
            .ThenBy(candidate => candidate.FaceOccurrenceId.ToString(), StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        IReadOnlyDictionary<FaceOccurrenceId, CatalogueReviewFace> faces =
            await ReadFacesAsync(connection, top.Select(candidate => candidate.FaceOccurrenceId).ToArray(), cancellationToken);

        List<CatalogueSimilarFace> items = [];
        foreach (CandidateScore candidate in top)
        {
            if (faces.TryGetValue(candidate.FaceOccurrenceId, out CatalogueReviewFace? face))
            {
                items.Add(new CatalogueSimilarFace(face, candidate.Similarity));
            }
        }

        stopwatch.Stop();
        return new CatalogueSimilarFaceQueryResult(
            sourceFaceId,
            modelId,
            modelHash,
            includeUnknown,
            candidates.Count,
            stopwatch.ElapsedMilliseconds,
            items);
    }

    private static async Task<EmbeddingVector?> ReadSourceEmbeddingAsync(
        NpgsqlConnection connection,
        FaceOccurrenceId sourceFaceId,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_action AS (
                SELECT
                    face_occurrence_id,
                    action_kind,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_occurrence_id
                        ORDER BY id DESC) AS row_number
                FROM review_actions
                WHERE action_kind IN ('assign', 'unknown', 'reject')
                  AND reversed_at_utc IS NULL
            ),
            matching_embeddings AS (
                SELECT
                    crop.face_occurrence_id,
                    embedding.dimensions,
                    embedding.l2_norm,
                    embedding.vector_blob,
                    ROW_NUMBER() OVER (
                        PARTITION BY crop.face_occurrence_id
                        ORDER BY embedding.created_at_utc DESC, embedding.id DESC) AS row_number
                FROM face_crops AS crop
                INNER JOIN embeddings AS embedding
                    ON embedding.face_crop_id = crop.id
                WHERE crop.face_occurrence_id = @source_face_id
                  AND embedding.model_id = @model_id
                  AND embedding.model_hash = @model_hash
            )
            SELECT
                matching.dimensions,
                matching.l2_norm,
                matching.vector_blob
            FROM matching_embeddings AS matching
            LEFT JOIN latest_action AS review
                ON review.face_occurrence_id = matching.face_occurrence_id
               AND review.row_number = 1
            WHERE matching.row_number = 1
              AND (review.action_kind IS NULL OR review.action_kind <> 'reject');
            """;
        AddModelParameters(command, sourceFaceId, modelId, modelHash);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadVector(reader, 0, 1, 2)
            : null;
    }

    private static async Task<List<CandidateScore>> ReadCandidateScoresAsync(
        NpgsqlConnection connection,
        FaceOccurrenceId sourceFaceId,
        EmbeddingVector sourceVector,
        ModelId modelId,
        Sha256Digest modelHash,
        bool includeUnknown,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_action AS (
                SELECT
                    face_occurrence_id,
                    action_kind,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_occurrence_id
                        ORDER BY id DESC) AS row_number
                FROM review_actions
                WHERE action_kind IN ('assign', 'unknown', 'reject')
                  AND reversed_at_utc IS NULL
            ),
            matching_embeddings AS (
                SELECT
                    crop.face_occurrence_id,
                    embedding.dimensions,
                    embedding.l2_norm,
                    embedding.vector_blob,
                    ROW_NUMBER() OVER (
                        PARTITION BY crop.face_occurrence_id
                        ORDER BY embedding.created_at_utc DESC, embedding.id DESC) AS row_number
                FROM face_crops AS crop
                INNER JOIN embeddings AS embedding
                    ON embedding.face_crop_id = crop.id
                WHERE embedding.model_id = @model_id
                  AND embedding.model_hash = @model_hash
            )
            SELECT
                matching.face_occurrence_id,
                matching.dimensions,
                matching.l2_norm,
                matching.vector_blob
            FROM matching_embeddings AS matching
            LEFT JOIN latest_action AS review
                ON review.face_occurrence_id = matching.face_occurrence_id
               AND review.row_number = 1
            WHERE matching.row_number = 1
              AND matching.face_occurrence_id <> @source_face_id
              AND (
                    review.face_occurrence_id IS NULL
                    OR (@include_unknown AND review.action_kind = 'unknown'))
              AND NOT EXISTS (
                    SELECT 1
                    FROM person_labels AS label
                    WHERE label.face_occurrence_id = matching.face_occurrence_id
                      AND label.label_kind = 'confirmed'
                      AND NOT EXISTS (
                            SELECT 1
                            FROM review_actions AS action
                            WHERE action.face_occurrence_id = label.face_occurrence_id));
            """;
        AddModelParameters(command, sourceFaceId, modelId, modelHash);
        command.Parameters.AddWithValue("include_unknown", includeUnknown);

        List<CandidateScore> candidates = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            FaceOccurrenceId faceOccurrenceId = FaceOccurrenceId.From(reader.GetGuid(0));
            EmbeddingVector candidateVector = ReadVector(reader, 1, 2, 3);
            double similarity;
            try
            {
                similarity = sourceVector.CosineSimilarity(candidateVector);
            }
            catch (ArgumentException exception)
            {
                throw new DataException(
                    "Embeddings from one exact model revision have inconsistent dimensions.",
                    exception);
            }

            if (!double.IsFinite(similarity))
            {
                throw new DataException("Cosine similarity produced a non-finite score.");
            }

            candidates.Add(new CandidateScore(faceOccurrenceId, similarity));
        }

        return candidates;
    }

    private static async Task<IReadOnlyDictionary<FaceOccurrenceId, CatalogueReviewFace>> ReadFacesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<FaceOccurrenceId> faceOccurrenceIds,
        CancellationToken cancellationToken)
    {
        if (faceOccurrenceIds.Count == 0)
        {
            return new Dictionary<FaceOccurrenceId, CatalogueReviewFace>();
        }

        Guid[] ids = faceOccurrenceIds
            .Select(id => Guid.Parse(id.ToString()))
            .ToArray();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            {ReviewFaceCtes}
            SELECT {ReviewFaceColumns}
            {ReviewFaceFrom}
            WHERE face_occurrences.id = ANY(@face_ids);
            """;
        command.Parameters.AddWithValue("face_ids", ids);

        Dictionary<FaceOccurrenceId, CatalogueReviewFace> faces = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            CatalogueReviewFace face = ReadFace(reader);
            faces[face.Id] = face;
        }

        return faces;
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
        for (int index = 0; index < dimensions; index++)
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

    private static CatalogueReviewFace ReadFace(NpgsqlDataReader reader)
    {
        string sourceKey = reader.GetString(3).Replace('\\', '/');
        string photoName = Path.GetFileName(sourceKey);
        long? activeActionId = reader.IsDBNull(10) ? null : reader.GetInt64(10);
        string? actionKind = reader.IsDBNull(11) ? null : reader.GetString(11);
        PersonId? personId = reader.IsDBNull(12)
            ? null
            : PersonId.From(reader.GetGuid(12));
        string? personName = reader.IsDBNull(13) ? null : reader.GetString(13);
        CatalogueReviewPerson? person = personId is PersonId id && personName is not null
            ? new CatalogueReviewPerson(id, personName)
            : null;
        string reviewState = actionKind switch
        {
            CatalogueReviewActionKinds.Assign => CatalogueReviewStates.Assigned,
            CatalogueReviewActionKinds.Unknown => CatalogueReviewStates.Unknown,
            CatalogueReviewActionKinds.Reject => CatalogueReviewStates.Rejected,
            _ => CatalogueReviewStates.Unreviewed,
        };

        return new CatalogueReviewFace(
            FaceOccurrenceId.From(reader.GetGuid(0)),
            reader.GetInt32(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            string.IsNullOrWhiteSpace(photoName) ? "Photo" : photoName,
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            new Sha256Digest(reader.GetString(7)),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetDouble(9),
            reviewState,
            person,
            activeActionId,
            AssetRevisionId.From(reader.GetGuid(15)),
            reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    private static void AddModelParameters(
        NpgsqlCommand command,
        FaceOccurrenceId sourceFaceId,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue("source_face_id", Guid.Parse(sourceFaceId.ToString()));
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
    }

    private sealed record CandidateScore(
        FaceOccurrenceId FaceOccurrenceId,
        double Similarity);
}
