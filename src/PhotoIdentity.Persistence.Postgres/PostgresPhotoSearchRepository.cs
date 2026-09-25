using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoSearchRepository : IPhotoSearchRepository
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS photo_semantic_embeddings (
            asset_revision_id UUID NOT NULL REFERENCES asset_revisions(id) ON DELETE CASCADE,
            model_id TEXT NOT NULL,
            model_sha256 TEXT NOT NULL,
            preprocessing_version TEXT NOT NULL,
            vector_encoding TEXT NOT NULL,
            dimensions INTEGER NOT NULL CHECK (dimensions > 0),
            vector_values REAL[] NOT NULL,
            generation_milliseconds DOUBLE PRECISION NOT NULL CHECK (generation_milliseconds >= 0),
            generated_at_utc TIMESTAMPTZ NOT NULL,
            PRIMARY KEY (
                asset_revision_id,
                model_id,
                model_sha256,
                preprocessing_version,
                vector_encoding)
        );

        CREATE INDEX IF NOT EXISTS ix_photo_semantic_embeddings_model
            ON photo_semantic_embeddings (
                model_id,
                model_sha256,
                preprocessing_version,
                vector_encoding,
                asset_revision_id);

        CREATE INDEX IF NOT EXISTS ix_photo_generated_captions_search_simple
            ON photo_generated_captions
            USING GIN (to_tsvector('simple', COALESCE(content, '')));
        """;

    private readonly PostgresCatalogueDatabase _database;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private volatile bool _schemaReady;

    public PostgresPhotoSearchRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<AssetRevisionId>> GetEmbeddingCandidatesAsync(
        string reviewProxyProfileId,
        string modelId,
        string modelSha256,
        string preprocessingVersion,
        string vectorEncoding,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateFingerprint(modelId, modelSha256, preprocessingVersion, vectorEncoding);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewProxyProfileId);
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await EnsureSchemaAsync(cancellationToken);
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.id
            FROM assets AS asset
            INNER JOIN LATERAL (
                SELECT candidate.id
                FROM asset_revisions AS candidate
                WHERE candidate.asset_id = asset.id
                ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                LIMIT 1
            ) AS revision ON TRUE
            INNER JOIN asset_revision_review_proxies AS proxy
                ON proxy.asset_revision_id = revision.id
               AND proxy.profile_id = @profile_id
            LEFT JOIN photo_semantic_embeddings AS embedding
                ON embedding.asset_revision_id = revision.id
               AND embedding.model_id = @model_id
               AND embedding.model_sha256 = @model_sha256
               AND embedding.preprocessing_version = @preprocessing_version
               AND embedding.vector_encoding = @vector_encoding
            WHERE asset.deleted_at_utc IS NULL
              AND embedding.asset_revision_id IS NULL
            ORDER BY asset.source_key, revision.id
            LIMIT @limit;
            """;
        AddFingerprintParameters(command, modelId, modelSha256, preprocessingVersion, vectorEncoding);
        command.Parameters.AddWithValue("profile_id", NpgsqlDbType.Text, reviewProxyProfileId.Trim());
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);

        List<AssetRevisionId> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(AssetRevisionId.From(reader.GetGuid(0)));
        }
        return result;
    }

    public async Task SaveEmbeddingAsync(
        PhotoEmbeddingEvidence evidence,
        double generationMilliseconds,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!double.IsFinite(generationMilliseconds) || generationMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generationMilliseconds));
        }

        await EnsureSchemaAsync(cancellationToken);
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_semantic_embeddings (
                asset_revision_id,
                model_id,
                model_sha256,
                preprocessing_version,
                vector_encoding,
                dimensions,
                vector_values,
                generation_milliseconds,
                generated_at_utc)
            VALUES (
                @revision_id,
                @model_id,
                @model_sha256,
                @preprocessing_version,
                @vector_encoding,
                @dimensions,
                @vector_values,
                @generation_milliseconds,
                @generated_at_utc)
            ON CONFLICT (
                asset_revision_id,
                model_id,
                model_sha256,
                preprocessing_version,
                vector_encoding)
            DO UPDATE SET
                dimensions = excluded.dimensions,
                vector_values = excluded.vector_values,
                generation_milliseconds = excluded.generation_milliseconds,
                generated_at_utc = excluded.generated_at_utc;
            """;
        command.Parameters.AddWithValue(
            "revision_id",
            NpgsqlDbType.Uuid,
            Guid.Parse(evidence.RevisionId.ToString()));
        AddFingerprintParameters(
            command,
            evidence.ModelId,
            evidence.ModelSha256,
            evidence.PreprocessingVersion,
            evidence.VectorEncoding);
        command.Parameters.AddWithValue("dimensions", NpgsqlDbType.Integer, evidence.Dimensions);
        command.Parameters.AddWithValue(
            "vector_values",
            NpgsqlDbType.Array | NpgsqlDbType.Real,
            evidence.Values.ToArray());
        command.Parameters.AddWithValue(
            "generation_milliseconds",
            NpgsqlDbType.Double,
            generationMilliseconds);
        command.Parameters.AddWithValue(
            "generated_at_utc",
            generatedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PhotoSearchEmbeddingRecord>> GetCurrentEmbeddingsAsync(
        string modelId,
        string modelSha256,
        string preprocessingVersion,
        string vectorEncoding,
        CancellationToken cancellationToken = default)
    {
        ValidateFingerprint(modelId, modelSha256, preprocessingVersion, vectorEncoding);
        await EnsureSchemaAsync(cancellationToken);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                embedding.asset_revision_id,
                embedding.vector_values,
                embedding.generation_milliseconds,
                embedding.generated_at_utc
            FROM assets AS asset
            INNER JOIN LATERAL (
                SELECT candidate.id
                FROM asset_revisions AS candidate
                WHERE candidate.asset_id = asset.id
                ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                LIMIT 1
            ) AS revision ON TRUE
            INNER JOIN photo_semantic_embeddings AS embedding
                ON embedding.asset_revision_id = revision.id
               AND embedding.model_id = @model_id
               AND embedding.model_sha256 = @model_sha256
               AND embedding.preprocessing_version = @preprocessing_version
               AND embedding.vector_encoding = @vector_encoding
            WHERE asset.deleted_at_utc IS NULL
            ORDER BY revision.id;
            """;
        AddFingerprintParameters(command, modelId, modelSha256, preprocessingVersion, vectorEncoding);

        List<PhotoSearchEmbeddingRecord> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AssetRevisionId revisionId = AssetRevisionId.From(reader.GetGuid(0));
            float[] values = reader.GetFieldValue<float[]>(1);
            PhotoEmbeddingEvidence evidence = new(
                revisionId,
                modelId,
                modelSha256,
                preprocessingVersion,
                values);
            if (!string.Equals(evidence.VectorEncoding, vectorEncoding, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unsupported persisted semantic-search vector encoding '{vectorEncoding}'.");
            }

            result.Add(new PhotoSearchEmbeddingRecord(
                evidence,
                reader.GetDouble(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }
        return result;
    }

    public async Task<IReadOnlyList<PhotoSearchCaptionHit>> SearchCaptionsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await EnsureSchemaAsync(cancellationToken);
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH current_revisions AS (
                SELECT revision.id
                FROM assets AS asset
                INNER JOIN LATERAL (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1
                ) AS revision ON TRUE
                WHERE asset.deleted_at_utc IS NULL
            ),
            latest AS (
                SELECT DISTINCT ON (caption.asset_revision_id, caption.language)
                    caption.asset_revision_id,
                    caption.language,
                    caption.content,
                    caption.risk_flags,
                    caption.generated_at_utc
                FROM photo_generated_captions AS caption
                INNER JOIN current_revisions AS current
                    ON current.id = caption.asset_revision_id
                ORDER BY
                    caption.asset_revision_id,
                    caption.language,
                    caption.generated_at_utc DESC
            ),
            ranked AS (
                SELECT
                    latest.asset_revision_id,
                    latest.language,
                    latest.content,
                    (
                        ts_rank_cd(
                            to_tsvector('simple', latest.content),
                            plainto_tsquery('simple', @query))
                        + CASE
                            WHEN lower(latest.content) LIKE '%' || lower(@query) || '%' THEN 1.0
                            ELSE 0.0
                          END
                    )::double precision AS score
                FROM latest
                WHERE latest.content IS NOT NULL
                  AND cardinality(latest.risk_flags) = 0
                  AND (
                      to_tsvector('simple', latest.content)
                          @@ plainto_tsquery('simple', @query)
                      OR lower(latest.content) LIKE '%' || lower(@query) || '%'
                  )
            ),
            best AS (
                SELECT DISTINCT ON (asset_revision_id)
                    asset_revision_id,
                    language,
                    content,
                    score
                FROM ranked
                ORDER BY asset_revision_id, score DESC, language
            )
            SELECT
                asset_revision_id,
                language,
                content,
                score
            FROM best
            ORDER BY score DESC, asset_revision_id
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("query", NpgsqlDbType.Text, query.Trim());
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);

        List<PhotoSearchCaptionHit> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PhotoSearchCaptionHit(
                AssetRevisionId.From(reader.GetGuid(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3)));
        }
        return result;
    }

    public async Task<PhotoSearchCatalogueStatistics> GetCatalogueStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH current_revisions AS (
                SELECT revision.id
                FROM assets AS asset
                INNER JOIN LATERAL (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1
                ) AS revision ON TRUE
                WHERE asset.deleted_at_utc IS NULL
            ),
            latest_captions AS (
                SELECT DISTINCT ON (caption.asset_revision_id, caption.language)
                    caption.asset_revision_id,
                    caption.language,
                    caption.content,
                    caption.risk_flags
                FROM photo_generated_captions AS caption
                INNER JOIN current_revisions AS current
                    ON current.id = caption.asset_revision_id
                ORDER BY
                    caption.asset_revision_id,
                    caption.language,
                    caption.generated_at_utc DESC
            ),
            displayable_captions AS (
                SELECT DISTINCT asset_revision_id
                FROM latest_captions
                WHERE content IS NOT NULL
                  AND cardinality(risk_flags) = 0
            )
            SELECT
                (SELECT count(*) FROM current_revisions),
                (SELECT count(*) FROM displayable_captions);
            """;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidDataException("Photo search catalogue statistics query returned no row.");
        }
        return new PhotoSearchCatalogueStatistics(
            checked((int)reader.GetInt64(0)),
            checked((int)reader.GetInt64(1)));
    }

    public async Task<PhotoSearchStorageStatistics> GetStatisticsAsync(
        string modelId,
        string modelSha256,
        string preprocessingVersion,
        string vectorEncoding,
        CancellationToken cancellationToken = default)
    {
        ValidateFingerprint(modelId, modelSha256, preprocessingVersion, vectorEncoding);
        await EnsureSchemaAsync(cancellationToken);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH current_revisions AS (
                SELECT revision.id
                FROM assets AS asset
                INNER JOIN LATERAL (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1
                ) AS revision ON TRUE
                WHERE asset.deleted_at_utc IS NULL
            ),
            matching_embeddings AS (
                SELECT embedding.*
                FROM photo_semantic_embeddings AS embedding
                INNER JOIN current_revisions AS current
                    ON current.id = embedding.asset_revision_id
                WHERE embedding.model_id = @model_id
                  AND embedding.model_sha256 = @model_sha256
                  AND embedding.preprocessing_version = @preprocessing_version
                  AND embedding.vector_encoding = @vector_encoding
            ),
            latest_captions AS (
                SELECT DISTINCT ON (caption.asset_revision_id, caption.language)
                    caption.asset_revision_id,
                    caption.language,
                    caption.content,
                    caption.risk_flags
                FROM photo_generated_captions AS caption
                INNER JOIN current_revisions AS current
                    ON current.id = caption.asset_revision_id
                ORDER BY
                    caption.asset_revision_id,
                    caption.language,
                    caption.generated_at_utc DESC
            ),
            displayable_captions AS (
                SELECT DISTINCT asset_revision_id
                FROM latest_captions
                WHERE content IS NOT NULL
                  AND cardinality(risk_flags) = 0
            )
            SELECT
                (SELECT count(*) FROM current_revisions),
                (SELECT count(*) FROM matching_embeddings),
                (SELECT count(*) FROM displayable_captions),
                (SELECT max(dimensions) FROM matching_embeddings),
                COALESCE((SELECT sum(dimensions::bigint * 4) FROM matching_embeddings), 0),
                (SELECT avg(generation_milliseconds) FROM matching_embeddings);
            """;
        AddFingerprintParameters(command, modelId, modelSha256, preprocessingVersion, vectorEncoding);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidDataException("Semantic search statistics query returned no row.");
        }

        return new PhotoSearchStorageStatistics(
            checked((int)reader.GetInt64(0)),
            checked((int)reader.GetInt64(1)),
            checked((int)reader.GetInt64(2)),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetDouble(5));
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            PostgresPhotoCaptionRepository captionRepository = new(_database);
            _ = await captionRepository.GetSettingsAsync(cancellationToken);

            await using NpgsqlConnection connection =
                await _database.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = SchemaSql;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _schemaReady = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private static void ValidateFingerprint(
        string modelId,
        string modelSha256,
        string preprocessingVersion,
        string vectorEncoding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(preprocessingVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(vectorEncoding);
        string hash = modelSha256.Trim();
        if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Semantic-search model SHA-256 must contain exactly 64 hexadecimal characters.",
                nameof(modelSha256));
        }
    }

    private static void AddFingerprintParameters(
        NpgsqlCommand command,
        string modelId,
        string modelSha256,
        string preprocessingVersion,
        string vectorEncoding)
    {
        command.Parameters.AddWithValue("model_id", NpgsqlDbType.Text, modelId.Trim());
        command.Parameters.AddWithValue(
            "model_sha256",
            NpgsqlDbType.Text,
            modelSha256.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue(
            "preprocessing_version",
            NpgsqlDbType.Text,
            preprocessingVersion.Trim());
        command.Parameters.AddWithValue(
            "vector_encoding",
            NpgsqlDbType.Text,
            vectorEncoding.Trim());
    }
}
