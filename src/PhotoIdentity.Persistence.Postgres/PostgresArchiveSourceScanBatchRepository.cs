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

        int[] ordinals = Enumerable.Range(0, writes.Count).ToArray();
        Guid[] proposedAssetIds = writes.Select(static _ => Guid.NewGuid()).ToArray();
        Guid[] proposedRevisionIds = writes.Select(static _ => Guid.NewGuid()).ToArray();
        string[] sourceKeys = writes.Select(static write => write.SourceAsset.Reference.ItemKey).ToArray();
        string[] availability = writes.Select(static write => ToStorageValue(write.SourceAsset.Availability)).ToArray();
        long[] sizes = writes.Select(static write => write.SourceAsset.SizeBytes).ToArray();
        DateTimeOffset[] lastWrites = writes.Select(static write => write.SourceAsset.LastWriteTimeUtc.ToUniversalTime()).ToArray();
        long[] lastWriteTicks = lastWrites.Select(static value => value.UtcTicks).ToArray();
        string[] mediaTypes = writes.Select(static write => write.SourceAsset.MediaType.Trim()).ToArray();
        string?[] hashes = writes.Select(static write => write.VerifiedContentHash?.ToString()).ToArray();

        using (NpgsqlCommand stage = connection.CreateCommand())
        {
            stage.Transaction = transaction;
            stage.CommandText = """
                CREATE TEMP TABLE archive_source_scan_stage (
                    ordinal integer PRIMARY KEY,
                    proposed_asset_id uuid NOT NULL,
                    proposed_revision_id uuid NOT NULL,
                    source_key text NOT NULL UNIQUE,
                    availability text NOT NULL,
                    size_bytes bigint NOT NULL,
                    last_write_utc timestamp with time zone NOT NULL,
                    last_write_ticks bigint NOT NULL,
                    media_type text NOT NULL,
                    content_sha256 text NULL
                ) ON COMMIT DROP;

                INSERT INTO archive_source_scan_stage (
                    ordinal,
                    proposed_asset_id,
                    proposed_revision_id,
                    source_key,
                    availability,
                    size_bytes,
                    last_write_utc,
                    last_write_ticks,
                    media_type,
                    content_sha256)
                SELECT *
                FROM unnest(
                    @ordinals,
                    @proposed_asset_ids,
                    @proposed_revision_ids,
                    @source_keys,
                    @availability,
                    @sizes,
                    @last_writes,
                    @last_write_ticks,
                    @media_types,
                    @hashes);
                """;
            stage.Parameters.AddWithValue("ordinals", NpgsqlDbType.Array | NpgsqlDbType.Integer, ordinals);
            stage.Parameters.AddWithValue("proposed_asset_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, proposedAssetIds);
            stage.Parameters.AddWithValue("proposed_revision_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, proposedRevisionIds);
            stage.Parameters.AddWithValue("source_keys", NpgsqlDbType.Array | NpgsqlDbType.Text, sourceKeys);
            stage.Parameters.AddWithValue("availability", NpgsqlDbType.Array | NpgsqlDbType.Text, availability);
            stage.Parameters.AddWithValue("sizes", NpgsqlDbType.Array | NpgsqlDbType.Bigint, sizes);
            stage.Parameters.AddWithValue("last_writes", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz, lastWrites);
            stage.Parameters.AddWithValue("last_write_ticks", NpgsqlDbType.Array | NpgsqlDbType.Bigint, lastWriteTicks);
            stage.Parameters.AddWithValue("media_types", NpgsqlDbType.Array | NpgsqlDbType.Text, mediaTypes);
            stage.Parameters.AddWithValue("hashes", NpgsqlDbType.Array | NpgsqlDbType.Text, hashes);
            await stage.ExecuteNonQueryAsync(cancellationToken);
        }

        // Snapshot the authoritative state before clearing deletion markers. This refreshes the
        // scanner's earlier baseline read inside the serializable write transaction.
        using (NpgsqlCommand capture = connection.CreateCommand())
        {
            capture.Transaction = transaction;
            capture.CommandText = """
                CREATE TEMP TABLE archive_source_scan_existing ON COMMIT DROP AS
                SELECT
                    stage.ordinal,
                    asset.id AS asset_id,
                    asset.deleted_at_utc IS NOT NULL AS was_deleted,
                    observation.verification_state,
                    observation.verified_revision_id,
                    observation.verified_size_bytes,
                    observation.verified_last_write_utc,
                    observation.verified_last_write_ticks,
                    observation.verified_media_type,
                    observation.verified_at_utc,
                    latest.id AS latest_revision_id
                FROM archive_source_scan_stage AS stage
                LEFT JOIN assets AS asset
                    ON asset.source_id = @source_id
                   AND asset.source_key = stage.source_key
                LEFT JOIN archive_source_observations AS observation
                    ON observation.asset_id = asset.id
                LEFT JOIN LATERAL (
                    SELECT revision.id
                    FROM asset_revisions AS revision
                    WHERE revision.asset_id = asset.id
                    ORDER BY revision.observed_at_utc DESC, revision.id DESC
                    LIMIT 1
                ) AS latest ON TRUE;

                CREATE UNIQUE INDEX archive_source_scan_existing_ordinal
                    ON archive_source_scan_existing (ordinal);
                """;
            capture.Parameters.AddWithValue("source_id", source.SourceId.Value);
            await capture.ExecuteNonQueryAsync(cancellationToken);
        }

        using (NpgsqlCommand assets = connection.CreateCommand())
        {
            assets.Transaction = transaction;
            assets.CommandText = """
                INSERT INTO assets (
                    id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc)
                SELECT
                    stage.proposed_asset_id,
                    @source_id,
                    stage.source_key,
                    @scanned_at_utc,
                    @scanned_at_utc,
                    NULL
                FROM archive_source_scan_stage AS stage
                ON CONFLICT(source_id, source_key) DO UPDATE SET
                    last_seen_at_utc = excluded.last_seen_at_utc,
                    deleted_at_utc = NULL;
                """;
            assets.Parameters.AddWithValue("source_id", source.SourceId.Value);
            assets.Parameters.AddWithValue("scanned_at_utc", scannedAt);
            await assets.ExecuteNonQueryAsync(cancellationToken);
        }

        using (NpgsqlCommand availabilityWrite = connection.CreateCommand())
        {
            availabilityWrite.Transaction = transaction;
            availabilityWrite.CommandText = """
                INSERT INTO archive_asset_availability (asset_id, availability, checked_at_utc)
                SELECT asset.id, stage.availability, @scanned_at_utc
                FROM archive_source_scan_stage AS stage
                INNER JOIN assets AS asset
                    ON asset.source_id = @source_id
                   AND asset.source_key = stage.source_key
                ON CONFLICT(asset_id) DO UPDATE SET
                    availability = excluded.availability,
                    checked_at_utc = excluded.checked_at_utc;
                """;
            availabilityWrite.Parameters.AddWithValue("source_id", source.SourceId.Value);
            availabilityWrite.Parameters.AddWithValue("scanned_at_utc", scannedAt);
            await availabilityWrite.ExecuteNonQueryAsync(cancellationToken);
        }

        using (NpgsqlCommand revisions = connection.CreateCommand())
        {
            revisions.Transaction = transaction;
            revisions.CommandText = """
                INSERT INTO asset_revisions (
                    id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
                SELECT
                    stage.proposed_revision_id,
                    asset.id,
                    stage.content_sha256,
                    stage.size_bytes,
                    @scanned_at_utc,
                    stage.media_type,
                    NULL,
                    NULL
                FROM archive_source_scan_stage AS stage
                INNER JOIN assets AS asset
                    ON asset.source_id = @source_id
                   AND asset.source_key = stage.source_key
                WHERE stage.content_sha256 IS NOT NULL
                ON CONFLICT(asset_id, content_sha256) DO UPDATE SET
                    size_bytes = excluded.size_bytes,
                    observed_at_utc = excluded.observed_at_utc,
                    media_type = excluded.media_type;
                """;
            revisions.Parameters.AddWithValue("source_id", source.SourceId.Value);
            revisions.Parameters.AddWithValue("scanned_at_utc", scannedAt);
            await revisions.ExecuteNonQueryAsync(cancellationToken);
        }

        using (NpgsqlCommand observations = connection.CreateCommand())
        {
            observations.Transaction = transaction;
            observations.CommandText = """
                WITH resolved AS (
                    SELECT
                        stage.*,
                        asset.id AS asset_id,
                        existing.was_deleted,
                        existing.verification_state AS previous_verification_state,
                        existing.verified_revision_id AS previous_verified_revision_id,
                        existing.verified_size_bytes AS previous_verified_size_bytes,
                        existing.verified_last_write_utc AS previous_verified_last_write_utc,
                        existing.verified_last_write_ticks AS previous_verified_last_write_ticks,
                        existing.verified_media_type AS previous_verified_media_type,
                        existing.verified_at_utc AS previous_verified_at_utc,
                        existing.latest_revision_id,
                        hash_revision.id AS hash_revision_id
                    FROM archive_source_scan_stage AS stage
                    INNER JOIN assets AS asset
                        ON asset.source_id = @source_id
                       AND asset.source_key = stage.source_key
                    INNER JOIN archive_source_scan_existing AS existing
                        ON existing.ordinal = stage.ordinal
                    LEFT JOIN asset_revisions AS hash_revision
                        ON hash_revision.asset_id = asset.id
                       AND hash_revision.content_sha256 = stage.content_sha256
                ), projected AS (
                    SELECT
                        resolved.*,
                        CASE
                            WHEN content_sha256 IS NOT NULL THEN 'verified'
                            WHEN was_deleted AND latest_revision_id IS NOT NULL THEN 'needs-source-verification'
                            WHEN previous_verification_state = 'needs-source-verification' THEN 'needs-source-verification'
                            WHEN previous_verified_revision_id IS NOT NULL
                             AND previous_verified_size_bytes IS NOT NULL
                             AND previous_verified_last_write_utc IS NOT NULL
                             AND previous_verified_media_type IS NOT NULL
                                THEN CASE
                                    WHEN previous_verified_size_bytes = size_bytes
                                     AND (
                                         previous_verified_last_write_ticks = last_write_ticks
                                         OR (previous_verified_last_write_ticks IS NULL
                                             AND previous_verified_last_write_utc = last_write_utc))
                                     AND previous_verified_media_type = media_type
                                        THEN 'verified'
                                    ELSE 'needs-source-verification'
                                END
                            WHEN latest_revision_id IS NOT NULL THEN 'needs-source-verification'
                            ELSE 'unverified'
                        END AS next_verification_state,
                        CASE
                            WHEN content_sha256 IS NOT NULL THEN hash_revision_id
                            ELSE COALESCE(previous_verified_revision_id, latest_revision_id)
                        END AS next_verified_revision_id
                    FROM resolved
                )
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
                    verified_at_utc,
                    observed_last_write_ticks,
                    verified_last_write_ticks)
                SELECT
                    asset_id,
                    size_bytes,
                    last_write_utc,
                    media_type,
                    @scanned_at_utc,
                    next_verification_state,
                    next_verified_revision_id,
                    CASE WHEN content_sha256 IS NOT NULL THEN size_bytes ELSE previous_verified_size_bytes END,
                    CASE WHEN content_sha256 IS NOT NULL THEN last_write_utc ELSE previous_verified_last_write_utc END,
                    CASE WHEN content_sha256 IS NOT NULL THEN media_type ELSE previous_verified_media_type END,
                    CASE WHEN content_sha256 IS NOT NULL THEN @scanned_at_utc ELSE previous_verified_at_utc END,
                    last_write_ticks,
                    CASE WHEN content_sha256 IS NOT NULL THEN last_write_ticks ELSE previous_verified_last_write_ticks END
                FROM projected
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
            observations.Parameters.AddWithValue("source_id", source.SourceId.Value);
            observations.Parameters.AddWithValue("scanned_at_utc", scannedAt);
            await observations.ExecuteNonQueryAsync(cancellationToken);
        }

        List<ArchiveSourceObservationPersistenceResult> results = new(writes.Count);
        using (NpgsqlCommand readResults = connection.CreateCommand())
        {
            readResults.Transaction = transaction;
            readResults.CommandText = """
                SELECT
                    asset.id,
                    observation.verified_revision_id,
                    stage.content_sha256 IS NOT NULL
                        AND hash_revision.id = stage.proposed_revision_id AS new_revision,
                    observation.verification_state
                FROM archive_source_scan_stage AS stage
                INNER JOIN assets AS asset
                    ON asset.source_id = @source_id
                   AND asset.source_key = stage.source_key
                INNER JOIN archive_source_observations AS observation
                    ON observation.asset_id = asset.id
                LEFT JOIN asset_revisions AS hash_revision
                    ON hash_revision.asset_id = asset.id
                   AND hash_revision.content_sha256 = stage.content_sha256
                ORDER BY stage.ordinal;
                """;
            readResults.Parameters.AddWithValue("source_id", source.SourceId.Value);
            await using NpgsqlDataReader reader = await readResults.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new ArchiveSourceObservationPersistenceResult(
                    AssetId.From(reader.GetGuid(0)),
                    reader.IsDBNull(1) ? null : AssetRevisionId.From(reader.GetGuid(1)),
                    reader.GetBoolean(2),
                    ParseVerificationState(reader.GetString(3))));
            }
        }

        if (results.Count != writes.Count)
        {
            throw new InvalidOperationException("Archive scan batch did not persist every observation.");
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

    public async Task<int> MarkMissingAssetsAsync(
        SourceId sourceId,
        string? relativeRoot,
        IReadOnlyCollection<string> observedSourceKeys,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observedSourceKeys);

        string scope = ArchiveCoverage.NormalizeRelativeFolder(relativeRoot ?? string.Empty);
        string[] observedKeys = observedSourceKeys
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await using NpgsqlConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = scope.Length == 0
            ? """
              UPDATE assets
              SET deleted_at_utc = COALESCE(deleted_at_utc, @scanned_at_utc)
              WHERE source_id = @source_id
                AND NOT (source_key = ANY(@observed_source_keys))
                AND deleted_at_utc IS NULL;
              """
            : """
              UPDATE assets
              SET deleted_at_utc = COALESCE(deleted_at_utc, @scanned_at_utc)
              WHERE source_id = @source_id
                AND substr(source_key, 1, length(@scope_prefix)) = @scope_prefix
                AND NOT (source_key = ANY(@observed_source_keys))
                AND deleted_at_utc IS NULL;
              """;
        command.Parameters.AddWithValue("@source_id", Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("@scanned_at_utc", Format(scannedAtUtc));
        command.Parameters.AddWithValue(
            "@observed_source_keys",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            observedKeys);
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
