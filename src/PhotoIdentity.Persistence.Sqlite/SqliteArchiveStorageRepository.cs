using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Privacy-safe aggregate storage queries for the permanent archive. No source paths or filenames
/// are returned from this repository.
/// </summary>
public sealed class SqliteArchiveStorageRepository : IArchiveStorageAccountingRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveStorageRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<long> GetCurrentLogicalSourceBytesAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default)
    {
        await new SqliteArchiveSourceObservationRepository(_database).EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(COALESCE(observation.observed_size_bytes, revision.size_bytes, 0)), 0)
            FROM assets AS asset
            LEFT JOIN archive_source_observations AS observation
                ON observation.asset_id = asset.id
            LEFT JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            WHERE asset.source_id = $source_id
              AND asset.deleted_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task<long> GetReviewProxyBytesAsync(
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return 0L;
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(encoded_byte_length), 0)
            FROM asset_revision_review_proxies
            WHERE profile_id = $profile_id;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId.Trim());
        try
        {
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            // Slice 1 tables are created lazily. An untouched catalogue has zero proxy bytes.
            return 0L;
        }
    }
}
