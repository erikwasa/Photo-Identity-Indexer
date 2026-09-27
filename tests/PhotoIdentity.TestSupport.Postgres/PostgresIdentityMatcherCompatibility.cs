using System.Buffers.Binary;
using System.Data;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// PostgreSQL-backed compatibility matcher for mature application tests that predate the durable
/// regeneration worker. It preserves the legacy synchronous fixture semantics without referencing
/// the SQLite persistence assembly. Production continues to use the durable regeneration contracts.
/// </summary>
public sealed class PostgresIdentityMatcherCompatibility
{
    private const string PendingStatus = "pending";
    private const string RejectedStatus = "rejected";

    private readonly PostgresTestCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public PostgresIdentityMatcherCompatibility(
        PostgresTestCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IdentityMatchSummary> RegenerateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default) =>
        RegenerateAsync(
            modelId,
            modelHash,
            IdentityMatchTargetScope.UnreviewedOnly,
            cancellationToken);

    public async Task<IdentityMatchSummary> RegenerateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        IdentityMatchTargetScope targetScope,
        CancellationToken cancellationToken = default)
    {
        if (targetScope is not (
            IdentityMatchTargetScope.UnreviewedOnly or
            IdentityMatchTargetScope.UnreviewedAndUnknown))
        {
            throw new ArgumentOutOfRangeException(nameof(targetScope));
        }

        await _database.InitializeAsync(cancellationToken);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ClearDerivedPendingSuggestionsAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);

        IReadOnlyList<Exemplar> exemplars = await ReadExemplarsAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            cancellationToken);
        IReadOnlyList<StoredEmbedding> targets = await ReadTargetsAsync(
            connection,
            transaction,
            modelId,
            modelHash,
            targetScope,
            cancellationToken);

        DateTimeOffset generatedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        int suggestedTargetCount = 0;
        int suggestionCount = 0;

        foreach (StoredEmbedding target in targets)
        {
            HashSet<PersonId> rejectedPersonIds = await ReadRejectedPersonIdsAsync(
                connection,
                transaction,
                target.FaceOccurrenceId,
                cancellationToken);
            Candidate[] candidates = ScoreCandidates(target, exemplars, rejectedPersonIds)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.PersonId.ToString(), StringComparer.Ordinal)
                .Take(2)
                .ToArray();

            if (candidates.Length > 0)
            {
                suggestedTargetCount++;
                suggestionCount += candidates.Length;
            }

            await ReplaceSuggestionsAndRankingsAsync(
                connection,
                transaction,
                target.FaceOccurrenceId,
                modelId,
                modelHash,
                candidates,
                generatedAtUtc,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new IdentityMatchSummary(targets.Count, suggestedTargetCount, suggestionCount);
    }

    public async Task<IReadOnlyList<CatalogueRankedIdentitySuggestion>> GetRankedSuggestionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                suggestion.id,
                suggestion.face_occurrence_id,
                suggestion.suggested_person_id,
                suggestion.model_id,
                suggestion.model_hash,
                ranking.rank,
                suggestion.score,
                ranking.score_margin,
                suggestion.status,
                ranking.generated_at_utc
            FROM identity_suggestion_rankings AS ranking
            INNER JOIN identity_suggestions AS suggestion
                ON suggestion.id = ranking.suggestion_id
            WHERE ranking.face_occurrence_id = @face_occurrence_id
              AND ranking.model_id = @model_id
              AND ranking.model_hash = @model_hash
            ORDER BY ranking.rank;
            """;
        AddVersionParameters(command, faceOccurrenceId, modelId, modelHash);

        List<CatalogueRankedIdentitySuggestion> suggestions = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            suggestions.Add(new CatalogueRankedIdentitySuggestion(
                reader.GetInt64(0),
                FaceOccurrenceId.From(reader.GetGuid(1)),
                PersonId.From(reader.GetGuid(2)),
                new ModelId(reader.GetString(3)),
                new Sha256Digest(reader.GetString(4)),
                reader.GetInt32(5),
                reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.GetString(8),
                reader.GetFieldValue<DateTimeOffset>(9).ToUniversalTime()));
        }

        return suggestions;
    }

    private static async Task ClearDerivedPendingSuggestionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        await using (NpgsqlCommand rankings = connection.CreateCommand())
        {
            rankings.Transaction = transaction;
            rankings.CommandText =
                """
                DELETE FROM identity_suggestion_rankings
                WHERE model_id = @model_id
                  AND model_hash = @model_hash;
                """;
            AddModelParameters(rankings, modelId, modelHash);
            await rankings.ExecuteNonQueryAsync(cancellationToken);
        }

        await using NpgsqlCommand suggestions = connection.CreateCommand();
        suggestions.Transaction = transaction;
        suggestions.CommandText =
            """
            DELETE FROM identity_suggestions
            WHERE model_id = @model_id
              AND model_hash = @model_hash
              AND status = @pending_status;
            """;
        AddModelParameters(suggestions, modelId, modelHash);
        suggestions.Parameters.AddWithValue("pending_status", PendingStatus);
        await suggestions.ExecuteNonQueryAsync(cancellationToken);
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
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            exemplars.Add(new Exemplar(
                PersonId.From(reader.GetGuid(0)),
                ReadVector(reader, 2, 3, 4)));
        }

        return exemplars;
    }

    private static async Task<IReadOnlyList<StoredEmbedding>> ReadTargetsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        IdentityMatchTargetScope targetScope,
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
            legacy_confirmed AS (
                SELECT DISTINCT label.face_occurrence_id
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
                    AND review.row_number = 1
                    AND (NOT @include_unknown OR review.action_kind <> 'unknown'))
              AND NOT EXISTS (
                  SELECT 1
                  FROM legacy_confirmed AS confirmed
                  WHERE confirmed.face_occurrence_id = matching.face_occurrence_id)
            ORDER BY matching.face_occurrence_id;
            """;
        AddModelParameters(command, modelId, modelHash);
        command.Parameters.AddWithValue(
            "include_unknown",
            targetScope == IdentityMatchTargetScope.UnreviewedAndUnknown);

        List<StoredEmbedding> targets = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(new StoredEmbedding(
                FaceOccurrenceId.From(reader.GetGuid(0)),
                ReadVector(reader, 1, 2, 3)));
        }

        return targets;
    }

    private static async Task<HashSet<PersonId>> ReadRejectedPersonIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT DISTINCT suggested_person_id
            FROM identity_suggestions
            WHERE face_occurrence_id = @face_occurrence_id
              AND status = @status;
            """;
        command.Parameters.AddWithValue("face_occurrence_id", Guid.Parse(faceOccurrenceId.ToString()));
        command.Parameters.AddWithValue("status", RejectedStatus);

        HashSet<PersonId> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(PersonId.From(reader.GetGuid(0)));
        }

        return result;
    }

    private static IEnumerable<Candidate> ScoreCandidates(
        StoredEmbedding target,
        IReadOnlyList<Exemplar> exemplars,
        IReadOnlySet<PersonId> rejectedPersonIds)
    {
        Dictionary<PersonId, double> bestByPerson = [];
        foreach (Exemplar exemplar in exemplars)
        {
            if (rejectedPersonIds.Contains(exemplar.PersonId))
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

            if (!bestByPerson.TryGetValue(exemplar.PersonId, out double existing) || score > existing)
            {
                bestByPerson[exemplar.PersonId] = score;
            }
        }

        return bestByPerson.Select(pair => new Candidate(pair.Key, pair.Value));
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

            await using NpgsqlCommand ranking = connection.CreateCommand();
            ranking.Transaction = transaction;
            ranking.CommandText =
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
            AddVersionParameters(ranking, faceOccurrenceId, modelId, modelHash);
            ranking.Parameters.AddWithValue("rank", index + 1);
            ranking.Parameters.AddWithValue("suggestion_id", suggestionId);
            ranking.Parameters.Add(new NpgsqlParameter("score_margin", NpgsqlDbType.Double)
            {
                Value = (object?)margin ?? DBNull.Value,
            });
            ranking.Parameters.AddWithValue("generated_at_utc", generatedAtUtc);
            await ranking.ExecuteNonQueryAsync(cancellationToken);
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
        command.Parameters.AddWithValue("suggested_person_id", Guid.Parse(candidate.PersonId.ToString()));
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

    private static void AddVersionParameters(
        NpgsqlCommand command,
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue("face_occurrence_id", Guid.Parse(faceOccurrenceId.ToString()));
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

    private sealed record StoredEmbedding(FaceOccurrenceId FaceOccurrenceId, EmbeddingVector Vector);
    private sealed record Exemplar(PersonId PersonId, EmbeddingVector Vector);
    private sealed record Candidate(PersonId PersonId, double Score);
}
