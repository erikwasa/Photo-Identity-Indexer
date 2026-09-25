using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Finds current immutable revisions whose governed analysis is durable but whose selected review
/// proxy is still missing. Excluded source copies are filtered before derivative work is selected.
/// </summary>
public sealed class SqliteArchivePostAnalysisRepository : IArchivePostAnalysisRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly SqliteSourceCopyExclusionRepository _exclusions;

    public SqliteArchivePostAnalysisRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _exclusions = new SqliteSourceCopyExclusionRepository(database);
    }

    public async Task<AssetRevisionId?> GetNextMissingProxyRevisionAsync(
        SourceId sourceId,
        Sha256Digest analysisProfileHash,
        string proxyProfileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyProfileId);
        IReadOnlyList<SourceCopyExclusionState> exclusions =
            await _exclusions.ListAsync(sourceId, cancellationToken);
        HashSet<string> excludedKeys = exclusions
            .Select(item => item.SourceKey)
            .ToHashSet(StringComparer.Ordinal);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.id, asset.source_key
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
            ORDER BY asset.source_key;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$analysis_profile_hash", analysisProfileHash.ToString());
        command.Parameters.AddWithValue("$proxy_profile_id", proxyProfileId.Trim());
        try
        {
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!excludedKeys.Contains(reader.GetString(1)))
                {
                    return AssetRevisionId.From(Guid.Parse(reader.GetString(0)));
                }
            }

            return null;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            // Analysis/proxy tables are created lazily. No durable analysis means no post-analysis
            // proxy work can be pending yet.
            return null;
        }
    }
}
