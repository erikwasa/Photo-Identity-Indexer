using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL persistence for versioned review-proxy profiles and immutable derivative completion.
/// </summary>
public sealed class PostgresArchiveReviewProxyRepository :
    IArchiveReviewProxyRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresArchiveReviewProxyRepository(
        PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task RegisterProfileAsync(
        ReviewProxyProfile profile,
        DateTimeOffset recordedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO archive_review_proxy_profiles (
                    profile_id,
                    protocol_version,
                    encoder,
                    format,
                    jpeg_quality,
                    maximum_long_edge,
                    resize_policy,
                    canonical_definition,
                    recorded_at_utc)
                VALUES (
                    @profile_id,
                    @protocol_version,
                    @encoder,
                    @format,
                    @jpeg_quality,
                    @maximum_long_edge,
                    @resize_policy,
                    @canonical_definition,
                    @recorded_at_utc)
                ON CONFLICT(profile_id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("profile_id", profile.Id);
            insert.Parameters.AddWithValue(
                "protocol_version",
                ReviewProxyProfile.ProtocolVersion);
            insert.Parameters.AddWithValue(
                "encoder",
                ReviewProxyProfile.Encoder);
            insert.Parameters.AddWithValue(
                "format",
                ReviewProxyProfile.Format);
            insert.Parameters.AddWithValue(
                "jpeg_quality",
                profile.JpegQuality);
            insert.Parameters.AddWithValue(
                "maximum_long_edge",
                profile.MaximumLongEdge);
            insert.Parameters.AddWithValue(
                "resize_policy",
                ReviewProxyProfile.ResizePolicy);
            insert.Parameters.AddWithValue(
                "canonical_definition",
                profile.ToCanonicalText());
            insert.Parameters.AddWithValue(
                "recorded_at_utc",
                recordedAtUtc.ToUniversalTime());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                """
                SELECT canonical_definition
                FROM archive_review_proxy_profiles
                WHERE profile_id = @profile_id;
                """;
            read.Parameters.AddWithValue("profile_id", profile.Id);
            object? canonical =
                await read.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(
                    canonical as string,
                    profile.ToCanonicalText(),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Review proxy profile '{profile.Id}' is already registered with different settings. Use a new profile id for changed settings.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ReviewProxyProfile?> GetProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                protocol_version,
                encoder,
                format,
                jpeg_quality,
                maximum_long_edge,
                resize_policy,
                canonical_definition
            FROM archive_review_proxy_profiles
            WHERE profile_id = @profile_id;
            """;
        command.Parameters.AddWithValue(
            "profile_id",
            profileId.Trim());

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        EnsureCurrentProfileConstants(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(5));

        ReviewProxyProfile profile = new(
            profileId.Trim(),
            reader.GetInt32(4),
            reader.GetInt32(3));
        if (!string.Equals(
                profile.ToCanonicalText(),
                reader.GetString(6),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Stored review proxy profile '{profileId}' has inconsistent canonical settings.");
        }

        return profile;
    }

    public async Task<ArchiveReviewProxyMetadata> RecordCompletionAsync(
        ArchiveReviewProxyMetadata proxy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO asset_revision_review_proxies (
                    asset_revision_id,
                    profile_id,
                    encoded_byte_length,
                    content_sha256,
                    width,
                    height,
                    generated_at_utc,
                    relative_path)
                VALUES (
                    @asset_revision_id,
                    @profile_id,
                    @encoded_byte_length,
                    @content_sha256,
                    @width,
                    @height,
                    @generated_at_utc,
                    @relative_path)
                ON CONFLICT(asset_revision_id, profile_id) DO NOTHING;
                """;
            insert.Parameters.AddWithValue(
                "asset_revision_id",
                Guid.Parse(proxy.AssetRevisionId.ToString()));
            insert.Parameters.AddWithValue(
                "profile_id",
                proxy.ProfileId);
            insert.Parameters.AddWithValue(
                "encoded_byte_length",
                proxy.EncodedByteLength);
            insert.Parameters.AddWithValue(
                "content_sha256",
                proxy.ContentHash.ToString());
            insert.Parameters.AddWithValue("width", proxy.Width);
            insert.Parameters.AddWithValue("height", proxy.Height);
            insert.Parameters.AddWithValue(
                "generated_at_utc",
                proxy.GeneratedAtUtc);
            insert.Parameters.AddWithValue(
                "relative_path",
                proxy.RelativePath);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        ArchiveReviewProxyMetadata persisted =
            await ReadAsync(
                connection,
                transaction,
                proxy.AssetRevisionId,
                proxy.ProfileId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Review proxy completion was not available after persistence.");

        if (!SameDerivative(proxy, persisted))
        {
            throw new InvalidOperationException(
                $"Review proxy profile '{proxy.ProfileId}' is already complete for revision {proxy.AssetRevisionId} with different derivative metadata.");
        }

        await transaction.CommitAsync(cancellationToken);
        return persisted;
    }

    public async Task<ArchiveReviewProxyMetadata?> GetAsync(
        AssetRevisionId revisionId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(
            connection,
            transaction: null,
            revisionId,
            profileId.Trim(),
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<AssetRevisionId, ArchiveReviewProxyMetadata>>
        GetManyAsync(
            IReadOnlyCollection<AssetRevisionId> revisionIds,
            string profileId,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revisionIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        AssetRevisionId[] distinctRevisionIds =
            revisionIds.Distinct().ToArray();
        if (distinctRevisionIds.Length == 0)
        {
            return new Dictionary<AssetRevisionId, ArchiveReviewProxyMetadata>();
        }

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();

        string[] parameters = distinctRevisionIds
            .Select((_, index) => $"@asset_revision_id_{index}")
            .ToArray();
        command.CommandText =
            $"""
            SELECT
                asset_revision_id,
                profile_id,
                encoded_byte_length,
                content_sha256,
                width,
                height,
                generated_at_utc,
                relative_path
            FROM asset_revision_review_proxies
            WHERE profile_id = @profile_id
              AND asset_revision_id IN ({string.Join(", ", parameters)});
            """;
        command.Parameters.AddWithValue(
            "profile_id",
            profileId.Trim());
        for (int index = 0; index < distinctRevisionIds.Length; index++)
        {
            command.Parameters.AddWithValue(
                $"asset_revision_id_{index}",
                Guid.Parse(distinctRevisionIds[index].ToString()));
        }

        Dictionary<AssetRevisionId, ArchiveReviewProxyMetadata> results = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ArchiveReviewProxyMetadata record = ReadMetadata(reader);
            results[record.AssetRevisionId] = record;
        }

        return results;
    }

    public async Task<IReadOnlyList<AssetRevisionId>> GetPendingCurrentRevisionIdsAsync(
        SourceId sourceId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT revision.id
            FROM assets AS asset
            INNER JOIN asset_revisions AS revision
                ON revision.id = (
                    SELECT candidate.id
                    FROM asset_revisions AS candidate
                    WHERE candidate.asset_id = asset.id
                    ORDER BY candidate.observed_at_utc DESC, candidate.id DESC
                    LIMIT 1)
            LEFT JOIN asset_revision_review_proxies AS proxy
                ON proxy.asset_revision_id = revision.id
               AND proxy.profile_id = @profile_id
            WHERE asset.source_id = @source_id
              AND asset.deleted_at_utc IS NULL
              AND proxy.asset_revision_id IS NULL
            ORDER BY asset.source_key;
            """;
        command.Parameters.AddWithValue(
            "source_id",
            Guid.Parse(sourceId.ToString()));
        command.Parameters.AddWithValue(
            "profile_id",
            profileId.Trim());

        List<AssetRevisionId> revisions = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(
                AssetRevisionId.From(reader.GetGuid(0)));
        }

        return revisions;
    }

    private static async Task<ArchiveReviewProxyMetadata?> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        AssetRevisionId revisionId,
        string profileId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT
                asset_revision_id,
                profile_id,
                encoded_byte_length,
                content_sha256,
                width,
                height,
                generated_at_utc,
                relative_path
            FROM asset_revision_review_proxies
            WHERE asset_revision_id = @asset_revision_id
              AND profile_id = @profile_id;
            """;
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(revisionId.ToString()));
        command.Parameters.AddWithValue(
            "profile_id",
            profileId);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadMetadata(reader)
            : null;
    }

    private static ArchiveReviewProxyMetadata ReadMetadata(
        NpgsqlDataReader reader) => new(
        AssetRevisionId.From(reader.GetGuid(0)),
        reader.GetString(1),
        reader.GetInt64(2),
        new Sha256Digest(reader.GetString(3)),
        reader.GetInt32(4),
        reader.GetInt32(5),
        reader.GetFieldValue<DateTimeOffset>(6),
        reader.GetString(7));

    private static bool SameDerivative(
        ArchiveReviewProxyMetadata requested,
        ArchiveReviewProxyMetadata persisted) =>
        requested.AssetRevisionId == persisted.AssetRevisionId &&
        string.Equals(
            requested.ProfileId,
            persisted.ProfileId,
            StringComparison.Ordinal) &&
        requested.EncodedByteLength == persisted.EncodedByteLength &&
        requested.ContentHash == persisted.ContentHash &&
        requested.Width == persisted.Width &&
        requested.Height == persisted.Height &&
        string.Equals(
            requested.RelativePath,
            persisted.RelativePath,
            StringComparison.Ordinal);

    private static void EnsureCurrentProfileConstants(
        string protocolVersion,
        string encoder,
        string format,
        string resizePolicy)
    {
        if (!string.Equals(
                protocolVersion,
                ReviewProxyProfile.ProtocolVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                encoder,
                ReviewProxyProfile.Encoder,
                StringComparison.Ordinal) ||
            !string.Equals(
                format,
                ReviewProxyProfile.Format,
                StringComparison.Ordinal) ||
            !string.Equals(
                resizePolicy,
                ReviewProxyProfile.ResizePolicy,
                StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "The stored review proxy profile uses a protocol that this build does not support.");
        }
    }
}
