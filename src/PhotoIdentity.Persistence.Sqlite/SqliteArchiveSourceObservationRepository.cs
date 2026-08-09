using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Sqlite;

public enum ArchiveSourceVerificationState
{
    Verified,
    NeedsSourceVerification,
    Unverified,
}

public sealed record ArchiveSourceObservation(
    AssetId AssetId,
    SourceId SourceId,
    string RootLocator,
    string SourceKey,
    long ObservedSizeBytes,
    DateTimeOffset ObservedLastWriteTimeUtc,
    string MediaType,
    DateTimeOffset ObservedAtUtc,
    AssetAvailability Availability,
    ArchiveSourceVerificationState VerificationState,
    AssetRevisionId? VerifiedRevisionId,
    DateTimeOffset? VerifiedAtUtc);

public sealed record ArchiveSourceObservationWriteResult(
    AssetId AssetId,
    AssetRevisionId? RevisionId,
    bool NewRevision,
    ArchiveSourceVerificationState VerificationState);

public sealed record ArchiveSourceVerificationWriteResult(
    AssetRevisionId RevisionId,
    bool NewRevision);

/// <summary>
/// Persists lightweight archive source observations independently of immutable content revisions.
/// Metadata divergence can require verification, but only a local SHA-256 read may establish or
/// change the current immutable revision.
/// </summary>
public sealed class SqliteArchiveSourceObservationRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveSourceObservationRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await new SqliteArchiveAvailabilityRepository(_database).EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS archive_source_observations (
                asset_id TEXT NOT NULL PRIMARY KEY,
                observed_size_bytes INTEGER NOT NULL CHECK (observed_size_bytes >= 0),
                observed_last_write_utc TEXT NOT NULL,
                observed_media_type TEXT NOT NULL,
                observed_at_utc TEXT NOT NULL,
                verification_state TEXT NOT NULL CHECK (
                    verification_state IN ('verified', 'needs-source-verification', 'unverified')),
                verified_revision_id TEXT NULL,
                verified_size_bytes INTEGER NULL CHECK (verified_size_bytes IS NULL OR verified_size_bytes >= 0),
                verified_last_write_utc TEXT NULL,
                verified_media_type TEXT NULL,
                verified_at_utc TEXT NULL,
                FOREIGN KEY (asset_id) REFERENCES assets (id) ON DELETE CASCADE,
                FOREIGN KEY (verified_revision_id) REFERENCES asset_revisions (id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS ix_archive_source_observations_verification
                ON archive_source_observations (verification_state, observed_at_utc, asset_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ArchiveSourceObservationWriteResult> RecordScanObservationAsync(
        CatalogueSource source,
        SourceAsset sourceAsset,
        Sha256Digest? verifiedContentHash,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceAsset);
        await EnsureSchemaAsync(cancellationToken);

        DateTimeOffset scannedAt = scannedAtUtc.ToUniversalTime();
        DateTimeOffset observedWrite = sourceAsset.LastWriteTimeUtc.ToUniversalTime();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await UpsertSourceAsync(connection, transaction, source, cancellationToken);
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

        ExistingObservation? existing = await ReadExistingObservationAsync(
            connection,
            transaction,
            assetId,
            cancellationToken);
        AssetRevisionId? latestRevisionId = await ReadLatestRevisionIdAsync(
            connection,
            transaction,
            assetId,
            cancellationToken);

        bool newRevision = false;
        AssetRevisionId? revisionId = null;
        ArchiveSourceVerificationState verificationState;
        long? verifiedSize = existing?.VerifiedSizeBytes;
        DateTimeOffset? verifiedWrite = existing?.VerifiedLastWriteTimeUtc;
        string? verifiedMediaType = existing?.VerifiedMediaType;
        DateTimeOffset? verifiedAt = existing?.VerifiedAtUtc;
        AssetRevisionId? verifiedRevisionId = existing?.VerifiedRevisionId ?? latestRevisionId;

        if (verifiedContentHash is Sha256Digest contentHash)
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
        else if (existing?.VerificationState == ArchiveSourceVerificationState.NeedsSourceVerification)
        {
            // Once divergence has been observed, metadata alone may not clear the requirement.
            verificationState = ArchiveSourceVerificationState.NeedsSourceVerification;
        }
        else if (existing is not null &&
            existing.VerifiedRevisionId is not null &&
            existing.VerifiedSizeBytes is long baselineSize &&
            existing.VerifiedLastWriteTimeUtc is DateTimeOffset baselineWrite &&
            existing.VerifiedMediaType is string baselineMedia)
        {
            bool metadataMatches = baselineSize == sourceAsset.SizeBytes &&
                baselineWrite == observedWrite &&
                string.Equals(baselineMedia, sourceAsset.MediaType, StringComparison.Ordinal);
            verificationState = metadataMatches
                ? ArchiveSourceVerificationState.Verified
                : ArchiveSourceVerificationState.NeedsSourceVerification;
        }
        else if (latestRevisionId is not null)
        {
            // Legacy catalogues have a verified content revision but no retained lightweight
            // baseline. Failing closed forces one bounded re-verification rather than accepting
            // current placeholder metadata as proof of unchanged content.
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

        transaction.Commit();
        return new ArchiveSourceObservationWriteResult(
            assetId,
            revisionId ?? verifiedRevisionId,
            newRevision,
            verificationState);
    }

    public async Task<ArchiveSourceObservation?> GetNextPendingAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                asset.id,
                asset.source_id,
                source.root_locator,
                asset.source_key,
                observation.observed_size_bytes,
                observation.observed_last_write_utc,
                observation.observed_media_type,
                observation.observed_at_utc,
                COALESCE(availability.availability, 'local'),
                observation.verification_state,
                observation.verified_revision_id,
                observation.verified_at_utc
            FROM archive_source_observations AS observation
            INNER JOIN assets AS asset ON asset.id = observation.asset_id
            INNER JOIN sources AS source ON source.id = asset.source_id
            LEFT JOIN archive_asset_availability AS availability ON availability.asset_id = asset.id
            WHERE asset.source_id = $source_id
              AND asset.deleted_at_utc IS NULL
              AND observation.verification_state <> 'verified'
            ORDER BY
                CASE observation.verification_state
                    WHEN 'needs-source-verification' THEN 0
                    ELSE 1
                END,
                asset.source_key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadObservation(reader) : null;
    }

    public async Task<ArchiveSourceObservation?> GetAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                asset.id,
                asset.source_id,
                source.root_locator,
                asset.source_key,
                observation.observed_size_bytes,
                observation.observed_last_write_utc,
                observation.observed_media_type,
                observation.observed_at_utc,
                COALESCE(availability.availability, 'local'),
                observation.verification_state,
                observation.verified_revision_id,
                observation.verified_at_utc
            FROM archive_source_observations AS observation
            INNER JOIN assets AS asset ON asset.id = observation.asset_id
            INNER JOIN sources AS source ON source.id = asset.source_id
            LEFT JOIN archive_asset_availability AS availability ON availability.asset_id = asset.id
            WHERE asset.id = $asset_id;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadObservation(reader) : null;
    }

    public async Task<ArchiveSourceVerificationWriteResult> RecordVerifiedContentAsync(
        AssetId assetId,
        Sha256Digest contentHash,
        long sizeBytes,
        DateTimeOffset lastWriteTimeUtc,
        string mediaType,
        DateTimeOffset verifiedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        await EnsureSchemaAsync(cancellationToken);

        DateTimeOffset verifiedAt = verifiedAtUtc.ToUniversalTime();
        DateTimeOffset lastWrite = lastWriteTimeUtc.ToUniversalTime();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        (AssetRevisionId revisionId, bool newRevision) = await UpsertRevisionAsync(
            connection,
            transaction,
            assetId,
            contentHash,
            sizeBytes,
            mediaType,
            verifiedAt,
            cancellationToken);

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE archive_source_observations
                SET observed_size_bytes = $size_bytes,
                    observed_last_write_utc = $last_write_utc,
                    observed_media_type = $media_type,
                    observed_at_utc = $verified_at_utc,
                    verification_state = 'verified',
                    verified_revision_id = $verified_revision_id,
                    verified_size_bytes = $size_bytes,
                    verified_last_write_utc = $last_write_utc,
                    verified_media_type = $media_type,
                    verified_at_utc = $verified_at_utc
                WHERE asset_id = $asset_id;
                """;
            command.Parameters.AddWithValue("$asset_id", assetId.ToString());
            command.Parameters.AddWithValue("$size_bytes", sizeBytes);
            command.Parameters.AddWithValue("$last_write_utc", Format(lastWrite));
            command.Parameters.AddWithValue("$media_type", mediaType);
            command.Parameters.AddWithValue("$verified_at_utc", Format(verifiedAt));
            command.Parameters.AddWithValue("$verified_revision_id", revisionId.ToString());
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Archive source observation disappeared before verification could be committed.");
            }
        }

        await UpsertAvailabilityAsync(
            connection,
            transaction,
            assetId,
            AssetAvailability.Local,
            verifiedAt,
            cancellationToken);
        transaction.Commit();
        return new ArchiveSourceVerificationWriteResult(revisionId, newRevision);
    }

    private static ArchiveSourceObservation ReadObservation(SqliteDataReader reader) => new(
        AssetId.From(Guid.Parse(reader.GetString(0))),
        SourceId.From(Guid.Parse(reader.GetString(1))),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetInt64(4),
        Parse(reader.GetString(5)),
        reader.GetString(6),
        Parse(reader.GetString(7)),
        ParseAvailability(reader.GetString(8)),
        ParseVerificationState(reader.GetString(9)),
        reader.IsDBNull(10) ? null : AssetRevisionId.From(Guid.Parse(reader.GetString(10))),
        reader.IsDBNull(11) ? null : Parse(reader.GetString(11)));

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
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc, deleted_at_utc)
                VALUES ($id, $source_id, $source_key, $created_at_utc, $last_seen_at_utc, NULL)
                ON CONFLICT(source_id, source_key) DO UPDATE SET
                    last_seen_at_utc = excluded.last_seen_at_utc,
                    deleted_at_utc = NULL;
                """;
            command.Parameters.AddWithValue("$id", proposed.ToString());
            command.Parameters.AddWithValue("$source_id", source.Id.ToString());
            command.Parameters.AddWithValue("$source_key", sourceKey);
            command.Parameters.AddWithValue("$created_at_utc", Format(observedAtUtc));
            command.Parameters.AddWithValue("$last_seen_at_utc", Format(observedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT id FROM assets WHERE source_id = $source_id AND source_key = $source_key;";
        read.Parameters.AddWithValue("$source_id", source.Id.ToString());
        read.Parameters.AddWithValue("$source_key", sourceKey);
        object? value = await read.ExecuteScalarAsync(cancellationToken);
        return value is string id
            ? AssetId.From(Guid.Parse(id))
            : throw new InvalidOperationException("Archive asset was unavailable after persistence.");
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
        int inserted;
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
            inserted = await command.ExecuteNonQueryAsync(cancellationToken);
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

        // SQLite reports one affected row for both INSERT and DO UPDATE. Compare identity instead.
        return (revisionId, revisionId == proposed);
    }

    private static async Task<AssetRevisionId?> ReadLatestRevisionIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetId assetId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id
            FROM asset_revisions
            WHERE asset_id = $asset_id
            ORDER BY observed_at_utc DESC, id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string id ? AssetRevisionId.From(Guid.Parse(id)) : null;
    }

    private static async Task<ExistingObservation?> ReadExistingObservationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetId assetId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                verification_state,
                verified_revision_id,
                verified_size_bytes,
                verified_last_write_utc,
                verified_media_type,
                verified_at_utc
            FROM archive_source_observations
            WHERE asset_id = $asset_id;
            """;
        command.Parameters.AddWithValue("$asset_id", assetId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ExistingObservation(
            ParseVerificationState(reader.GetString(0)),
            reader.IsDBNull(1) ? null : AssetRevisionId.From(Guid.Parse(reader.GetString(1))),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.IsDBNull(3) ? null : Parse(reader.GetString(3)),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : Parse(reader.GetString(5)));
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
        command.Parameters.AddWithValue("$verification_state", ToStorageValue(verificationState));
        command.Parameters.AddWithValue("$verified_revision_id", (object?)verifiedRevisionId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$verified_size_bytes", (object?)verifiedSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$verified_last_write_utc", (object?)(verifiedWrite is null ? null : Format(verifiedWrite.Value)) ?? DBNull.Value);
        command.Parameters.AddWithValue("$verified_media_type", (object?)verifiedMedia ?? DBNull.Value);
        command.Parameters.AddWithValue("$verified_at_utc", (object?)(verifiedAt is null ? null : Format(verifiedAt.Value)) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string ToStorageValue(ArchiveSourceVerificationState state) => state switch
    {
        ArchiveSourceVerificationState.Verified => "verified",
        ArchiveSourceVerificationState.NeedsSourceVerification => "needs-source-verification",
        ArchiveSourceVerificationState.Unverified => "unverified",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static ArchiveSourceVerificationState ParseVerificationState(string value) => value switch
    {
        "verified" => ArchiveSourceVerificationState.Verified,
        "needs-source-verification" => ArchiveSourceVerificationState.NeedsSourceVerification,
        "unverified" => ArchiveSourceVerificationState.Unverified,
        _ => throw new InvalidDataException($"Unknown archive source verification state '{value}'."),
    };

    private static AssetAvailability ParseAvailability(string value) => value switch
    {
        "local" => AssetAvailability.Local,
        "online-only" => AssetAvailability.OnlineOnly,
        "downloading" => AssetAvailability.Downloading,
        "unavailable" => AssetAvailability.Unavailable,
        "error" => AssetAvailability.Error,
        _ => throw new InvalidDataException($"Unknown archive availability state '{value}'."),
    };

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record ExistingObservation(
        ArchiveSourceVerificationState VerificationState,
        AssetRevisionId? VerifiedRevisionId,
        long? VerifiedSizeBytes,
        DateTimeOffset? VerifiedLastWriteTimeUtc,
        string? VerifiedMediaType,
        DateTimeOffset? VerifiedAtUtc);
}
