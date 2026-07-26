using System.Buffers.Binary;
using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record IdentityMatchSummary(
    int TargetCount,
    int SuggestedTargetCount,
    int SuggestionCount);

public sealed record CatalogueRankedIdentitySuggestion(
    long SuggestionId,
    FaceOccurrenceId FaceOccurrenceId,
    PersonId SuggestedPersonId,
    ModelId ModelId,
    Sha256Digest ModelHash,
    int Rank,
    double Score,
    double? ScoreMargin,
    string Status,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// Regenerates exact cosine-similarity suggestions from current human-confirmed exemplars.
/// Suggestions never write canonical labels, and reviewed suggestion states are preserved.
/// </summary>
public sealed class SqliteIdentityMatcher
{
    private const string PendingStatus = "pending";
    private const string RejectedStatus = "rejected";

    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public SqliteIdentityMatcher(
        SqliteCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IdentityMatchSummary> RegenerateAsync(
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRankingSchemaAsync(connection, transaction, cancellationToken);

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
            cancellationToken);
        HashSet<RejectedPair> rejectedPairs = await ReadRejectedPairsAsync(
            connection,
            transaction,
            cancellationToken);

        await ClearRankingsAsync(connection, transaction, modelId, modelHash, cancellationToken);

        DateTimeOffset generatedAtUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        int suggestedTargetCount = 0;
        int suggestionCount = 0;

        foreach (StoredEmbedding target in targets)
        {
            Candidate[] candidates = ScoreCandidates(target, exemplars, rejectedPairs)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.PersonId.ToString(), StringComparer.Ordinal)
                .Take(2)
                .ToArray();

            if (candidates.Length > 0)
            {
                suggestedTargetCount++;
                suggestionCount += candidates.Length;
            }

            await ReplaceRankingsAsync(
                connection,
                transaction,
                target.FaceOccurrenceId,
                modelId,
                modelHash,
                candidates,
                generatedAtUtc,
                cancellationToken);
        }

        transaction.Commit();
        return new IdentityMatchSummary(targets.Count, suggestedTargetCount, suggestionCount);
    }

    public async Task<IReadOnlyList<CatalogueRankedIdentitySuggestion>> GetRankedSuggestionsAsync(
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await EnsureRankingSchemaAsync(connection, transaction, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
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
            WHERE ranking.face_occurrence_id = $face_occurrence_id
              AND ranking.model_id = $model_id
              AND ranking.model_hash = $model_hash
            ORDER BY ranking.rank;
            """;
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());

        List<CatalogueRankedIdentitySuggestion> suggestions = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                suggestions.Add(new CatalogueRankedIdentitySuggestion(
                    reader.GetInt64(0),
                    FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))),
                    PersonId.From(Guid.Parse(reader.GetString(2))),
                    new ModelId(reader.GetString(3)),
                    new Sha256Digest(reader.GetString(4)),
                    reader.GetInt32(5),
                    reader.GetDouble(6),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    reader.GetString(8),
                    ParseTimestamp(reader.GetString(9))));
            }
        }

        transaction.Commit();
        return suggestions;
    }

    private static async Task EnsureRankingSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS identity_suggestion_rankings (
                face_occurrence_id TEXT NOT NULL,
                model_id TEXT NOT NULL,
                model_hash TEXT NOT NULL,
                rank INTEGER NOT NULL CHECK (rank IN (1, 2)),
                suggestion_id INTEGER NOT NULL,
                score_margin REAL NULL CHECK (score_margin IS NULL OR score_margin >= 0),
                generated_at_utc TEXT NOT NULL,
                PRIMARY KEY (face_occurrence_id, model_id, model_hash, rank),
                UNIQUE (suggestion_id),
                FOREIGN KEY (face_occurrence_id) REFERENCES face_occurrences (id) ON DELETE CASCADE,
                FOREIGN KEY (suggestion_id) REFERENCES identity_suggestions (id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ClearRankingsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM identity_suggestion_rankings
            WHERE model_id = $model_id
              AND model_hash = $model_hash;
            """;
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<Exemplar>> ReadExemplarsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH latest_review AS (
                SELECT
                    face_occurrence_id,
                    action_kind,
                    person_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_occurrence_id
                        ORDER BY id DESC) AS row_number
                FROM review_actions
                WHERE action_kind IN ('assign', 'reject')
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
                WHERE embedding.model_id = $model_id
                  AND embedding.model_hash = $model_hash
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
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());

        List<Exemplar> exemplars = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            exemplars.Add(new Exemplar(
                PersonId.From(Guid.Parse(reader.GetString(0))),
                FaceOccurrenceId.From(Guid.Parse(reader.GetString(1))),
                ReadVector(reader, 2, 3, 4)));
        }

        return exemplars;
    }

    private static async Task<IReadOnlyList<StoredEmbedding>> ReadTargetsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ModelId modelId,
        Sha256Digest modelHash,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH latest_review AS (
                SELECT
                    face_occurrence_id,
                    ROW_NUMBER() OVER (
                        PARTITION BY face_occurrence_id
                        ORDER BY id DESC) AS row_number
                FROM review_actions
                WHERE action_kind IN ('assign', 'reject')
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
                WHERE embedding.model_id = $model_id
                  AND embedding.model_hash = $model_hash
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
                  FROM legacy_confirmed AS confirmed
                  WHERE confirmed.face_occurrence_id = matching.face_occurrence_id)
            ORDER BY matching.face_occurrence_id;
            """;
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());

        List<StoredEmbedding> targets = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            targets.Add(new StoredEmbedding(
                FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
                ReadVector(reader, 1, 2, 3)));
        }

        return targets;
    }

    private static async Task<HashSet<RejectedPair>> ReadRejectedPairsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT DISTINCT face_occurrence_id, suggested_person_id
            FROM identity_suggestions
            WHERE status = $status;
            """;
        command.Parameters.AddWithValue("$status", RejectedStatus);

        HashSet<RejectedPair> pairs = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pairs.Add(new RejectedPair(
                FaceOccurrenceId.From(Guid.Parse(reader.GetString(0))),
                PersonId.From(Guid.Parse(reader.GetString(1)))));
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

            if (!bestByPerson.TryGetValue(exemplar.PersonId, out double existing) || score > existing)
            {
                bestByPerson[exemplar.PersonId] = score;
            }
        }

        return bestByPerson.Select(pair => new Candidate(pair.Key, pair.Value));
    }

    private static async Task ReplaceRankingsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash,
        IReadOnlyList<Candidate> candidates,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        string? firstPersonId = candidates.Count > 0 ? candidates[0].PersonId.ToString() : null;
        string? secondPersonId = candidates.Count > 1 ? candidates[1].PersonId.ToString() : null;
        using (SqliteCommand deleteStale = connection.CreateCommand())
        {
            deleteStale.Transaction = transaction;
            deleteStale.CommandText = """
                DELETE FROM identity_suggestions
                WHERE face_occurrence_id = $face_occurrence_id
                  AND model_id = $model_id
                  AND model_hash = $model_hash
                  AND status = $pending_status
                  AND ($first_person_id IS NULL OR suggested_person_id <> $first_person_id)
                  AND ($second_person_id IS NULL OR suggested_person_id <> $second_person_id);
                """;
            AddVersionParameters(deleteStale, faceOccurrenceId, modelId, modelHash);
            deleteStale.Parameters.AddWithValue("$pending_status", PendingStatus);
            deleteStale.Parameters.AddWithValue("$first_person_id", (object?)firstPersonId ?? DBNull.Value);
            deleteStale.Parameters.AddWithValue("$second_person_id", (object?)secondPersonId ?? DBNull.Value);
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

            using SqliteCommand insertRanking = connection.CreateCommand();
            insertRanking.Transaction = transaction;
            insertRanking.CommandText = """
                INSERT INTO identity_suggestion_rankings (
                    face_occurrence_id,
                    model_id,
                    model_hash,
                    rank,
                    suggestion_id,
                    score_margin,
                    generated_at_utc)
                VALUES (
                    $face_occurrence_id,
                    $model_id,
                    $model_hash,
                    $rank,
                    $suggestion_id,
                    $score_margin,
                    $generated_at_utc);
                """;
            AddVersionParameters(insertRanking, faceOccurrenceId, modelId, modelHash);
            insertRanking.Parameters.AddWithValue("$rank", index + 1);
            insertRanking.Parameters.AddWithValue("$suggestion_id", suggestionId);
            insertRanking.Parameters.AddWithValue("$score_margin", (object?)margin ?? DBNull.Value);
            insertRanking.Parameters.AddWithValue("$generated_at_utc", Format(generatedAtUtc));
            await insertRanking.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<long> UpsertSuggestionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FaceOccurrenceId faceOccurrenceId,
        Candidate candidate,
        ModelId modelId,
        Sha256Digest modelHash,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken)
    {
        using (SqliteCommand upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO identity_suggestions (
                    face_occurrence_id,
                    suggested_person_id,
                    model_id,
                    model_hash,
                    score,
                    status,
                    created_at_utc)
                VALUES (
                    $face_occurrence_id,
                    $suggested_person_id,
                    $model_id,
                    $model_hash,
                    $score,
                    $status,
                    $created_at_utc)
                ON CONFLICT(face_occurrence_id, suggested_person_id, model_id, model_hash) DO UPDATE SET
                    score = excluded.score;
                """;
            upsert.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
            upsert.Parameters.AddWithValue("$suggested_person_id", candidate.PersonId.ToString());
            upsert.Parameters.AddWithValue("$model_id", modelId.ToString());
            upsert.Parameters.AddWithValue("$model_hash", modelHash.ToString());
            upsert.Parameters.AddWithValue("$score", candidate.Score);
            upsert.Parameters.AddWithValue("$status", PendingStatus);
            upsert.Parameters.AddWithValue("$created_at_utc", Format(generatedAtUtc));
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        using SqliteCommand select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT id
            FROM identity_suggestions
            WHERE face_occurrence_id = $face_occurrence_id
              AND suggested_person_id = $suggested_person_id
              AND model_id = $model_id
              AND model_hash = $model_hash;
            """;
        select.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        select.Parameters.AddWithValue("$suggested_person_id", candidate.PersonId.ToString());
        select.Parameters.AddWithValue("$model_id", modelId.ToString());
        select.Parameters.AddWithValue("$model_hash", modelHash.ToString());
        object? value = await select.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static void AddVersionParameters(
        SqliteCommand command,
        FaceOccurrenceId faceOccurrenceId,
        ModelId modelId,
        Sha256Digest modelHash)
    {
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId.ToString());
        command.Parameters.AddWithValue("$model_id", modelId.ToString());
        command.Parameters.AddWithValue("$model_hash", modelHash.ToString());
    }

    private static EmbeddingVector ReadVector(
        SqliteDataReader reader,
        int dimensionsOrdinal,
        int normOrdinal,
        int blobOrdinal)
    {
        int dimensions = reader.GetInt32(dimensionsOrdinal);
        double storedNorm = reader.GetDouble(normOrdinal);
        byte[] bytes = (byte[])reader.GetValue(blobOrdinal);
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

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private sealed record StoredEmbedding(FaceOccurrenceId FaceOccurrenceId, EmbeddingVector Vector);
    private sealed record Exemplar(PersonId PersonId, FaceOccurrenceId FaceOccurrenceId, EmbeddingVector Vector);
    private sealed record Candidate(PersonId PersonId, double Score);
    private readonly record struct RejectedPair(FaceOccurrenceId FaceOccurrenceId, PersonId PersonId);
}
