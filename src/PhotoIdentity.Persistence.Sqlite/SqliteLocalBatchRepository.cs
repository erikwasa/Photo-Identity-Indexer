using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record CatalogueProcessingAssetRevision(
    AssetRevisionId RevisionId,
    AssetId AssetId,
    SourceId SourceId,
    string SourceKind,
    string RootLocator,
    string SourceKey,
    Sha256Digest ContentHash,
    long SizeBytes,
    string? MediaType) : IAssetRevisionStorageDescriptor;

/// <summary>
/// Resolves local source configuration and immutable revisions for durable batch processing.
/// </summary>
public sealed class SqliteLocalBatchRepository : IAssetRevisionLookupRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteLocalBatchRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueSource> GetOrCreateLocalFolderSourceAsync(
        string rootLocator,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLocator);
        string root = Path.GetFullPath(rootLocator);
        DateTimeOffset createdAt = createdAtUtc.ToUniversalTime();

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        CatalogueSource? existing = await ReadSourceAsync(
            connection,
            transaction,
            "local-folder",
            root,
            cancellationToken);
        if (existing is not null)
        {
            transaction.Commit();
            return existing;
        }

        CatalogueSource created = new(SourceId.New(), "local-folder", root, createdAt);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($id, $kind, $root_locator, $created_at_utc)
                ON CONFLICT(kind, root_locator) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$id", created.Id.ToString());
            command.Parameters.AddWithValue("$kind", created.Kind);
            command.Parameters.AddWithValue("$root_locator", created.RootLocator);
            command.Parameters.AddWithValue("$created_at_utc", Format(created.CreatedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueSource persisted = await ReadSourceAsync(
            connection,
            transaction,
            created.Kind,
            created.RootLocator,
            cancellationToken)
            ?? throw new InvalidOperationException("The local source was unavailable after it was persisted.");
        transaction.Commit();
        return persisted;
    }

    public async Task<IReadOnlyList<AssetRevisionId>> GetCurrentRevisionIdsAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default)
    {
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
            WHERE asset.source_id = $source_id
              AND asset.deleted_at_utc IS NULL
            ORDER BY asset.source_key;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());

        List<AssetRevisionId> revisions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(AssetRevisionId.From(Guid.Parse(reader.GetString(0))));
        }

        return revisions;
    }

    public async Task<AssetRevisionLookup?> GetRevisionAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default) =>
        ToLookup(await GetAssetRevisionAsync(revisionId, cancellationToken));

    public async Task<AssetRevisionLookup?> FindRevisionAsync(
        string sourceKey,
        Sha256Digest contentHash,
        CancellationToken cancellationToken = default) =>
        ToLookup(await FindAssetRevisionAsync(sourceKey, contentHash, cancellationToken));

    public async Task<CatalogueProcessingAssetRevision?> GetAssetRevisionAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = AssetRevisionSelect + " WHERE revision.id = $revision_id;";
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString());
        return await ReadAssetRevisionAsync(command, cancellationToken);
    }

    public async Task<CatalogueProcessingAssetRevision?> FindAssetRevisionAsync(
        string sourceKey,
        Sha256Digest contentHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = AssetRevisionSelect + "\n" + """
            WHERE asset.source_key = $source_key
              AND revision.content_sha256 = $content_sha256
            ORDER BY revision.observed_at_utc DESC, revision.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$source_key", sourceKey);
        command.Parameters.AddWithValue("$content_sha256", contentHash.ToString());
        return await ReadAssetRevisionAsync(command, cancellationToken);
    }

    private static AssetRevisionLookup? ToLookup(CatalogueProcessingAssetRevision? revision) =>
        revision is null
            ? null
            : new AssetRevisionLookup(
                revision.RevisionId,
                revision.AssetId,
                revision.SourceId,
                revision.SourceKind,
                revision.RootLocator,
                revision.SourceKey,
                revision.ContentHash,
                revision.SizeBytes,
                revision.MediaType);

    private const string AssetRevisionSelect = """
        SELECT
            revision.id,
            revision.asset_id,
            asset.source_id,
            source.kind,
            source.root_locator,
            asset.source_key,
            revision.content_sha256,
            revision.size_bytes,
            revision.media_type
        FROM asset_revisions AS revision
        INNER JOIN assets AS asset ON asset.id = revision.asset_id
        INNER JOIN sources AS source ON source.id = asset.source_id
        """;

    private static async Task<CatalogueProcessingAssetRevision?> ReadAssetRevisionAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CatalogueProcessingAssetRevision(
            AssetRevisionId.From(Guid.Parse(reader.GetString(0))),
            AssetId.From(Guid.Parse(reader.GetString(1))),
            SourceId.From(Guid.Parse(reader.GetString(2))),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            new Sha256Digest(reader.GetString(6)),
            reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    private static async Task<CatalogueSource?> ReadSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string kind,
        string rootLocator,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, kind, root_locator, created_at_utc
            FROM sources
            WHERE kind = $kind AND root_locator = $root_locator;
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$root_locator", rootLocator);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CatalogueSource(
                SourceId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.GetString(2),
                ParseTimestamp(reader.GetString(3)))
            : null;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
