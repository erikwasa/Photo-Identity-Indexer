using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Finds analyzed current revisions whose selected review proxy is still missing.
/// </summary>
public sealed class PostgresArchivePostAnalysisRepository :
    IArchivePostAnalysisRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchivePostAnalysisRepository(
        PostgresCatalogueDatabase database)
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

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
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
               AND analysis.profile_hash = @analysis_profile_hash
            LEFT JOIN asset_revision_review_proxies AS proxy
                ON proxy.asset_revision_id = revision.id
               AND proxy.profile_id = @proxy_profile_id
            WHERE asset.source_id = @source_id
              AND asset.deleted_at_utc IS NULL
              AND proxy.asset_revision_id IS NULL
            ORDER BY asset.source_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "source_id",
            Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue(
            "analysis_profile_hash",
            analysisProfileHash.ToString());
        command.Parameters.AddWithValue(
            "proxy_profile_id",
            proxyProfileId.Trim());

        object? value =
            await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id
            ? AssetRevisionId.From(id)
            : null;
    }
}
