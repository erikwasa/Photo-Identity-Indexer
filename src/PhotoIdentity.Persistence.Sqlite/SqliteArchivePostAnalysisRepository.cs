using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Finds current immutable revisions whose governed analysis is durable but whose selected review
/// derivatives are still incomplete. This retry boundary lets derivative failures resume without
/// rerunning already-successful detector/embedder inference.
/// </summary>
public sealed class SqliteArchivePostAnalysisRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchivePostAnalysisRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<AssetRevisionId?> GetNextMissingProxyRevisionAsync(
        SourceId sourceId,
        Sha256Digest analysisProfileHash,
        string proxyProfileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyProfileId);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
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
            INNER JOIN asset_revision_analysis AS analysis
                ON analysis.asset_revision_id = revision.id
               AND analysis.profile_hash = $analysis_profile_hash
            LEFT JOIN asset_revision_review_proxies AS proxy
                ON proxy.asset_revision_id = revision.id
               AND proxy.profile_id = $proxy_profile_id
            WHERE asset.source_id = $source_id
              AND asset.deleted_at_utc IS NULL
              AND proxy.asset_revision_id IS NULL
            ORDER BY asset.source_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$analysis_profile_hash", analysisProfileHash.ToString());
        command.Parameters.AddWithValue("$proxy_profile_id", proxyProfileId.Trim());
        try
        {
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return value is string id ? AssetRevisionId.From(Guid.Parse(id)) : null;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            return null;
        }
    }

    public async Task<AssetRevisionId?> GetNextIncompleteDerivativeRevisionAsync(
        SourceId sourceId,
        Sha256Digest analysisProfileHash,
        string proxyProfileId,
        string faceReviewProfileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(faceReviewProfileId);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
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
            INNER JOIN asset_revision_analysis AS analysis
                ON analysis.asset_revision_id = revision.id
               AND analysis.profile_hash = $analysis_profile_hash
            LEFT JOIN asset_revision_review_proxies AS proxy
                ON proxy.asset_revision_id = revision.id
               AND proxy.profile_id = $proxy_profile_id
            LEFT JOIN asset_revision_face_review_completions AS face_review
                ON face_review.asset_revision_id = revision.id
               AND face_review.profile_id = $face_review_profile_id
            WHERE asset.source_id = $source_id
              AND asset.deleted_at_utc IS NULL
              AND (
                    proxy.asset_revision_id IS NULL
                    OR face_review.asset_revision_id IS NULL
                  )
            ORDER BY asset.source_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$analysis_profile_hash", analysisProfileHash.ToString());
        command.Parameters.AddWithValue("$proxy_profile_id", proxyProfileId.Trim());
        command.Parameters.AddWithValue("$face_review_profile_id", faceReviewProfileId.Trim());
        try
        {
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return value is string id ? AssetRevisionId.From(Guid.Parse(id)) : null;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            return null;
        }
    }
}
