using System.Data;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Explicit, caller-gated video revision registration. It is NOT wired to the photo
/// scanner, hosted services, API, or UI. The caller must prove local availability,
/// stream-hash the original, and deliberately request registration.
/// </summary>
public sealed class PostgresVideoAssetRepository(PostgresCatalogueDatabase database)
{
    private readonly PostgresSourceCopyExclusionRepository _exclusions = new(database);

    public async Task<RegisteredVideoRevision> RegisterVerifiedAsync(
        VerifiedLocalVideo verified,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verified);
        Validate(verified);

        await database.InitializeAsync(cancellationToken);
        await _exclusions.EnsureReadyAsync(cancellationToken);

        SourceId sourceId = verified.Source.SourceId;
        string sourceKey = verified.Asset.Reference.ItemKey;
        DateTimeOffset timestamp = verified.VerifiedAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        // Source registration is anchored to its original stable source ID/kind/root.
        // Deliberately do not invent a video-specific source or silently change a root.
        await using (NpgsqlCommand ensureSource = connection.CreateCommand())
        {
            ensureSource.Transaction = transaction;
            ensureSource.CommandText = """
                INSERT INTO sources(id, kind, root_locator, created_at_utc)
                VALUES (@source_id, 'local-folder', @root, @created)
                ON CONFLICT(id) DO NOTHING;
                """;
            ensureSource.Parameters.AddWithValue("source_id", sourceId.Value);
            ensureSource.Parameters.AddWithValue("root", verified.Source.RootLocator);
            ensureSource.Parameters.AddWithValue("created", verified.Source.CreatedAtUtc.ToUniversalTime());
            await ensureSource.ExecuteNonQueryAsync(cancellationToken);
        }

