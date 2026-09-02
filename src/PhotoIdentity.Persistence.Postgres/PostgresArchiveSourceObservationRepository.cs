using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Persists archive source observations and content-verification baselines in PostgreSQL.
/// </summary>
public sealed class PostgresArchiveSourceObservationRepository : IArchiveSourceObservationRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveSourceObservationRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<ArchiveSourceObservationPersistenceResult> RecordScanObservationAsync(
        ArchiveCatalogueSource source,
        SourceAsset sourceAsset,
        Sha256Digest? verifiedContentHash,
        DateTimeOffset scannedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceAsset);

        DateTimeOffset scannedAt = scannedAtUtc.ToUniversalTime();
        DateTimeOffset observedWrite = sourceAsset.LastWriteTimeUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await UpsertSourceAsync(connection, transaction, source, cancellationToken);
        AssetId assetId = await UpsertAssetAsync(
            connection,
            transaction,
            source.SourceId,
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
        ArchiveSourceObservationVerificationState verificationState;
        long? verifiedSize = existing?.VerifiedSizeBytes;
        DateTimeOffset? verifiedWrite = existing?.VerifiedLastWriteTimeUtc;
        string? verifiedMediaType = existing?.VerifiedMediaType;
        DateTimeOffset? verifiedAt = existing?.VerifiedAtUtc;
        AssetRevisionId? verifiedRevisionId =
            existing?.VerifiedRevisionId ?? latestRevisionId;

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
            verificationState = ArchiveSourceObservationVerificationState.Verified;
            verifiedRevisionId = revisionId;
            verifiedSize = sourceAsset.SizeBytes;
            verifiedWrite = observedWrite;
            verifiedMediaType = sourceAsset.MediaType;
            verifiedAt = scannedAt;
        }
        else if (existing?.VerificationState ==
                 ArchiveSourceObservationVerificationState.NeedsSourceVerification)
        {
            verificationState =
                ArchiveSourceObservationVerificationState.NeedsSourceVerification;
        }
        else if (existing is not null &&
                 existing.VerifiedRevisionId is not null &&
                 existing.VerifiedSizeBytes is long baselineSize &&
                 existing.VerifiedLastWriteTimeUtc is DateTimeOffset baselineWrite &&
                 existing.VerifiedMediaType is string baselineMedia)
        {
            bool metadataMatches =
                baselineSize == sourceAsset.SizeBytes &&
                baselineWrite == observedWrite &&
                string.Equals(
                    baselineMedia,
                    sourceAsset.MediaType,
                    StringComparison.Ordinal);
            verificationState = metadataMatches
                ? ArchiveSourceObservationVerificationState.Verified
                : ArchiveSourceObservationVerificationState.NeedsSourceVerification;
        }
        else if (latestRevisionId is not null)
        {
            verificationState =
                ArchiveSourceObservationVerificationState.NeedsSourceVerification;
        }
        else
        {
            verificationState =
                ArchiveSourceObservationVerificationState.Unverified;
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

        await transaction.CommitAsync(cancellationToken);
        return new ArchiveSourceObservationPersistenceResult(
            assetId,
            revisionId ?? verifiedRevisionId,
            newRevision,
            verificationState);
    }

    public Task<ArchiveSourceObservationSnapshot?> GetNextPendingAsync(
        SourceId sourceId,
        CancellationToken cancellationToken = default) =>
        ReadObservationAsync(
            """
            WHERE asset.source_id = @id
              AND asset.deleted_at_utc IS NULL
              AND observation.verification_state <> 'verified'
            ORDER BY
                CASE observation.verification_state
                    WHEN 'needs-source-verification' THEN 0
                    ELSE 1
                END,
                asset.source_key
            LIMIT 1
            """,
            Guid.Parse(sourceId.ToString()),
            cancellationToken);

    public Task<ArchiveSourceObservationSnapshot?> GetAsync(
        AssetId assetId,
        CancellationToken cancellationToken = default) =>
        ReadObservationAsync(
            "WHERE asset.id = @id",
            Guid.Parse(assetId.ToString()),
            cancellationToken);

    public async Task<ArchiveSourceVerificationPersistenceResult> RecordVerifiedContentAsync(
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

        DateTimeOffset verifiedAt = verifiedAtUtc.ToUniversalTime();
        DateTimeOffset lastWrite = lastWriteTimeUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        (AssetRevisionId revisionId, bool newRevision) =
            await UpsertRevisionAsync(
                connection,
                transaction,
                assetId,
                contentHash,
                sizeBytes,
                mediaType,
                verifiedAt,
                cancellationToken);

        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE archive_source_observations
                SET observed_size_bytes = @size_bytes,
                    observed_last_write_utc = @last_write_utc,
                    observed_media_type = @media_type,
                    observed_at_utc = @verified_at_utc,
                    verification_state = 'verified',
                    verified_revision_id = @verified_revision_id,
                    verified_size_bytes = @size_bytes,
                    verified_last_write_utc = @last_write_utc,
                    verified_media_type = @media_type,
                    verified_at_utc = @verified_at_utc
                WHERE asset_id = @asset_id;
                """;
            command.Parameters.AddWithValue(
                "asset_id",
                Guid.Parse(assetId.ToString()));
            command.Parameters.AddWithValue("size_bytes", sizeBytes);
            command.Parameters.AddWithValue("last_write_utc", lastWrite);
            command.Parameters.AddWithValue("media_type", mediaType.Trim());
            command.Parameters.AddWithValue("verified_at_utc", verifiedAt);
            command.Parameters.AddWithValue(
                "verified_revision_id",
                Guid.Parse(revisionId.ToString()));

            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException(
                    "Archive source observation disappeared before verification could be committed.");
            }
        }

        await UpsertAvailabilityAsync(
            connection,
            transaction,
            assetId,
            AssetAvailability.Local,
            verifiedAt,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ArchiveSourceVerificationPersistenceResult(
            revisionId,
            newRevision);
    }

    private async Task<ArchiveSourceObservationSnapshot?> ReadObservationAsync(
        string predicate,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            $"""
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
            LEFT JOIN archive_asset_availability AS availability
                ON availability.asset_id = asset.id
            {predicate};
            """;
        command.Parameters.AddWithValue("id", id);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ArchiveSourceObservationSnapshot(
            AssetId.From(reader.GetGuid(0)),
            SourceId.From(reader.GetGuid(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            ParseAvailability(reader.GetString(8)),
            ParseVerificationState(reader.GetString(9)),
            reader.IsDBNull(10)
                ? null
                : AssetRevisionId.From(reader.GetGuid(10)),
            reader.IsDBNull(11)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(11));
    }

    private static async Task UpsertSourceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ArchiveCatalogueSource source,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
            VALUES (@id, @kind, @root_locator, @created_at_utc)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind,
                root_locator = excluded.root_locator;
            """;
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(source.SourceId.ToString()));
        command.Parameters.AddWithValue("kind", source.Kind);
        command.Parameters.AddWithValue("root_locator", source.RootLocator);
        command.Parameters.AddWithValue(
            "created_at_utc",
            source.CreatedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AssetId> UpsertAssetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SourceId sourceId,
        string sourceKey,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        AssetId proposed = AssetId.New();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO assets (
                id,
                source_id,
                source_key,
                created_at_utc,
                last_seen_at_utc,
                deleted_at_utc)
            VALUES (
                @id,
                @source_id,
                @source_key,
                @created_at_utc,
                @last_seen_at_utc,
                NULL)
            ON CONFLICT(source_id, source_key) DO UPDATE SET
                last_seen_at_utc = excluded.last_seen_at_utc,
                deleted_at_utc = NULL
            RETURNING id;
            """;
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(proposed.ToString()));
        command.Parameters.AddWithValue(
            "source_id",
            Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue("source_key", sourceKey);
        command.Parameters.AddWithValue("created_at_utc", observedAtUtc);
        command.Parameters.AddWithValue("last_seen_at_utc", observedAtUtc);

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id
            ? AssetId.From(id)
            : throw new InvalidOperationException(
                "Archive asset was unavailable after persistence.");
    }

    private static async Task UpsertAvailabilityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetId assetId,
        AssetAvailability availability,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO archive_asset_availability (
                asset_id,
                availability,
                checked_at_utc)
            VALUES (
                @asset_id,
                @availability,
                @checked_at_utc)
            ON CONFLICT(asset_id) DO UPDATE SET
                availability = excluded.availability,
                checked_at_utc = excluded.checked_at_utc;
            """;
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue(
            "availability",
            ToStorageValue(availability));
        command.Parameters.AddWithValue(
            "checked_at_utc",
            checkedAtUtc.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(AssetRevisionId RevisionId, bool NewRevision)>
        UpsertRevisionAsync(
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
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type,
                width,
                height)
            VALUES (
                @id,
                @asset_id,
                @content_sha256,
                @size_bytes,
                @observed_at_utc,
                @media_type,
                NULL,
                NULL)
            ON CONFLICT(asset_id, content_sha256) DO UPDATE SET
                size_bytes = excluded.size_bytes,
                observed_at_utc = excluded.observed_at_utc,
                media_type = excluded.media_type
            RETURNING id;
            """;
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(proposed.ToString()));
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue(
            "content_sha256",
            contentHash.ToString());
        command.Parameters.AddWithValue("size_bytes", sizeBytes);
        command.Parameters.AddWithValue(
            "observed_at_utc",
            observedAtUtc.ToUniversalTime());
        command.Parameters.AddWithValue("media_type", mediaType.Trim());

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not Guid id)
        {
            throw new InvalidOperationException(
                "Archive revision was unavailable after verification.");
        }

        AssetRevisionId revisionId = AssetRevisionId.From(id);
        return (revisionId, revisionId == proposed);
    }

    private static async Task<AssetRevisionId?> ReadLatestRevisionIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetId assetId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id
            FROM asset_revisions
            WHERE asset_id = @asset_id
            ORDER BY observed_at_utc DESC, id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? AssetRevisionId.From(id) : null;
    }

    private static async Task<ExistingObservation?> ReadExistingObservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AssetId assetId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                verification_state,
                verified_revision_id,
                verified_size_bytes,
                verified_last_write_utc,
                verified_media_type,
                verified_at_utc
            FROM archive_source_observations
            WHERE asset_id = @asset_id;
            """;
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ExistingObservation(
            ParseVerificationState(reader.GetString(0)),
            reader.IsDBNull(1)
                ? null
                : AssetRevisionId.From(reader.GetGuid(1)),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.IsDBNull(3)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(5));
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
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
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
                @verified_at_utc)
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
        command.Parameters.AddWithValue(
            "asset_id",
            Guid.Parse(assetId.ToString()));
        command.Parameters.AddWithValue("observed_size_bytes", observedSize);
        command.Parameters.AddWithValue(
            "observed_last_write_utc",
            observedWrite.ToUniversalTime());
        command.Parameters.AddWithValue(
            "observed_media_type",
            observedMedia.Trim());
        command.Parameters.AddWithValue(
            "observed_at_utc",
            observedAt.ToUniversalTime());
        command.Parameters.AddWithValue(
            "verification_state",
            ToStorageValue(verificationState));
        command.Parameters.AddWithValue(
            "verified_revision_id",
            verifiedRevisionId is null
                ? DBNull.Value
                : Guid.Parse(verifiedRevisionId.Value.ToString()));
        command.Parameters.AddWithValue(
            "verified_size_bytes",
            (object?)verifiedSize ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "verified_last_write_utc",
            verifiedWrite is null
                ? DBNull.Value
                : verifiedWrite.Value.ToUniversalTime());
        command.Parameters.AddWithValue(
            "verified_media_type",
            (object?)verifiedMedia ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "verified_at_utc",
            verifiedAt is null
                ? DBNull.Value
                : verifiedAt.Value.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ToStorageValue(
        ArchiveSourceObservationVerificationState state) => state switch
    {
        ArchiveSourceObservationVerificationState.Verified => "verified",
        ArchiveSourceObservationVerificationState.NeedsSourceVerification =>
            "needs-source-verification",
        ArchiveSourceObservationVerificationState.Unverified => "unverified",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static ArchiveSourceObservationVerificationState ParseVerificationState(
        string value) => value switch
    {
        "verified" => ArchiveSourceObservationVerificationState.Verified,
        "needs-source-verification" =>
            ArchiveSourceObservationVerificationState.NeedsSourceVerification,
        "unverified" => ArchiveSourceObservationVerificationState.Unverified,
        _ => throw new InvalidDataException(
            $"Unknown archive source verification state '{value}'."),
    };

    private static string ToStorageValue(AssetAvailability availability) =>
        availability switch
        {
            AssetAvailability.Local => "local",
            AssetAvailability.OnlineOnly => "online-only",
            AssetAvailability.Downloading => "downloading",
            AssetAvailability.Unavailable => "unavailable",
            AssetAvailability.Error => "error",
            _ => throw new ArgumentOutOfRangeException(nameof(availability)),
        };

    private static AssetAvailability ParseAvailability(string value) => value switch
    {
        "local" => AssetAvailability.Local,
        "online-only" => AssetAvailability.OnlineOnly,
        "downloading" => AssetAvailability.Downloading,
        "unavailable" => AssetAvailability.Unavailable,
        "error" => AssetAvailability.Error,
        _ => throw new InvalidDataException(
            $"Unknown archive availability state '{value}'."),
    };

    private sealed record ExistingObservation(
        ArchiveSourceObservationVerificationState VerificationState,
        AssetRevisionId? VerifiedRevisionId,
        long? VerifiedSizeBytes,
        DateTimeOffset? VerifiedLastWriteTimeUtc,
        string? VerifiedMediaType,
        DateTimeOffset? VerifiedAtUtc);
}
