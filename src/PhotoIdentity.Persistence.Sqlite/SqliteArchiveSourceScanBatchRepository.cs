using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

public sealed record ArchiveSourceScanBaseline(
    string SourceKey,
    bool WasDeleted,
    ArchiveSourceVerificationState? VerificationState,
    AssetRevisionId? VerifiedRevisionId,
    long? VerifiedSizeBytes,
    DateTimeOffset? VerifiedLastWriteTimeUtc,
    string? VerifiedMediaType,
    DateTimeOffset? VerifiedAtUtc,
    AssetRevisionId? LatestRevisionId)
{
    public bool CanReuseVerifiedRevision(SourceAsset sourceAsset)
    {
        ArgumentNullException.ThrowIfNull(sourceAsset);
        return !WasDeleted &&
            VerificationState == ArchiveSourceVerificationState.Verified &&
            VerifiedRevisionId is not null &&
            VerifiedSizeBytes == sourceAsset.SizeBytes &&
            VerifiedLastWriteTimeUtc == sourceAsset.LastWriteTimeUtc.ToUniversalTime() &&
            string.Equals(VerifiedMediaType, sourceAsset.MediaType, StringComparison.Ordinal);
    }
}

public sealed record ArchiveSourceScanWrite(
    SourceAsset SourceAsset,
    Sha256Digest? VerifiedContentHash);