        // A source-row update lock also serializes competing video observations for
        // that source. The exclusion table's FK insert takes a conflicting KEY SHARE
        // lock, so a newly requested exclusion cannot pass this pre-write check.
        await using (NpgsqlCommand lockSource = connection.CreateCommand())
        {
            lockSource.Transaction = transaction;
            lockSource.CommandText = """
                SELECT kind, root_locator FROM sources WHERE id = @id FOR UPDATE;
                """;
            lockSource.Parameters.AddWithValue("id", sourceId.Value);
            await using NpgsqlDataReader reader = await lockSource.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) ||
                reader.GetString(0) != "local-folder" ||
                !string.Equals(reader.GetString(1), verified.Source.RootLocator, StringComparison.Ordinal))
                throw new InvalidOperationException("Source identity differs from the registered catalogue source.");
        }

        await using (NpgsqlCommand exclusion = connection.CreateCommand())
        {
            exclusion.Transaction = transaction;
            exclusion.CommandText = """
                SELECT EXISTS(SELECT 1 FROM source_copy_exclusions
                              WHERE source_id = @source_id AND source_key = @source_key);
                """;
            exclusion.Parameters.AddWithValue("source_id", sourceId.Value);
            exclusion.Parameters.AddWithValue("source_key", sourceKey);
            if (await exclusion.ExecuteScalarAsync(cancellationToken) is true)
                throw new InvalidOperationException("The source copy is excluded and must not be indexed.");
        }

        Guid proposedAsset = Guid.NewGuid();
        Guid assetId;
        await using (NpgsqlCommand asset = connection.CreateCommand())
        {
            asset.Transaction = transaction;
            asset.CommandText = """
                INSERT INTO assets(
                    id, source_id, source_key, created_at_utc,
                    last_seen_at_utc, media_kind)
                VALUES (@id, @source_id, @source_key, @seen, @seen, 'video')
                ON CONFLICT(source_id, source_key) DO UPDATE
                    SET last_seen_at_utc = EXCLUDED.last_seen_at_utc,
                        deleted_at_utc = NULL
                WHERE assets.media_kind = 'video'
                RETURNING id;
                """;
            asset.Parameters.AddWithValue("id", proposedAsset);
            asset.Parameters.AddWithValue("source_id", sourceId.Value);
            asset.Parameters.AddWithValue("source_key", sourceKey);
            asset.Parameters.AddWithValue("seen", timestamp);
            object? value = await asset.ExecuteScalarAsync(cancellationToken);
            if (value is not Guid id)
                throw new InvalidOperationException(
                    "Existing photo-owned source key cannot be registered as a video.");
            assetId = id;
        }

        Guid proposedRevision = Guid.NewGuid();
        Guid revisionId;
        bool newRevision;
        await using (NpgsqlCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO asset_revisions (
                    id, asset_id, content_sha256, size_bytes,
                    observed_at_utc, media_type, media_kind)
                VALUES (
                    @id, @asset, @hash, @bytes, @observed, @mime, 'video')
                ON CONFLICT(asset_id, content_sha256) DO NOTHING
                RETURNING id;
                """;
            insert.Parameters.AddWithValue("id", proposedRevision);
            insert.Parameters.AddWithValue("asset", assetId);
            insert.Parameters.AddWithValue("hash", verified.ContentHash.ToString());
            insert.Parameters.AddWithValue("bytes", verified.Asset.SizeBytes);
            insert.Parameters.AddWithValue("observed", timestamp);
            insert.Parameters.AddWithValue("mime", verified.Asset.MediaType);
            object? value = await insert.ExecuteScalarAsync(cancellationToken);
            newRevision = value is Guid;
            revisionId = value is Guid inserted ? inserted : Guid.Empty;
        }
        if (!newRevision)
        {
            await using NpgsqlCommand existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = """
                SELECT id, size_bytes, media_type, media_kind
                FROM asset_revisions
                WHERE asset_id = @asset AND content_sha256 = @hash;
                """;
            existing.Parameters.AddWithValue("asset", assetId);
            existing.Parameters.AddWithValue("hash", verified.ContentHash.ToString());
            await using NpgsqlDataReader reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) ||
                reader.GetInt64(1) != verified.Asset.SizeBytes ||
                reader.GetString(2) != verified.Asset.MediaType ||
                reader.GetString(3) != "video")
                throw new InvalidOperationException(
                    "Existing content revision has conflicting media or size evidence.");
            revisionId = reader.GetGuid(0);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RegisteredVideoRevision(
            AssetId.From(assetId), AssetRevisionId.From(revisionId),
            assetId == proposedAsset, newRevision);
    }

    /// <summary>
    /// Returns the current, nonexcluded video revision only. An older poster must
    /// never be selected for the new content hash following a revision change.
    /// </summary>
    public async Task<AssetRevisionId?> GetLatestVisibleRevisionAsync(
        SourceId sourceId, string sourceKey, CancellationToken cancellationToken = default)
    {
        string key = SourceCopyLocator.NormalizeSourceKey(sourceKey);
        await database.InitializeAsync(cancellationToken);
        await _exclusions.EnsureReadyAsync(cancellationToken);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT revision.id
            FROM assets asset
            JOIN asset_revisions revision ON revision.asset_id = asset.id
            WHERE asset.source_id = @source_id AND asset.source_key = @key
              AND asset.deleted_at_utc IS NULL
              AND asset.media_kind = 'video' AND revision.media_kind = 'video'
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id = asset.source_id
                    AND excluded.source_key = asset.source_key)
            ORDER BY revision.observed_at_utc DESC, revision.id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("source_id", sourceId.Value);
        command.Parameters.AddWithValue("key", key);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? AssetRevisionId.From(id) : null;
    }

    private static void Validate(VerifiedLocalVideo value)
    {
        if (value.Source.SourceId.IsEmpty ||
            value.Source.Kind != "local-folder" ||
            value.Asset.Reference.SourceId != value.Source.SourceId ||
            value.Asset.Availability != AssetAvailability.Local ||
            value.Asset.SizeBytes is <= 0 or > (1024L * 1024 * 1024) ||
            value.Asset.MediaType is not ("video/mp4" or "video/quicktime") ||
            !string.Equals(
                SourceCopyLocator.NormalizeSourceKey(value.Asset.Reference.ItemKey),
                value.Asset.Reference.ItemKey, StringComparison.Ordinal) ||
            Path.GetExtension(value.Asset.Reference.ItemKey).ToLowerInvariant() is not (".mov" or ".mp4"))
            throw new ArgumentException("Only explicitly verified local MOV/MP4 video observations are accepted.");
    }
}
