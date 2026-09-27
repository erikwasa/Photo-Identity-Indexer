using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Testing.Postgres;

public sealed record PhotoMetadataBackfillCandidate(
    AssetRevisionId RevisionId,
    Sha256Digest ContentHash,
    long SizeBytes,
    string RootLocator,
    string SourceKey,
    string? MediaType);

/// <summary>
/// Test-fixture facade for legacy catalogue seeding helpers. It preserves the small concrete API
/// used by integration tests while all reads and writes go to the PostgreSQL catalogue.
/// </summary>
public sealed class PostgresAssetCatalogueCompatibilityRepository : IPhotoCaptureMetadataRepository
{
    private readonly PostgresTestCatalogueDatabase _database;
    private readonly PostgresPhotoCaptureMetadataRepository _metadata;

    public PostgresAssetCatalogueCompatibilityRepository(PostgresTestCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _metadata = new PostgresPhotoCaptureMetadataRepository(database.Database);
    }

    public async Task<CatalogueAssetRevision> SaveRevisionAsync(
        CatalogueSource source,
        CatalogueAsset asset,
        CatalogueAssetRevision revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(revision);
        if (asset.SourceId != source.Id)
        {
            throw new ArgumentException("The asset must belong to the supplied source.", nameof(asset));
        }
        if (revision.AssetId != asset.Id)
        {
            throw new ArgumentException("The revision must belong to the supplied asset.", nameof(revision));
        }

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES (@source_id, @kind, @root_locator, @source_created_at)
                ON CONFLICT (id) DO UPDATE SET
                    kind = excluded.kind,
                    root_locator = excluded.root_locator;

                INSERT INTO assets (id, source_id, source_key, created_at_utc)
                VALUES (@asset_id, @source_id, @source_key, @asset_created_at)
                ON CONFLICT (id) DO UPDATE SET
                    source_id = excluded.source_id,
                    source_key = excluded.source_key;

                INSERT INTO asset_revisions (
                    id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
                VALUES (
                    @revision_id, @asset_id, @content_sha256, @size_bytes, @observed_at_utc, @media_type, @width, @height)
                ON CONFLICT (asset_id, content_sha256) DO NOTHING;
                """;
            command.Parameters.AddWithValue("source_id", Guid.Parse(source.Id.ToString()));
            command.Parameters.AddWithValue("kind", source.Kind);
            command.Parameters.AddWithValue("root_locator", source.RootLocator);
            command.Parameters.AddWithValue("source_created_at", source.CreatedAtUtc.ToUniversalTime());
            command.Parameters.AddWithValue("asset_id", Guid.Parse(asset.Id.ToString()));
            command.Parameters.AddWithValue("source_key", asset.SourceKey);
            command.Parameters.AddWithValue("asset_created_at", asset.CreatedAtUtc.ToUniversalTime());
            command.Parameters.AddWithValue("revision_id", Guid.Parse(revision.Id.ToString()));
            command.Parameters.AddWithValue("content_sha256", revision.ContentHash.ToString());
            command.Parameters.AddWithValue("size_bytes", revision.SizeBytes);
            command.Parameters.AddWithValue("observed_at_utc", revision.ObservedAtUtc.ToUniversalTime());
            command.Parameters.AddWithValue("media_type", (object?)revision.MediaType ?? DBNull.Value);
            command.Parameters.AddWithValue("width", (object?)revision.Width ?? DBNull.Value);
            command.Parameters.AddWithValue("height", (object?)revision.Height ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueAssetRevision persisted = await FindRevisionByContentAsync(
            connection,
            transaction,
            revision.AssetId,
            revision.ContentHash,
            cancellationToken)
            ?? throw new InvalidOperationException("The revision was not available after it was persisted.");
        await transaction.CommitAsync(cancellationToken);
        return persisted;
    }

    public async Task<CatalogueSource?> GetSourceAsync(SourceId id, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, kind, root_locator, created_at_utc FROM sources WHERE id = @id;";
        command.Parameters.AddWithValue("id", Guid.Parse(id.ToString()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSource(reader) : null;
    }

    public async Task<CatalogueSource?> FindSourceAsync(string kind, string rootLocator, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLocator);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, kind, root_locator, created_at_utc FROM sources WHERE kind = @kind AND root_locator = @root_locator;";
        command.Parameters.AddWithValue("kind", kind.Trim());
        command.Parameters.AddWithValue("root_locator", rootLocator.Trim());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSource(reader) : null;
    }

    public async Task<CatalogueAsset?> GetAssetAsync(AssetId id, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, source_id, source_key, created_at_utc FROM assets WHERE id = @id;";
        command.Parameters.AddWithValue("id", Guid.Parse(id.ToString()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAsset(reader) : null;
    }

    public async Task<CatalogueAsset?> FindAssetAsync(SourceId sourceId, string sourceKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, source_id, source_key, created_at_utc FROM assets WHERE source_id = @source_id AND source_key = @source_key;";
        command.Parameters.AddWithValue("source_id", Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("source_key", sourceKey.Trim());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAsset(reader) : null;
    }

    public async Task<CatalogueAssetRevision?> GetRevisionAsync(AssetRevisionId id, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height FROM asset_revisions WHERE id = @id;";
        command.Parameters.AddWithValue("id", Guid.Parse(id.ToString()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    public async Task<CatalogueAssetRevision?> GetLatestRevisionAsync(AssetId assetId, CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height FROM asset_revisions WHERE asset_id = @asset_id ORDER BY observed_at_utc DESC, id DESC LIMIT 1;";
        command.Parameters.AddWithValue("asset_id", Guid.Parse(assetId.ToString()));
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    public Task<PhotoCaptureMetadata?> GetPhotoMetadataAsync(AssetRevisionId revisionId, CancellationToken cancellationToken = default) =>
        _metadata.GetPhotoMetadataAsync(revisionId, cancellationToken);

    public Task SavePhotoMetadataAsync(
        AssetRevisionId revisionId,
        PhotoCaptureMetadata metadata,
        DateTimeOffset extractedAtUtc,
        CancellationToken cancellationToken = default) =>
        _metadata.SavePhotoMetadataAsync(revisionId, metadata, extractedAtUtc, cancellationToken);

    public async Task<IReadOnlyList<PhotoMetadataBackfillCandidate>> GetPhotoMetadataBackfillCandidatesAsync(
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Metadata backfill batch size must be between 1 and 5000.");
        }

        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                revision.id,
                revision.content_sha256,
                revision.size_bytes,
                source.root_locator,
                asset.source_key,
                revision.media_type
            FROM asset_revisions AS revision
            INNER JOIN assets AS asset ON asset.id = revision.asset_id
            INNER JOIN sources AS source ON source.id = asset.source_id
            LEFT JOIN photo_capture_metadata AS metadata ON metadata.asset_revision_id = revision.id
            WHERE metadata.asset_revision_id IS NULL
              AND asset.deleted_at_utc IS NULL
              AND source.kind = 'local-folder'
            ORDER BY revision.observed_at_utc, revision.id
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("limit", limit);
        List<PhotoMetadataBackfillCandidate> result = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PhotoMetadataBackfillCandidate(
                AssetRevisionId.From(reader.GetGuid(0)),
                new Sha256Digest(reader.GetString(1)),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return result;
    }

    private static async Task<CatalogueAssetRevision?> FindRevisionByContentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetId assetId,
        Sha256Digest contentHash,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height FROM asset_revisions WHERE asset_id = @asset_id AND content_sha256 = @content_sha256;";
        command.Parameters.AddWithValue("asset_id", Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue("content_sha256", contentHash.ToString());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    private static CatalogueSource ReadSource(NpgsqlDataReader reader) => new(
        SourceId.From(reader.GetGuid(0)),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetFieldValue<DateTimeOffset>(3));

    private static CatalogueAsset ReadAsset(NpgsqlDataReader reader) => new(
        AssetId.From(reader.GetGuid(0)),
        SourceId.From(reader.GetGuid(1)),
        reader.GetString(2),
        reader.GetFieldValue<DateTimeOffset>(3));

    private static CatalogueAssetRevision ReadRevision(NpgsqlDataReader reader) => new(
        AssetRevisionId.From(reader.GetGuid(0)),
        AssetId.From(reader.GetGuid(1)),
        new Sha256Digest(reader.GetString(2)),
        reader.GetInt64(3),
        reader.GetFieldValue<DateTimeOffset>(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetInt32(6),
        reader.IsDBNull(7) ? null : reader.GetInt32(7));
}