/// <summary>
/// Scan-specific persistence path for permanent archive synchronization. The scanner loads
/// lightweight verification baselines once, decides which local originals actually require
/// SHA-256 reads, then persists one included-folder batch in a single SQLite transaction.
/// </summary>
public sealed class SqliteArchiveSourceScanBatchRepository
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly SqliteArchiveSourceObservationRepository _observations;

    public SqliteArchiveSourceScanBatchRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _observations = new SqliteArchiveSourceObservationRepository(database);
    }

    public async Task<IReadOnlyDictionary<string, ArchiveSourceScanBaseline>> GetBaselinesAsync(
        SourceId sourceId,
        string? relativeRoot,
        CancellationToken cancellationToken = default)
    {
        await _observations.EnsureSchemaAsync(cancellationToken);
        string scope = ArchiveCoverage.NormalizeRelativeFolder(relativeRoot ?? string.Empty);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
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
                  ) AS latest_revision_id
              FROM assets AS asset
              LEFT JOIN archive_source_observations AS observation ON observation.asset_id = asset.id
              WHERE asset.source_id = $source_id;
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
                  ) AS latest_revision_id
              FROM assets AS asset
              LEFT JOIN archive_source_observations AS observation ON observation.asset_id = asset.id
              WHERE asset.source_id = $source_id
                AND substr(asset.source_key, 1, length($scope_prefix)) = $scope_prefix;
              """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        if (scope.Length > 0)
        {
            command.Parameters.AddWithValue("$scope_prefix", scope + "/");
        }

        Dictionary<string, ArchiveSourceScanBaseline> baselines = new(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string sourceKey = reader.GetString(0);
            baselines[sourceKey] = new ArchiveSourceScanBaseline(
                sourceKey,
                reader.GetInt64(1) != 0,
                reader.IsDBNull(2)
                    ? null
                    : SqliteArchiveSourceObservationRepository.ParseVerificationState(reader.GetString(2)),
                reader.IsDBNull(3) ? null : AssetRevisionId.From(Guid.Parse(reader.GetString(3))),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : AssetRevisionId.From(Guid.Parse(reader.GetString(8))));
        }

        return baselines;
    }

    public async Task<IReadOnlyList<ArchiveSourceObservationWriteResult>> RecordBatchAsync(
        CatalogueSource source,
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

        await _observations.EnsureSchemaAsync(cancellationToken);
        DateTimeOffset scannedAt = scannedAtUtc.ToUniversalTime();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        await UpsertSourceAsync(connection, transaction, source, cancellationToken);

        List<ArchiveSourceObservationWriteResult> results = new(writes.Count);
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
            ArchiveSourceVerificationState verificationState;
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
                verificationState = ArchiveSourceVerificationState.Verified;
                verifiedRevisionId = revisionId;
                verifiedSize = sourceAsset.SizeBytes;
                verifiedWrite = observedWrite;
                verifiedMediaType = sourceAsset.MediaType;
                verifiedAt = scannedAt;
            }
            else if (baseline?.WasDeleted == true && baseline.LatestRevisionId is not null)
            {
                verificationState = ArchiveSourceVerificationState.NeedsSourceVerification;
            }
            else if (baseline?.VerificationState == ArchiveSourceVerificationState.NeedsSourceVerification)
            {
                verificationState = ArchiveSourceVerificationState.NeedsSourceVerification;
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
                    ? ArchiveSourceVerificationState.Verified
                    : ArchiveSourceVerificationState.NeedsSourceVerification;
            }
            else if (baseline?.LatestRevisionId is not null)
            {
                verificationState = ArchiveSourceVerificationState.NeedsSourceVerification;
            }
            else
            {
                verificationState = ArchiveSourceVerificationState.Unverified;
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

            results.Add(new ArchiveSourceObservationWriteResult(
                assetId,
                revisionId ?? verifiedRevisionId,
                newRevision,
                verificationState));
        }

        transaction.Commit();
        return results;
    }

    private static async Task UpsertSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogueSource source,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES ($id, $kind, $root_locator, $created_at_utc)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                root_locator = excluded.root_locator;
            """;
        command.Parameters.AddWithValue("$id", source.Id.ToString());
        command.Parameters.AddWithValue("$kind", source.Kind);
        command.Parameters.AddWithValue("$root_locator", source.RootLocator);
        command.Parameters.AddWithValue("$created_at_utc", Format(source.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AssetId> UpsertAssetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogueSource source,
        string sourceKey,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        AssetId proposed = AssetId.New();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc)
            VALUES ($id, $source_id, $source_key, $created_at_utc, $last_seen_at_utc, NULL)
            ON CONFLICT(source_id, source_key) DO UPDATE SET
                last_seen_at_utc = excluded.last_seen_at_utc,
                deleted_at_utc = NULL
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$id", proposed.ToString());
        command.Parameters.AddWithValue("$source_id", source.Id.ToString());
        command.Parameters.AddWithValue("$source_key", sourceKey);
        command.Parameters.AddWithValue("$created_at_utc", Format(observedAtUtc));
        command.Parameters.AddWithValue("$last_seen_at_utc", Format(observedAtUtc));
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string id
            ? AssetId.From(Guid.Parse(id))
            : throw new InvalidOperationException("Archive asset was unavailable after batched persistence.");
    }

    private static async Task UpsertAvailabilityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetId assetId,
        AssetAvailability availability,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO archive_asset_availability (asset_id, availability, checked_at_utc)
            VALUES ($asset_id, $availability, $checked_at_utc)
            ON CONFLICT(asset_id) DO UPDATE SET
                availability = excluded.availability,
                checked_at_utc = excluded.checked_at_utc;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        command.Parameters.AddWithValue("$availability", SqliteArchiveAvailabilityRepository.ToStorageValue(availability));
        command.Parameters.AddWithValue("$checked_at_utc", Format(checkedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(AssetRevisionId RevisionId, bool NewRevision)> UpsertRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetId assetId,
        Sha256Digest contentHash,
        long sizeBytes,
        string mediaType,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        AssetRevisionId proposed = AssetRevisionId.New();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO asset_revisions (
                    id, asset_id, content_sha256, size_bytes, observed_at_utc, media_type, width, height)
                VALUES (
                    $id, $asset_id, $content_sha256, $size_bytes, $observed_at_utc, $media_type, NULL, NULL)
                ON CONFLICT(asset_id, content_sha256) DO UPDATE SET
                    size_bytes = excluded.size_bytes,
                    observed_at_utc = excluded.observed_at_utc,
                    media_type = excluded.media_type;
                """;
            command.Parameters.AddWithValue("$id", proposed.ToString());
            command.Parameters.AddWithValue("$asset_id", assetId.ToString());
            command.Parameters.AddWithValue("$content_sha256", contentHash.ToString());
            command.Parameters.AddWithValue("$size_bytes", sizeBytes);
            command.Parameters.AddWithValue("$observed_at_utc", Format(observedAtUtc));
            command.Parameters.AddWithValue("$media_type", mediaType);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT id
            FROM asset_revisions
            WHERE asset_id = $asset_id AND content_sha256 = $content_sha256;
            """;
        read.Parameters.AddWithValue("$asset_id", assetId.ToString());
        read.Parameters.AddWithValue("$content_sha256", contentHash.ToString());
        object? value = await read.ExecuteScalarAsync(cancellationToken);
        AssetRevisionId revisionId = value is string id
            ? AssetRevisionId.From(Guid.Parse(id))
            : throw new InvalidOperationException("Archive revision was unavailable after verification.");
        return (revisionId, revisionId == proposed);
    }

    private static async Task UpsertObservationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetId assetId,
        long observedSize,
        DateTimeOffset observedWrite,
        string observedMedia,
        DateTimeOffset observedAt,
        ArchiveSourceVerificationState verificationState,
        AssetRevisionId? verifiedRevisionId,
        long? verifiedSize,
        DateTimeOffset? verifiedWrite,
        string? verifiedMedia,
        DateTimeOffset? verifiedAt,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
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
                verified_at_utc)
            VALUES (
                $asset_id,
                $observed_size_bytes,
                $observed_last_write_utc,
                $observed_media_type,
                $observed_at_utc,
                $verification_state,
                $verified_revision_id,
                $verified_size_bytes,
                $verified_last_write_utc,
                $verified_media_type,
                $verified_at_utc)
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
                verified_at_utc = excluded.verified_at_utc;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        command.Parameters.AddWithValue("$observed_size_bytes", observedSize);
        command.Parameters.AddWithValue("$observed_last_write_utc", Format(observedWrite));
        command.Parameters.AddWithValue("$observed_media_type", observedMedia);
        command.Parameters.AddWithValue("$observed_at_utc", Format(observedAt));
        command.Parameters.AddWithValue("$verification_state", SqliteArchiveSourceObservationRepository.ToStorageValue(verificationState));
        command.Parameters.AddWithValue("$verified_revision_id", (object?)verifiedRevisionId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$verified_size_bytes", (object?)verifiedSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$verified_last_write_utc", (object?)(verifiedWrite is null ? null : Format(verifiedWrite.Value)) ?? DBNull.Value);
        command.Parameters.AddWithValue("$verified_media_type", (object?)verifiedMedia ?? DBNull.Value);
        command.Parameters.AddWithValue("$verified_at_utc", (object?)(verifiedAt is null ? null : Format(verifiedAt.Value)) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
