using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// SQLite implementation of authoritative exact-duplicate inventory. Only source observations
/// whose current revision remains verified participate; historical and verification-pending rows do not.
/// </summary>
public sealed class SqliteExactDuplicateRepository : IExactDuplicateRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteExactDuplicateRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyList<ExactDuplicateGroup>> GetGroupsAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default)
    {
        await new SqliteArchiveSourceObservationRepository(_database).EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await EnsureIndexAsync(connection, cancellationToken);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH verified_current AS (
                SELECT
                    asset.source_id,
                    asset.id AS asset_id,
                    asset.source_key,
                    asset.deleted_at_utc,
                    observation.verified_revision_id AS revision_id
                FROM assets AS asset
                INNER JOIN archive_source_observations AS observation
                    ON observation.asset_id = asset.id
                WHERE asset.source_id = $source_id
                  AND observation.verification_state = 'verified'
                  AND observation.verified_revision_id IS NOT NULL
            ),
            duplicate_hashes AS (
                SELECT revision.content_sha256
                FROM verified_current AS current_copy
                INNER JOIN asset_revisions AS revision
                    ON revision.id = current_copy.revision_id
                   AND revision.asset_id = current_copy.asset_id
                GROUP BY revision.content_sha256
                HAVING COUNT(*) >= 2
            )
            SELECT
                revision.content_sha256,
                current_copy.source_id,
                current_copy.asset_id,
                current_copy.revision_id,
                current_copy.source_key,
                current_copy.deleted_at_utc
            FROM verified_current AS current_copy
            INNER JOIN asset_revisions AS revision
                ON revision.id = current_copy.revision_id
               AND revision.asset_id = current_copy.asset_id
            INNER JOIN duplicate_hashes AS duplicate
                ON duplicate.content_sha256 = revision.content_sha256
            ORDER BY
                revision.content_sha256,
                current_copy.source_key,
                current_copy.asset_id;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());

        List<ExactDuplicateGroup> groups = [];
        Sha256Digest? currentHash = null;
        List<ExactDuplicateSourceCopy> currentCopies = [];

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Sha256Digest hash = new(reader.GetString(0));
            if (currentHash is Sha256Digest existing && existing != hash)
            {
                groups.Add(new ExactDuplicateGroup(existing, currentCopies.ToArray()));
                currentCopies = [];
            }

            currentHash = hash;
            currentCopies.Add(new ExactDuplicateSourceCopy(
                SourceId.From(Guid.Parse(reader.GetString(1))),
                AssetId.From(Guid.Parse(reader.GetString(2))),
                AssetRevisionId.From(Guid.Parse(reader.GetString(3))),
                reader.GetString(4),
                !reader.IsDBNull(5)));
        }

        if (currentHash is Sha256Digest finalHash)
        {
            groups.Add(new ExactDuplicateGroup(finalHash, currentCopies.ToArray()));
        }

        return groups;
    }

    private static async Task EnsureIndexAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_asset_revisions_content_sha256
                ON asset_revisions (content_sha256, asset_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
