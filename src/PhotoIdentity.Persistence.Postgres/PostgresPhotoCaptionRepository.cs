using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

public sealed class PostgresPhotoCaptionRepository : IPhotoCaptionRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresPhotoCaptionRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<PhotoCaptionEnrichmentSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT enabled, language, updated_at_utc
            FROM photo_caption_enrichment_settings
            WHERE id = 1;
            """;
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidDataException("Photo caption enrichment settings are missing.");
        }

        return new PhotoCaptionEnrichmentSettings(
            reader.GetBoolean(0),
            PhotoCaptionLanguages.Normalize(reader.GetString(1)),
            reader.GetFieldValue<DateTimeOffset>(2));
    }

    public async Task<PhotoCaptionEnrichmentSettings> UpdateSettingsAsync(
        bool enabled,
        string language,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        string normalizedLanguage = PhotoCaptionLanguages.Normalize(language);
        DateTimeOffset normalizedUpdatedAt = updatedAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_caption_enrichment_settings (
                id, enabled, language, updated_at_utc)
            VALUES (1, @enabled, @language, @updated_at_utc)
            ON CONFLICT(id) DO UPDATE SET
                enabled = excluded.enabled,
                language = excluded.language,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("enabled", NpgsqlDbType.Boolean, enabled);
        command.Parameters.AddWithValue("language", NpgsqlDbType.Text, normalizedLanguage);
        command.Parameters.AddWithValue("updated_at_utc", normalizedUpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new PhotoCaptionEnrichmentSettings(
            enabled,
            normalizedLanguage,
            normalizedUpdatedAt);
    }

    public async Task<IReadOnlyList<AssetRevisionId>> GetCandidatesAsync(
        string reviewProxyProfileId,
        string language,
        string generationVersion,
        string modelId,
        string modelDigest,
        string promptVersion,
        string imageMode,
        int contextTokens,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewProxyProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(generationVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageMode);
        string normalizedLanguage = PhotoCaptionLanguages.Normalize(language);
        if (contextTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextTokens));
        }
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.id
            FROM assets AS asset
            INNER JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            INNER JOIN asset_revision_review_proxies AS proxy
                ON proxy.asset_revision_id = revision.id
               AND proxy.profile_id = @profile_id
            LEFT JOIN photo_generated_captions AS caption
                ON caption.asset_revision_id = revision.id
               AND caption.language = @language
               AND caption.generation_version = @generation_version
               AND caption.model_id = @model_id
               AND caption.model_digest = @model_digest
               AND caption.prompt_version = @prompt_version
               AND caption.image_mode = @image_mode
               AND caption.context_tokens = @context_tokens
               AND caption.content IS NOT NULL
            WHERE asset.deleted_at_utc IS NULL
              AND caption.asset_revision_id IS NULL
            ORDER BY asset.source_key, revision.id
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("profile_id", reviewProxyProfileId.Trim());
        command.Parameters.AddWithValue("language", normalizedLanguage);
        command.Parameters.AddWithValue("generation_version", generationVersion.Trim());
        command.Parameters.AddWithValue("model_id", modelId.Trim());
        command.Parameters.AddWithValue("model_digest", modelDigest.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("prompt_version", promptVersion.Trim());
        command.Parameters.AddWithValue("image_mode", imageMode.Trim());
        command.Parameters.AddWithValue("context_tokens", contextTokens);
        command.Parameters.AddWithValue("limit", limit);

        List<AssetRevisionId> result = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(AssetRevisionId.From(reader.GetGuid(0)));
        }
        return result;
    }

    public async Task SaveAsync(
        PhotoGeneratedCaption caption,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caption);
        string language = PhotoCaptionLanguages.Normalize(caption.Language);
        string[] riskFlags = caption.RiskFlags
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO photo_generated_captions (
                asset_revision_id,
                language,
                generation_version,
                model_id,
                model_digest,
                prompt_version,
                image_mode,
                context_tokens,
                content,
                risk_flags,
                generation_milliseconds,
                generated_at_utc)
            VALUES (
                @revision_id,
                @language,
                @generation_version,
                @model_id,
                @model_digest,
                @prompt_version,
                @image_mode,
                @context_tokens,
                @content,
                @risk_flags,
                @generation_milliseconds,
                @generated_at_utc)
            ON CONFLICT (
                asset_revision_id,
                language,
                generation_version,
                model_id,
                model_digest,
                prompt_version,
                image_mode,
                context_tokens)
            DO UPDATE SET
                content = excluded.content,
                risk_flags = excluded.risk_flags,
                generation_milliseconds = excluded.generation_milliseconds,
                generated_at_utc = excluded.generated_at_utc;
            """;
        command.Parameters.AddWithValue(
            "revision_id",
            NpgsqlDbType.Uuid,
            Guid.Parse(caption.RevisionId.ToString()));
        command.Parameters.AddWithValue("language", NpgsqlDbType.Text, language);
        command.Parameters.AddWithValue("generation_version", NpgsqlDbType.Text, caption.GenerationVersion.Trim());
        command.Parameters.AddWithValue("model_id", NpgsqlDbType.Text, caption.ModelId.Trim());
        command.Parameters.AddWithValue("model_digest", NpgsqlDbType.Text, caption.ModelDigest.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("prompt_version", NpgsqlDbType.Text, caption.PromptVersion.Trim());
        command.Parameters.AddWithValue("image_mode", NpgsqlDbType.Text, caption.ImageMode.Trim());
        command.Parameters.AddWithValue("context_tokens", caption.ContextTokens);
        command.Parameters.AddWithValue("content", NpgsqlDbType.Text, (object?)caption.Content?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "risk_flags",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            riskFlags);
        command.Parameters.AddWithValue("generation_milliseconds", caption.GenerationMilliseconds);
        command.Parameters.AddWithValue("generated_at_utc", caption.GeneratedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PhotoGeneratedCaption?> GetLatestAsync(
        AssetRevisionId revisionId,
        string language,
        CancellationToken cancellationToken = default)
    {
        string normalizedLanguage = PhotoCaptionLanguages.Normalize(language);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                asset_revision_id,
                language,
                generation_version,
                model_id,
                model_digest,
                prompt_version,
                image_mode,
                context_tokens,
                content,
                risk_flags,
                generation_milliseconds,
                generated_at_utc
            FROM photo_generated_captions
            WHERE asset_revision_id = @revision_id
              AND language = @language
            ORDER BY generated_at_utc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "revision_id",
            NpgsqlDbType.Uuid,
            Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue("language", normalizedLanguage);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PhotoGeneratedCaption(
            AssetRevisionId.From(reader.GetGuid(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetFieldValue<string[]>(9),
            reader.GetDouble(10),
            reader.GetFieldValue<DateTimeOffset>(11));
    }
}
