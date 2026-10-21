using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

public sealed record PhotoQualityEvaluationCandidate(
    AssetRevisionId AssetRevisionId,
    string SourceKey,
    ArchiveReviewProxyMetadata Proxy);

public sealed record PhotoQualityBenchmarkPopulationRow(
    AssetRevisionId AssetRevisionId,
    string SourceKey,
    Sha256Digest ContentHash,
    string? MediaType,
    int? Width,
    int? Height,
    DateTime? TakenAtLocal,
    string? CameraMake,
    string? CameraModel,
    int FaceCount,
    ArchiveReviewProxyMetadata Proxy);

/// <summary>
/// Evaluation-only reader for current photo revisions that already have the requested durable
/// review proxy. It never requests or hydrates authoritative originals.
/// </summary>
public sealed class PostgresPhotoQualityEvaluationRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresPhotoQualityEvaluationRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<PhotoQualityEvaluationCandidate>> ReadCurrentProxySampleAsync(
        string profileId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await ReadCurrentProxyPageAsync(profileId, 0, limit, cancellationToken);
    }

    /// <summary>
    /// Stable, bounded pages of the existing review-proxy population. The
    /// caller stores its next offset; this never hydrates authoritative media.
    /// Rescans and concurrent catalogue changes may shift offsets slightly:
    /// consumers must deduplicate by revision and revalidate current proxies.
    /// </summary>
    public async Task<IReadOnlyList<PhotoQualityEvaluationCandidate>> ReadCurrentProxyPageAsync(
        string profileId, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (limit is <= 0 or > 5001 || offset is < 0 or > 200000)
            throw new ArgumentOutOfRangeException(nameof(limit),
                "Quality review page requires 1..5001 photos and a bounded nonnegative offset.");

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                revision.id,
                asset.source_key,
                proxy.profile_id,
                proxy.encoded_byte_length,
                proxy.content_sha256,
                proxy.width,
                proxy.height,
                proxy.generated_at_utc,
                proxy.relative_path
            FROM assets AS asset
            INNER JOIN archive_configuration AS configuration
                ON configuration.source_id = asset.source_id
            INNER JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                      AND candidate.media_kind = 'photo'
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            INNER JOIN asset_revision_review_proxies AS proxy
                ON proxy.asset_revision_id = revision.id
               AND proxy.profile_id = @profile_id
            WHERE asset.deleted_at_utc IS NULL
              AND asset.media_kind = 'photo'
              AND revision.media_kind = 'photo'

            ORDER BY md5(revision.id::text), revision.id
            LIMIT @limit OFFSET @offset;
            """;
        command.Parameters.AddWithValue("profile_id", profileId.Trim());
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);

        List<PhotoQualityEvaluationCandidate> candidates = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AssetRevisionId revisionId = AssetRevisionId.From(reader.GetGuid(0));
            candidates.Add(new PhotoQualityEvaluationCandidate(
                revisionId,
                reader.GetString(1),
                new ArchiveReviewProxyMetadata(
                    revisionId,
                    reader.GetString(2),
                    reader.GetInt64(3),
                    new Sha256Digest(reader.GetString(4)),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    reader.GetString(8))));
        }

        return candidates;
    }

    public async Task<IReadOnlyList<PhotoQualityBenchmarkPopulationRow>> ReadCurrentProxyPopulationAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                revision.id,
                asset.source_key,
                revision.content_sha256,
                revision.media_type,
                revision.width,
                revision.height,
                capture.taken_at_local,
                extended.camera_make,
                extended.camera_model,
                COALESCE(faces.face_count, 0),
                proxy.profile_id,
                proxy.encoded_byte_length,
                proxy.content_sha256,
                proxy.width,
                proxy.height,
                proxy.generated_at_utc,
                proxy.relative_path
            FROM assets AS asset
            INNER JOIN archive_configuration AS configuration
                ON configuration.source_id = asset.source_id
            INNER JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                      AND candidate.media_kind = 'photo'
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            INNER JOIN asset_revision_review_proxies AS proxy
                ON proxy.asset_revision_id = revision.id
               AND proxy.profile_id = @profile_id
            LEFT JOIN photo_capture_metadata AS capture
                ON capture.asset_revision_id = revision.id
            LEFT JOIN photo_extended_metadata AS extended
                ON extended.asset_revision_id = revision.id
            LEFT JOIN LATERAL (
                SELECT count(*)::integer AS face_count
                FROM face_occurrences AS face
                WHERE face.asset_revision_id = revision.id
            ) AS faces ON TRUE
            WHERE asset.deleted_at_utc IS NULL
              AND asset.media_kind = 'photo'
              AND revision.media_kind = 'photo'
            ORDER BY asset.source_key, revision.id;
            """;
        command.Parameters.AddWithValue("profile_id", profileId.Trim());

        List<PhotoQualityBenchmarkPopulationRow> rows = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AssetRevisionId revisionId = AssetRevisionId.From(reader.GetGuid(0));
            rows.Add(new PhotoQualityBenchmarkPopulationRow(
                revisionId,
                reader.GetString(1),
                new Sha256Digest(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetInt32(9),
                new ArchiveReviewProxyMetadata(
                    revisionId,
                    reader.GetString(10),
                    reader.GetInt64(11),
                    new Sha256Digest(reader.GetString(12)),
                    reader.GetInt32(13),
                    reader.GetInt32(14),
                    reader.GetFieldValue<DateTimeOffset>(15),
                    reader.GetString(16))));
        }

        return rows;
    }
}
