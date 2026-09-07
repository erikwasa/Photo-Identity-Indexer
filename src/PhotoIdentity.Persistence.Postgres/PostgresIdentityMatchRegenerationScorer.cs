using System.Buffers.Binary;
using System.Data;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Review;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Scores one snapshotted regeneration target per PostgreSQL transaction while preserving the
/// accepted SQLite candidate, rejection, ranking and stale-derived-suggestion semantics.
/// </summary>
public sealed class PostgresIdentityMatchRegenerationScorer :
    IIdentityMatchRegenerationScorer
{
    private const string PendingStatus = ReviewSuggestionStatuses.Pending;
    private const string RejectedStatus = ReviewSuggestionStatuses.Rejected;

    private readonly PostgresCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresIdentityMatchRegenerationScorer(
        PostgresCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<int> ScoreTargetAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        StoredEmbedding? target = await ReadEligibleTargetAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            faceOccurrenceId,
            cancellationToken);
        if (target is null)
        {
            await ClearTargetRankingsAsync(
                connection,
                transaction,
                faceOccurrenceId,
                modelId,
                modelHash,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return 0;
        }

        IReadOnlyList<Exemplar> exemplars = await ReadExemplarsAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        HashSet<RejectedPair> rejectedPairs = await ReadRejectedPairsAsync(
            connection,
            transaction,
            cancellationToken);
        Candidate[] candidates = ScoreCandidates(target, exemplars, rejectedPairs)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.PersonId.ToString(), StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        await ClearTargetRankingsAsync(
            connection,
            transaction,
            faceOccurrenceId,
            modelId,
            modelHash,
            cancellationToken);
        await ReplaceSuggestionsAndRankingsAsync(
            connection,
            transaction,
            faceOccurrenceId,
            modelId,
            modelHash,
            candidates,
            _timeProvider.GetUtcNow().ToUniversalTime(),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return candidates.Length;
    }

    public async Task RemoveObsoleteRankingsAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand deleteRankings = connection.CreateCommand())
        {
            deleteRankings.Transaction = transaction;
            deleteRankings.CommandText =
                """
                DELETE FROM identity_suggestion_rankings
                WHERE model_id = @model_id
                  AND model_hash = @model_hash
                  AND face_occurrence_id NOT IN (
                      SELECT face_occurrence_id
                      FROM identity_match_regeneration_targets
                      WHERE run_id = @run_id);
                """;
            AddModelParameters(deleteRankings, modelId, modelHash);
            deleteRankings.Parameters.AddWithValue("run_id", runId);
            await deleteRankings.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand deleteSuggestions = connection.CreateCommand())
        {
            deleteSuggestions.Transaction = transaction;
            deleteSuggestions.CommandText =
                """
                DELETE FROM identity_suggestions
                WHERE model_id = @model_id
                  AND model_hash = @model_hash
                  AND status = @pending_status
                  AND id NOT IN (
                      SELECT suggestion_id
                      FROM identity_suggestion_rankings
                      WHERE model_id = @model_id
                        AND model_hash = @model_hash);
                """;
            AddModelParameters(deleteSuggestions, modelId, modelHash);
            deleteSuggestions.Parameters.AddWithValue("pending_status", PendingStatus);
            await deleteSuggestions.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<StoredEmbedding?> ReadEligibleTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            WITH latest_review AS (
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
                WHERE crop.face_occurrence_id = @face_occurrence_id
                  AND embedding.model_id = @model_id
                  AND embedding.model_hash = @model_hash
            )
            SELECT
                matching.face_occurrence_id,
                matching.dimensions,
                matching.l2_norm,
                matching.vector_blob
            FROM matching_embeddings AS matching
            WHERE matching.row_number = 1
              AND NOT EXISTS (
                  SELECT 1
                  FROM latest_review AS review
                  WHERE review.face_occurrence_id = matching.face_occurrence_id
                    AND review.row_number = 1)
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
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));
        AddModelParameters(command, modelId, modelHash);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredEmbedding(
            FaceOccurrenceId.From(reader.GetGuid(0)),
            ReadVector(reader, 1, 2, 3));
    }

    private static async Task<IReadOnlyList<Exemplar>> ReadExemplarsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            WITH latest_review AS (
                SELECT
                    face_occurrence_id,
                    action_kind,
                    person_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_occurrence_id
                        ORDER BY id DESC) AS row_number
                FROM review_actions
                WHERE action_kind IN ('assign', 'unknown', 'reject')
                  AND reversed_at_utc IS NULL
            ),
            confirmed_faces AS (
                SELECT face_occurrence_id, person_id
                FROM latest_review
                WHERE row_number = 1
                  AND action_kind = 'assign'
                  AND person_id IS NOT NULL
                UNION
                SELECT label.face_occurrence_id, label.person_id
                FROM person_labels AS label
                WHERE label.label_kind = 'confirmed'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM review_actions AS action
                      WHERE action.face_occurrence_id = label.face_occurrence_id)
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
                confirmed.person_id,
                confirmed.face_occurrence_id,
                matching.dimensions,
                matching.l2_norm,
                matching.vector_blob
            FROM confirmed_faces AS confirmed
            INNER JOIN matching_embeddings AS matching
                ON matching.face_occurrence_id = confirmed.face_occurrence_id
               AND matching.row_number = 1
            INNER JOIN people AS person
                ON person.id = confirmed.person_id
            WHERE person.merged_into_person_id IS NULL
            ORDER BY confirmed.person_id, confirmed.face_occurrence_id;
            """;
        AddModelParameters(command, modelId, modelHash);

        List<Exemplar> exemplars = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            exemplars.Add(new Exemplar(
                PersonId.From(reader.GetGuid(0)),
                FaceOccurrenceId.From(reader.GetGuid(1)),
                ReadVector(reader, 2, 3, 4)));
        }

        return exemplars;
    }

    private static async Task<HashSet<RejectedPair>> ReadRejectedPairsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT DISTINCT face_occurrence_id, suggested_person_id
            FROM identity_suggestions
            WHERE status = @status;
            """;
        command.Parameters.AddWithValue("status", RejectedStatus);

        HashSet<RejectedPair> pairs = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pairs.Add(new RejectedPair(
                FaceOccurrenceId.From(reader.GetGuid(0)),
                PersonId.From(reader.GetGuid(1))));
        }

        return pairs;
    }

    private static IEnumerable<Candidate> ScoreCandidates(
        StoredEmbedding target,
        IReadOnlyList<Exemplar> exemplars,
        IReadOnlySet<RejectedPair> rejectedPairs)
    {
        Dictionary<PersonId, double> bestByPerson = [];
        foreach (Exemplar exemplar in exemplars)
        {
            if (rejectedPairs.Contains(new RejectedPair(target.FaceOccurrenceId, exemplar.PersonId)))
            {
                continue;
            }

            double score;
            try
            {
                score = target.Vector.CosineSimilarity(exemplar.Vector);
            }
            catch (ArgumentException exception)
            {
                throw new DataException(
                    "Embeddings from one model revision have inconsistent dimensions.",
                    exception);
            }

            if (!double.IsFinite(score))
            {
                throw new DataException("Cosine similarity produced a non-finite score.");
            }

            if (!bestByPerson.TryGetValue(exemplar.PersonId, out double existing)
                || score > existing)
            {
                bestByPerson[exemplar.PersonId] = score;
            }
        }

        return bestByPerson.Select(pair => new Candidate(pair.Key, pair.Value));
    }

    private static async Task ClearTargetRankingsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM identity_suggestion_rankings
            WHERE face_occurrence_id = @face_occurrence_id
              AND model_id = @model_id
              AND model_hash = @model_hash;
            """;
        AddVersionParameters(command, faceOccurrenceId, modelId, modelHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplaceSuggestionsAndRankingsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash,
        IReadOnlyList<Candidate> candidates,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        Guid? firstPersonId = candidates.Count > 0
            ? Guid.Parse(candidates[0].PersonId.ToString())
            : null;
        Guid? secondPersonId = candidates.Count > 1
            ? Guid.Parse(candidates[1].PersonId.ToString())
            : null;

        await using (NpgsqlCommand deleteStale = connection.CreateCommand())
        {
            deleteStale.Transaction = transaction;
            deleteStale.CommandText =
                """
                DELETE FROM identity_suggestions
                WHERE face_occurrence_id = @face_occurrence_id
                  AND model_id = @model_id
                  AND model_hash = @model_hash
                  AND status = @pending_status
                  AND (@first_person_id IS NULL OR suggested_person_id <> @first_person_id)
                  AND (@second_person_id IS NULL OR suggested_person_id <> @second_person_id);
                """;
            AddVersionParameters(deleteStale, faceOccurrenceId, modelId, modelHash);
            deleteStale.Parameters.AddWithValue("pending_status", PendingStatus);
            deleteStale.Parameters.Add(new NpgsqlParameter("first_person_id", NpgsqlDbType.Uuid)
            {
                Value = (object?)firstPersonId ?? DBNull.Value,
            });
            deleteStale.Parameters.Add(new NpgsqlParameter("second_person_id", NpgsqlDbType.Uuid)
            {
                Value = (object?)secondPersonId ?? DBNull.Value,
            });
            await deleteStale.ExecuteNonQueryAsync(cancellationToken);
        }

        double? margin = candidates.Count > 1
            ? Math.Max(0, candidates[0].Score - candidates[1].Score)
            : null;
        for (int index = 0; index < candidates.Count; index++)
        {
            Candidate candidate = candidates[index];
            long suggestionId = await UpsertSuggestionAsync(
                connection,
                transaction,
                faceOccurrenceId,
                candidate,
                modelId,
                modelHash,
                generatedAtUtc,
                cancellationToken);

            await using NpgsqlCommand insertRanking = connection.CreateCommand();
            insertRanking.Transaction = transaction;
            insertRanking.CommandText =
                """
                INSERT INTO identity_suggestion_rankings (
                    face_occurrence_id,
                    model_id,
                    model_hash,
                    rank,
                    suggestion_id,
                    score_margin,
                    generated_at_utc)
                VALUES (
                    @face_occurrence_id,
                    @model_id,
                    @model_hash,
                    @rank,
                    @suggestion_id,
                    @score_margin,
                    @generated_at_utc);
                """;
            AddVersionParameters(insertRanking, faceOccurrenceId, modelId, modelHash);
            insertRanking.Parameters.AddWithValue("rank", index + 1);
            insertRanking.Parameters.AddWithValue("suggestion_id", suggestionId);
            insertRanking.Parameters.Add(new NpgsqlParameter("score_margin", NpgsqlDbType.Double)
            {
                Value = (object?)margin ?? DBNull.Value,
            });
            insertRanking.Parameters.AddWithValue("generated_at_utc", generatedAtUtc);
            await insertRanking.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<long> UpsertSuggestionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        Candidate candidate,
        ModelId modelId,
        Sha256Digest modelHash,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO identity_suggestions (
                face_occurrence_id,
                suggested_person_id,
                model_id,
                model_hash,
                score,
                status,
                created_at_utc)
            VALUES (
                @face_occurrence_id,
                @suggested_person_id,
                @model_id,
                @model_hash,
                @score,
                @status,
                @created_at_utc)
            ON CONFLICT (face_occurrence_id, suggested_person_id, model_id, model_hash)
            DO UPDATE SET score = EXCLUDED.score
            RETURNING id;
            """;
        AddVersionParameters(command, faceOccurrenceId, modelId, modelHash);
        command.Parameters.AddWithValue(
            "suggested_person_id",
            Guid.Parse(candidate.PersonId.ToString()));
        command.Parameters.AddWithValue("score", candidate.Score);
        command.Parameters.AddWithValue("status", PendingStatus);
        command.Parameters.AddWithValue("created_at_utc", generatedAtUtc);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value);
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
            throw new DataException(
                "The stored embedding dimensions do not match its vector data.");
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
            throw new DataException(
                "The stored embedding norm does not match its vector data.");
        }

        return vector;
    }

    private static void AddVersionParameters(
        NpgsqlCommand command,
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue(
            "face_occurrence_id",
            Guid.Parse(faceOccurrenceId.ToString()));
        AddModelParameters(command, modelId, modelHash);
    }

    private static void AddModelParameters(
        NpgsqlCommand command,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue("model_id", modelId.ToString());
        command.Parameters.AddWithValue("model_hash", modelHash.ToString());
    }

    private sealed record StoredEmbedding(
        FaceOccurrenceId FaceOccurrenceId,
        EmbeddingVector Vector);

    private sealed record Exemplar(
        PersonId PersonId,
        FaceOccurrenceId FaceOccurrenceId,
        EmbeddingVector Vector);

    private sealed record Candidate(PersonId PersonId, double Score);

    private readonly record struct RejectedPair(
        FaceOccurrenceId FaceOccurrenceId,
        PersonId PersonId);
}
