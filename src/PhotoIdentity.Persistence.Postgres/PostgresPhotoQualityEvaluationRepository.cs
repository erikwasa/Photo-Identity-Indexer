using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

public sealed record PhotoQualityEvaluationCandidate(
    AssetRevisionId AssetRevisionId,
    string SourceKey,
    ArchiveReviewProxyMetadata Proxy);

/// <summary>
/// Evaluation-only reader for a deterministic sample of current archive revisions that already
/// have the requested durable review proxy. It never requests or hydrates authoritative originals.
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
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        if (limit is <= 0 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Evaluation sample size must be between 1 and 5000.");
        }

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
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            INNER JOIN asset_revision_review_proxies AS proxy
                ON proxy.asset_revision_id = revision.id
               AND proxy.profile_id = @profile_id
            WHERE asset.deleted_at_utc IS NULL
            ORDER BY md5(revision.id::text)
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("profile_id", profileId.Trim());
        command.Parameters.AddWithValue("limit", limit);

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
}
