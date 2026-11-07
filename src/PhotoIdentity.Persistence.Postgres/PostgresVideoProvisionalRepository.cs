using System.Data;
using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Provisional video identity for an online-only source that has not yet
/// received a content SHA or FFprobe inspection. No fabricated revision is
/// created. The existing source-level hydration budget/receipt owns the bytes
/// until verification can atomically transfer ownership to a real revision.
/// </summary>
public sealed record ProvisionalVideoHydration(
    ArchiveSourceObservationSnapshot Observation,
    bool ReleaseRequested,
    AssetRevisionId? LatestRevisionId);

public sealed class PostgresVideoProvisionalRepository(PostgresCatalogueDatabase database)
{
    private const long MaxVideoBytes = 256L * 1024 * 1024;

    public async Task<ArchiveSourceObservationSnapshot> ObserveAsync(
        ArchiveCatalogueSource source, SourceAsset video,
        DateTimeOffset observedAtUtc, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(video);
        if (source.Kind != "local-folder" ||
            source.SourceId != video.Reference.SourceId ||
            video.MediaType is not ("video/mp4" or "video/quicktime") ||
            video.SizeBytes is < 1 or > MaxVideoBytes ||
            !string.Equals(
                SourceCopyLocator.NormalizeSourceKey(video.Reference.ItemKey),
                video.Reference.ItemKey, StringComparison.Ordinal) ||
            Path.GetExtension(video.Reference.ItemKey).ToLowerInvariant()
                is not (".mov" or ".mp4"))
            throw new ArgumentException("Provisional registration requires one bounded MOV/MP4 source.");

        await using NpgsqlConnection conn = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction tx =
            await conn.BeginTransactionAsync(IsolationLevel.Serializable, token);
        // Same source-row lock used by video registration and exclusion writes.
        await using (NpgsqlCommand guard = conn.CreateCommand())
        {
            guard.Transaction = tx;
            guard.CommandText = """
                SELECT kind, root_locator FROM sources WHERE id=@source FOR UPDATE;
                """;
            guard.Parameters.AddWithValue("source", source.SourceId.Value);
            await using NpgsqlDataReader reader = await guard.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token) ||
                reader.GetString(0) != "local-folder" ||
                !string.Equals(reader.GetString(1), source.RootLocator, StringComparison.Ordinal))
                throw new InvalidOperationException("Provisional source identity changed.");
        }
        await using (NpgsqlCommand excluded = conn.CreateCommand())
        {
            excluded.Transaction = tx;
            excluded.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM source_copy_exclusions
                    WHERE source_id=@source AND source_key=@key);
                """;
            excluded.Parameters.AddWithValue("source", source.SourceId.Value);
            excluded.Parameters.AddWithValue("key", video.Reference.ItemKey);
            if (await excluded.ExecuteScalarAsync(token) is true)
                throw new InvalidOperationException("Excluded video cannot be hydrated.");
        }

        Guid assetId;
        await using (NpgsqlCommand upsert = conn.CreateCommand())
        {
            upsert.Transaction = tx;
            upsert.CommandText = """
                INSERT INTO assets(id, source_id, source_key, created_at_utc,
                                   last_seen_at_utc, media_kind)
                VALUES (@id,@source,@key,@now,@now,'video')
                ON CONFLICT(source_id,source_key) DO UPDATE SET
                    last_seen_at_utc=EXCLUDED.last_seen_at_utc
                WHERE assets.media_kind='video' AND assets.deleted_at_utc IS NULL
                RETURNING id;
                """;
            upsert.Parameters.AddWithValue("id", Guid.NewGuid());
            upsert.Parameters.AddWithValue("source", source.SourceId.Value);
            upsert.Parameters.AddWithValue("key", video.Reference.ItemKey);
            upsert.Parameters.AddWithValue("now", observedAtUtc.ToUniversalTime());
            if (await upsert.ExecuteScalarAsync(token) is not Guid id)
                throw new InvalidOperationException("Source key is not an active video asset.");
            assetId = id;
        }

        await using (NpgsqlCommand inspected = conn.CreateCommand())
        {
            inspected.Transaction = tx;
            inspected.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM asset_revisions revision
                    JOIN video_revision_metadata metadata
                      ON metadata.asset_revision_id=revision.id
                    WHERE revision.asset_id=@asset AND revision.media_kind='video');
                """;
            inspected.Parameters.AddWithValue("asset", assetId);
            if (await inspected.ExecuteScalarAsync(token) is true)
                throw new InvalidOperationException("Video is already inspected.");
        }

        await using (NpgsqlCommand observation = conn.CreateCommand())
        {
            observation.Transaction = tx;
            observation.CommandText = """
                INSERT INTO archive_source_observations(
                    asset_id, observed_size_bytes, observed_last_write_utc,
                    observed_last_write_ticks, observed_media_type,
                    observed_at_utc, verification_state)
                VALUES (@asset,@size,@written,@ticks,@media,@now,'unverified')
                ON CONFLICT(asset_id) DO UPDATE SET
                    observed_size_bytes=EXCLUDED.observed_size_bytes,
                    observed_last_write_utc=EXCLUDED.observed_last_write_utc,
                    observed_last_write_ticks=EXCLUDED.observed_last_write_ticks,
                    observed_media_type=EXCLUDED.observed_media_type,
                    observed_at_utc=EXCLUDED.observed_at_utc
                WHERE archive_source_observations.verification_state <> 'verified'
                  AND NOT EXISTS (
                    SELECT 1 FROM archive_source_managed_hydrations receipt
                    WHERE receipt.asset_id=EXCLUDED.asset_id
                      AND receipt.released_at_utc IS NULL);
                """;
            observation.Parameters.AddWithValue("asset", assetId);
            observation.Parameters.AddWithValue("size", video.SizeBytes);
            observation.Parameters.AddWithValue("written", video.LastWriteTimeUtc.ToUniversalTime());
            observation.Parameters.AddWithValue("ticks", video.LastWriteTimeUtc.UtcTicks);
            observation.Parameters.AddWithValue("media", video.MediaType);
            observation.Parameters.AddWithValue("now", observedAtUtc.ToUniversalTime());
            await observation.ExecuteNonQueryAsync(token);
        }

        ArchiveSourceObservationSnapshot? recorded = null;
        await using (NpgsqlCommand read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = """
                SELECT observed_size_bytes, observed_last_write_ticks,
                       observed_media_type, observed_at_utc,
                       verification_state
                FROM archive_source_observations WHERE asset_id=@asset;
                """;
            read.Parameters.AddWithValue("asset", assetId);
            await using NpgsqlDataReader reader = await read.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token) &&
                reader.GetInt64(0) == video.SizeBytes &&
                !reader.IsDBNull(1) &&
                reader.GetInt64(1) == video.LastWriteTimeUtc.UtcTicks &&
                reader.GetString(2) == video.MediaType &&
                reader.GetString(4) == "unverified")
                recorded = new(
                    AssetId.From(assetId), source.SourceId,
                    source.RootLocator, video.Reference.ItemKey,
                    video.SizeBytes, video.LastWriteTimeUtc,
                    video.MediaType, reader.GetFieldValue<DateTimeOffset>(3),
                    video.Availability,
                    ArchiveSourceObservationVerificationState.Unverified,
                    null, null);
        }
        if (recorded is null)
            throw new InvalidOperationException(
                "Provisional observation changed while hydration was active.");
        await tx.CommitAsync(token);
        return recorded;
    }

    /// <summary>
    /// Bounded, metadata-only recovery of provisional VIDEO ownership.
    /// Photo source verification/leases never enter this path.
    /// </summary>
    public async Task<IReadOnlyList<ProvisionalVideoHydration>> GetActiveAsync(
        int maximum = 8, CancellationToken token = default)
    {
        if (maximum is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        await using NpgsqlConnection conn = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.id,s.id,s.root_locator,a.source_key,
                   o.observed_size_bytes,o.observed_last_write_ticks,
                   o.observed_media_type,o.observed_at_utc,
                   h.release_requested_at_utc,
                   (SELECT r.id FROM asset_revisions r
                    WHERE r.asset_id=a.id AND r.media_kind='video'
                    ORDER BY r.observed_at_utc DESC,r.id DESC LIMIT 1)
            FROM archive_source_managed_hydrations h
            JOIN assets a ON a.id=h.asset_id
            JOIN sources s ON s.id=a.source_id
            JOIN archive_source_observations o ON o.asset_id=a.id
            WHERE a.media_kind='video' AND a.deleted_at_utc IS NULL
              AND s.kind='local-folder' AND h.released_at_utc IS NULL
            ORDER BY h.requested_at_utc,h.asset_id
            LIMIT @limit;
            """;
        cmd.Parameters.AddWithValue("limit", maximum);
        List<ProvisionalVideoHydration> result = [];
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (reader.IsDBNull(5)) continue; // Pre-migration metadata cannot authorize source release.
            var obs = new ArchiveSourceObservationSnapshot(
                AssetId.From(reader.GetGuid(0)), SourceId.From(reader.GetGuid(1)),
                reader.GetString(2), reader.GetString(3), reader.GetInt64(4),
                new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero),
                reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7),
                AssetAvailability.OnlineOnly,
                ArchiveSourceObservationVerificationState.Unverified, null, null);
            result.Add(new(obs, !reader.IsDBNull(8),
                reader.IsDBNull(9) ? null : AssetRevisionId.From(reader.GetGuid(9))));
        }
        return result;
    }
}
