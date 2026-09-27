using Npgsql;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

/// <summary>
/// PostgreSQL-backed compatibility facade for mature integration fixtures that predate the
/// provider-neutral catalogue source and asset-revision lookup contracts. The public shape is kept
/// temporarily so WI-0148 can remove the SQLite adapter dependency without rewriting unrelated
/// behavior tests; WI-0149 can then clean up the historical names.
/// </summary>
public sealed class PostgresLocalBatchCompatibilityRepository :
    IAssetRevisionLookupRepository,
    ICatalogueSourceRepository
{
    private readonly PostgresTestCatalogueDatabase _database;
    private readonly PostgresAssetRevisionLookupRepository _revisions;

    public PostgresLocalBatchCompatibilityRepository(PostgresTestCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _revisions = new PostgresAssetRevisionLookupRepository(database);
    }

    public async Task<CatalogueSource> GetOrCreateLocalFolderSourceAsync(
        string rootLocator,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLocator);
        string root = Path.GetFullPath(rootLocator);
        DateTimeOffset createdAt = createdAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        CatalogueSource? existing = await ReadSourceAsync(
            connection,
            transaction,
            "local-folder",
            root,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return existing;
        }

        CatalogueSource created = new(SourceId.New(), "local-folder", root, createdAt);
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES (@id, @kind, @root_locator, @created_at_utc)
                ON CONFLICT(kind, root_locator) DO NOTHING;
                """;
            command.Parameters.AddWithValue("id", created.Id.Value);
            command.Parameters.AddWithValue("kind", created.Kind);
            command.Parameters.AddWithValue("root_locator", created.RootLocator);
            command.Parameters.AddWithValue("created_at_utc", created.CreatedAtUtc);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueSource persisted = await ReadSourceAsync(
            connection,
            transaction,
            created.Kind,
            created.RootLocator,
            cancellationToken)
            ?? throw new InvalidOperationException("The local source was unavailable after it was persisted.");
        await transaction.CommitAsync(cancellationToken);
        return persisted;
    }

    async Task<ArchiveCatalogueSource> ICatalogueSourceRepository.GetOrCreateLocalFolderSourceAsync(
        string rootLocator,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        CatalogueSource source = await GetOrCreateLocalFolderSourceAsync(
            rootLocator,
            createdAtUtc,
            cancellationToken);
        return new ArchiveCatalogueSource(
            source.Id,
            source.Kind,
            source.RootLocator,
            source.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<AssetRevisionId>> GetCurrentRevisionIdsAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
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
            WHERE asset.source_id = @source_id
              AND asset.deleted_at_utc IS NULL
            ORDER BY asset.source_key;
            """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);

        List<AssetRevisionId> revisions = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(AssetRevisionId.From(reader.GetGuid(0)));
        }
        return revisions;
    }

    public Task<AssetRevisionLookup?> GetRevisionAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default) =>
        _revisions.GetRevisionAsync(revisionId, cancellationToken);

    public Task<AssetRevisionLookup?> FindRevisionAsync(
        string sourceKey,
        Sha256Digest contentHash,
        CancellationToken cancellationToken = default) =>
        _revisions.FindRevisionAsync(sourceKey, contentHash, cancellationToken);

    public Task<AssetRevisionLookup?> GetAssetRevisionAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default) =>
        _revisions.GetRevisionAsync(revisionId, cancellationToken);

    public Task<AssetRevisionLookup?> FindAssetRevisionAsync(
        string sourceKey,
        Sha256Digest contentHash,
        CancellationToken cancellationToken = default) =>
        _revisions.FindRevisionAsync(sourceKey, contentHash, cancellationToken);

    private static async Task<CatalogueSource?> ReadSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string kind,
        string rootLocator,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, kind, root_locator, created_at_utc
            FROM sources
            WHERE kind = @kind AND root_locator = @root_locator;
            """;
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("root_locator", rootLocator);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CatalogueSource(
                SourceId.From(reader.GetGuid(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3))
            : null;
    }
}
