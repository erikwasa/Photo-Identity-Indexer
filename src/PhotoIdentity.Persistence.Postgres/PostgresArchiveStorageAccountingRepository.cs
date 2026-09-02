using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Privacy-safe aggregate storage accounting backed by PostgreSQL archive state.
/// </summary>
public sealed class PostgresArchiveStorageAccountingRepository :
    IArchiveStorageAccountingRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveStorageAccountingRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<long> GetCurrentLogicalSourceBytesAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(
                SUM(
                    COALESCE(
                        observation.observed_size_bytes,
                        revision.size_bytes,
                        0)),
                0)
            FROM assets AS asset
            LEFT JOIN archive_source_observations AS observation
                ON observation.asset_id = asset.id
            LEFT JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY
                        candidate.observed_at_utc DESC,
                        candidate.id DESC
                    LIMIT 1)
            WHERE asset.source_id = @source_id
              AND asset.deleted_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue(
            "source_id",
            Guid.Parse(sourceId.ToString()));

        object? value =
            await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value ?? 0L);
    }

    public async Task<long> GetReviewProxyBytesAsync(
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return 0L;
        }

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COALESCE(SUM(encoded_byte_length), 0)
            FROM asset_revision_review_proxies
            WHERE profile_id = @profile_id;
            """;
        command.Parameters.AddWithValue(
            "profile_id",
            profileId.Trim());

        object? value =
            await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value ?? 0L);
    }
}
