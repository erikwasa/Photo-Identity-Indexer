using System.Globalization;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Scan-specific persistence path for permanent archive synchronization. The scanner loads
/// lightweight verification baselines once, decides which local originals actually require
/// SHA-256 reads, then persists one included-folder batch in a single PostgreSQL transaction.
/// </summary>
public sealed class PostgresArchiveSourceScanBatchRepository : IArchiveSourceScanPersistence
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveSourceScanBatchRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<IReadOnlyDictionary<string, ArchiveSourceScanBaseline>> GetBaselinesAsync(
        SourceId sourceId,
        string? relativeRoot,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadBaselinesAsync(connection, sourceId, relativeRoot, cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, ArchiveSourceScanBaseline>> ReadBaselinesAsync(
        NpgsqlConnection connection, SourceId sourceId, string? relativeRoot, CancellationToken cancellationToken,
        string[]? keys = null)
    {
        string scope = ArchiveCoverage.NormalizeRelativeFolder(relativeRoot ?? string.Empty);
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = scope.Length == 0
            ? """
              SELECT
                  asset.source_key,
                  CASE WHEN asset.deleted_at_utc IS NULL THEN 0 ELSE 1 END AS was_deleted,
                  observation.verification_state,
                  observation.verified_revision_id,
                  observation.verified_size_bytes,
                  observation.verified_last_write_utc,
                  observation.verified_media_type,
                  observation.verified_at_utc,
                  (
                      SELECT revision.id
                      FROM asset_revisions AS revision
                      WHERE revision.asset_id = asset.id
                      ORDER BY revision.observed_at_utc DESC, revision.id DESC
                      LIMIT 1
                  ) AS latest_revision_id, observation.verified_last_write_ticks
              FROM assets AS asset
              LEFT JOIN archive_source_observations AS observation ON observation.asset_id = asset.id
              WHERE asset.source_id = @source_id;
              """
            : """
              SELECT
                  asset.source_key,
                  CASE WHEN asset.deleted_at_utc IS NULL THEN 0 ELSE 1 END AS was_deleted,
                  observation.verification_state,
                  observation.verified_revision_id,
                  observation.verified_size_bytes,
                  observation.verified_last_write_utc,
                  observation.verified_media_type,
                  observation.verified_at_utc,
                  (
                      SELECT revision.id
                      FROM asset_revisions AS revision
                      WHERE revision.asset_id = asset.id
                      ORDER BY revision.observed_at_utc DESC, revision.id DESC
                      LIMIT 1
                  ) AS latest_revision_id, observation.verified_last_write_ticks
              FROM assets AS asset
              LEFT JOIN archive_source_observations AS observation ON observation.asset_id = asset.id
              WHERE asset.source_id = @source_id
                AND substr(asset.source_key, 1, length(@scope_prefix)) = @scope_prefix;
              """;
        if (keys is not null)
        {
            command.CommandText = command.CommandText.TrimEnd().TrimEnd(';') + " AND asset.source_key = ANY(@keys);";
            command.Parameters.AddWithValue("keys", NpgsqlDbType.Array | NpgsqlDbType.Text, keys);
        }
        command.Parameters.AddWithValue("@source_id", Guid.Parse(sourceId.ToString()));
        if (scope.Length > 0)
        {
            command.Parameters.AddWithValue("@scope_prefix", scope + "/");
        }

        Dictionary<string, ArchiveSourceScanBaseline> baselines = new(StringComparer.Ordinal);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string sourceKey = reader.GetString(0);
            baselines[sourceKey] = new ArchiveSourceScanBaseline(
                sourceKey,
                reader.GetInt32(1) != 0,
                reader.IsDBNull(2)
                    ? null
                    : (ArchiveSourceObservationVerificationState)ParseVerificationState(reader.GetString(2)),
                reader.IsDBNull(3) ? null : AssetRevisionId.From(reader.GetGuid(3)),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.IsDBNull(9) ? reader.GetFieldValue<DateTimeOffset>(5) : new DateTimeOffset(reader.GetInt64(9), TimeSpan.Zero),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : AssetRevisionId.From(reader.GetGuid(8)));
        }

        return baselines;
    }

    public async Task<IReadOnlyList<ArchiveSourceObservationPersistenceResult>> RecordBatchAsync(
        ArchiveCatalogueSource source,
        IReadOnlyList<ArchiveSourceScanWrite> writes,
        IReadOnlyDictionary<string, ArchiveSourceScanBaseline> baselines,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(baselines);
        if (writes.Count == 0)
        {
            return [];
        }

        DateTimeOffset scannedAt = scannedAtUtc.ToUniversalTime();
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        await UpsertSourceAsync(connection, transaction, source, cancellationToken);
        // Refresh baselines inside the write snapshot; concurrent verification cannot be silently overwritten.
        baselines = await ReadBaselinesAsync(connection, source.SourceId, null, cancellationToken,
            writes.Select(write => write.SourceAsset.Reference.ItemKey).Distinct(StringComparer.Ordinal).ToArray());

        List<ArchiveSourceObservationPersistenceResult> results = new(writes.Count);
        foreach (ArchiveSourceScanWrite write in writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceAsset sourceAsset = write.SourceAsset;
            DateTimeOffset observedWrite = sourceAsset.LastWriteTimeUtc.ToUniversalTime();
            AssetId assetId = await UpsertAssetAsync(
                connection,
                transaction,
                source,
                sourceAsset.Reference.ItemKey,
                scannedAt,
                cancellationToken);
            await UpsertAvailabilityAsync(
                connection,
                transaction,
                assetId,
                sourceAsset.Availability,
                scannedAt,
                cancellationToken);

            baselines.TryGetValue(sourceAsset.Reference.ItemKey, out ArchiveSourceScanBaseline? baseline);
            bool newRevision = false;
            AssetRevisionId? revisionId = null;
            ArchiveSourceObservationVerificationState verificationState;
            AssetRevisionId? verifiedRevisionId = baseline?.VerifiedRevisionId ?? baseline?.LatestRevisionId;
            long? verifiedSize = baseline?.VerifiedSizeBytes;
            DateTimeOffset? verifiedWrite = baseline?.VerifiedLastWriteTimeUtc;
            string? verifiedMediaType = baseline?.VerifiedMediaType;
            DateTimeOffset? verifiedAt = baseline?.VerifiedAtUtc;

            if (write.VerifiedContentHash is Sha256Digest contentHash)
            {
                (revisionId, newRevision) = await UpsertRevisionAsync(
                    connection,
                    transaction,
                    assetId,
                    contentHash,
                    sourceAsset.SizeBytes,
                    sourceAsset.MediaType,
                    scannedAt,
                    cancellationToken);
                verificationState = ArchiveSourceObservationVerificationState.Verified;
                verifiedRevisionId = revisionId;
                verifiedSize = sourceAsset.SizeBytes;
                verifiedWrite = observedWrite;
                verifiedMediaType = sourceAsset.MediaType;
                verifiedAt = scannedAt;
            }
            else if (baseline?.WasDeleted == true && baseline.LatestRevisionId is not null)
            {
                verificationState = ArchiveSourceObservationVerificationState.NeedsSourceVerification;
            }
            else if (baseline?.VerificationState == ArchiveSourceObservationVerificationState.NeedsSourceVerification)
            {
                verificationState = ArchiveSourceObservationVerificationState.NeedsSourceVerification;
            }
            else if (baseline is not null &&
                baseline.VerifiedRevisionId is not null &&
                baseline.VerifiedSizeBytes is long baselineSize &&
                baseline.VerifiedLastWriteTimeUtc is DateTimeOffset baselineWrite &&
                baseline.VerifiedMediaType is string baselineMedia)
            {
                bool metadataMatches = baselineSize == sourceAsset.SizeBytes &&
                    baselineWrite == observedWrite &&
                    string.Equals(baselineMedia, sourceAsset.MediaType, StringComparison.Ordinal);
                verificationState = metadataMatches
                    ? ArchiveSourceObservationVerificationState.Verified
                    : ArchiveSourceObservationVerificationState.NeedsSourceVerification;
            }
            else if (baseline?.LatestRevisionId is not null)
            {
                verificationState = ArchiveSourceObservationVerificationState.NeedsSourceVerification;
            }
            else
            {
                verificationState = ArchiveSourceObservationVerificationState.Unverified;
            }

            await UpsertObservationAsync(
                connection,
                transaction,
                assetId,
                sourceAsset.SizeBytes,
                observedWrite,
                sourceAsset.MediaType,
                scannedAt,
                verificationState,
                verifiedRevisionId,
                verifiedSize,
                verifiedWrite,
                verifiedMediaType,
                verifiedAt,
                cancellationToken);

            results.Add(new ArchiveSourceObservationPersistenceResult(
                assetId,
                revisionId ?? verifiedRevisionId,
                newRevision,
                verificationState));
        }

        await transaction.CommitAsync(cancellationToken);
        return results;
    }

    private static async Task UpsertSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ArchiveCatalogueSource source,
        CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@id, @kind, @root_locator, @created_at_utc)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                root_locator = excluded.root_locator;
            """;
        command.Parameters.AddWithValue("@id", Guid.Parse(source.SourceId.ToString()));
        command.Parameters.AddWithValue("@kind", source.Kind);
        command.Parameters.AddWithValue("@root_locator", source.RootLocator);
        command.Parameters.AddWithValue("@created_at_utc", Format(source.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AssetId> UpsertAssetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ArchiveCatalogueSource source,
        string sourceKey,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        AssetId proposed = AssetId.New();
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc)
            VALUES (@id, @source_id, @source_key, @created_at_utc, @last_seen_at_utc, NULL)
            ON CONFLICT(source_id, source_key) DO UPDATE SET
                last_seen_at_utc = excluded.last_seen_at_utc,
                deleted_at_utc = NULL
            RETURNING id;
            """;
        command.Parameters.AddWithValue("@id", Guid.Parse(proposed.ToString()));
        command.Parameters.AddWithValue("@source_id", Guid.Parse(source.SourceId.ToString()));
        command.Parameters.AddWithValue("@source_key", sourceKey);
        command.Parameters.AddWithValue("@created_at_utc", Format(observedAtUtc));
        command.Parameters.AddWithValue("@last_seen_at_utc", Format(observedAtUtc));
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id
            ? AssetId.From(id)
            : throw new InvalidOperationException("Archive asset was unavailable after batched persistence.");
    }

    private static async Task UpsertAvailabilityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetId assetId,
        AssetAvailability availability,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO archive_asset_availability (asset_id, availability, checked_at_utc)
            VALUES (@asset_id, @availability, @checked_at_utc)
            ON CONFLICT(asset_id) DO UPDATE SET
                availability = excluded.availability,
                checked_at_utc = excluded.checked_at_utc;
            """;
        command.Parameters.AddWithValue("@asset_id", Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue("@availability", ToStorageValue(availability));
        command.Parameters.AddWithValue("@checked_at_utc", Format(checkedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(AssetRevisionId RevisionId, bool NewRevision)> UpsertRevisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetId assetId,
        Sha256Digest contentHash,
        long sizeBytes,
        string mediaType,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        AssetRevisionId proposed = AssetRevisionId.New();
        using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO asset_revisions (
                    id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
                VALUES (
                    @id, @asset_id, @content_sha256, @size_bytes, @observed_at_utc, @media_type, NULL, NULL)
                ON CONFLICT(asset_id, content_sha256) DO UPDATE SET
                    size_bytes = excluded.size_bytes,
                    observed_at_utc = excluded.observed_at_utc,
                    media_type = excluded.media_type;
                """;
            command.Parameters.AddWithValue("@id", Guid.Parse(proposed.ToString()));
            command.Parameters.AddWithValue("@asset_id", Guid.Parse(assetId.ToString()));
            command.Parameters.AddWithValue("@content_sha256", contentHash.ToString());
            command.Parameters.AddWithValue("@size_bytes", sizeBytes);
            command.Parameters.AddWithValue("@observed_at_utc", Format(observedAtUtc));
            command.Parameters.AddWithValue("@media_type", mediaType);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using NpgsqlCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT id
            FROM asset_revisions
            WHERE asset_id = @asset_id AND content_sha256 = @content_sha256;
            """;
        read.Parameters.AddWithValue("@asset_id", Guid.Parse(assetId.ToString()));
        read.Parameters.AddWithValue("@content_sha256", contentHash.ToString());
        object? value = await read.ExecuteScalarAsync(cancellationToken);
        AssetRevisionId revisionId = value is Guid id
            ? AssetRevisionId.From(id)
            : throw new InvalidOperationException("Archive revision was unavailable after verification.");
        return (revisionId, revisionId == proposed);
    }

    private static async Task UpsertObservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetId assetId,
        long observedSize,
        DateTimeOffset observedWrite,
        string observedMedia,
        DateTimeOffset observedAt,
        ArchiveSourceObservationVerificationState verificationState,
        AssetRevisionId? verifiedRevisionId,
        long? verifiedSize,
        DateTimeOffset? verifiedWrite,
        string? verifiedMedia,
        DateTimeOffset? verifiedAt,
        CancellationToken cancellationToken)
    {
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO archive_source_observations (
                asset_id,
                observed_size_bytes,
                observed_last_write_utc,
                observed_media_type,
                observed_at_utc,
                verification_state,
                verified_revision_id,
                verified_size_bytes,
                verified_last_write_utc,
                verified_media_type,
                verified_at_utc, observed_last_write_ticks, verified_last_write_ticks)
            VALUES (
                @asset_id,
                @observed_size_bytes,
                @observed_last_write_utc,
                @observed_media_type,
                @observed_at_utc,
                @verification_state,
                @verified_revision_id,
                @verified_size_bytes,
                @verified_last_write_utc,
                @verified_media_type,
                @verified_at_utc, @observed_last_write_ticks, @verified_last_write_ticks)
            ON CONFLICT(asset_id) DO UPDATE SET
                observed_size_bytes = excluded.observed_size_bytes,
                observed_last_write_utc = excluded.observed_last_write_utc,
                observed_media_type = excluded.observed_media_type,
                observed_at_utc = excluded.observed_at_utc,
                verification_state = excluded.verification_state,
                verified_revision_id = excluded.verified_revision_id,
                verified_size_bytes = excluded.verified_size_bytes,
                verified_last_write_utc = excluded.verified_last_write_utc,
                verified_media_type = excluded.verified_media_type,
                verified_at_utc = excluded.verified_at_utc,
                observed_last_write_ticks = excluded.observed_last_write_ticks,
                verified_last_write_ticks = excluded.verified_last_write_ticks;
            """;
        command.Parameters.AddWithValue("@asset_id", Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue("@observed_size_bytes", observedSize);
        command.Parameters.AddWithValue("@observed_last_write_utc", Format(observedWrite));
        command.Parameters.AddWithValue("@observed_media_type", observedMedia);
        command.Parameters.AddWithValue("@observed_at_utc", Format(observedAt));
        command.Parameters.AddWithValue("@verification_state", ToStorageValue(verificationState));
        command.Parameters.AddWithValue("@verified_revision_id", NpgsqlDbType.Uuid, verifiedRevisionId is AssetRevisionId revision ? Guid.Parse(revision.ToString()) : DBNull.Value);
        command.Parameters.AddWithValue("@verified_size_bytes", NpgsqlDbType.Bigint, (object?)verifiedSize ?? DBNull.Value);
        command.Parameters.AddWithValue("@verified_last_write_utc", NpgsqlDbType.TimestampTz, (object?)(verifiedWrite is null ? null : Format(verifiedWrite.Value)) ?? DBNull.Value);
        command.Parameters.AddWithValue("@verified_media_type", NpgsqlDbType.Text, (object?)verifiedMedia ?? DBNull.Value);
        command.Parameters.AddWithValue("@verified_at_utc", NpgsqlDbType.TimestampTz, (object?)(verifiedAt is null ? null : Format(verifiedAt.Value)) ?? DBNull.Value);
        command.Parameters.AddWithValue("observed_last_write_ticks", observedWrite.UtcTicks);
        command.Parameters.AddWithValue("verified_last_write_ticks", NpgsqlTypes.NpgsqlDbType.Bigint, (object?)verifiedWrite?.UtcTicks ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> MarkMissingAssetsAsync(
        SourceId sourceId,
        string? relativeRoot,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken)
    {
        string scope = ArchiveCoverage.NormalizeRelativeFolder(relativeRoot ?? string.Empty);
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = scope.Length == 0
            ? """
              UPDATE assets
              SET deleted_at_utc = COALESCE(deleted_at_utc, @scanned_at_utc)
              WHERE source_id = @source_id
                AND (last_seen_at_utc IS NULL OR last_seen_at_utc <> @scanned_at_utc)
                AND deleted_at_utc IS NULL;
              """
            : """
              UPDATE assets
              SET deleted_at_utc = COALESCE(deleted_at_utc, @scanned_at_utc)
              WHERE source_id = @source_id
                AND substr(source_key, 1, length(@scope_prefix)) = @scope_prefix
                AND (last_seen_at_utc IS NULL OR last_seen_at_utc <> @scanned_at_utc)
                AND deleted_at_utc IS NULL;
              """;
        command.Parameters.AddWithValue("@source_id", Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("@scanned_at_utc", Format(scannedAtUtc));
        if (scope.Length > 0)
        {
            command.Parameters.AddWithValue("@scope_prefix", scope + "/");
        }

        int updated = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    private static DateTimeOffset Format(DateTimeOffset value) => value.ToUniversalTime();


    private static ArchiveSourceObservationVerificationState ParseVerificationState(string value) => value switch
    {
        "verified" => ArchiveSourceObservationVerificationState.Verified,
        "needs-source-verification" => ArchiveSourceObservationVerificationState.NeedsSourceVerification,
        "unverified" => ArchiveSourceObservationVerificationState.Unverified,
        _ => throw new InvalidDataException("Unknown source verification state."),
    };

    private static string ToStorageValue(ArchiveSourceObservationVerificationState value) => value switch
    {
        ArchiveSourceObservationVerificationState.Verified => "verified",
        ArchiveSourceObservationVerificationState.NeedsSourceVerification => "needs-source-verification",
        ArchiveSourceObservationVerificationState.Unverified => "unverified",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string ToStorageValue(AssetAvailability value) => value switch
    {
        AssetAvailability.Local => "local",
        AssetAvailability.OnlineOnly => "online-only",
        AssetAvailability.Downloading => "downloading",
        AssetAvailability.Unavailable => "unavailable",
        AssetAvailability.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
