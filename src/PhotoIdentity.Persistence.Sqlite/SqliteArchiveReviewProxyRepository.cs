using System.Globalization;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Persists exact review-proxy profiles and completion independently from detector/embedder analysis state.
/// </summary>
public sealed class SqliteArchiveReviewProxyRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteArchiveReviewProxyRepository(SqliteCatalogueDatabase database)
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

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
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
                    $profile_id,
                    $protocol_version,
                    $encoder,
                    $format,
                    $jpeg_quality,
                    $maximum_long_edge,
                    $resize_policy,
                    $canonical_definition,
                    $recorded_at_utc)
                ON CONFLICT(profile_id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$profile_id", profile.Id);
            command.Parameters.AddWithValue("$protocol_version", ReviewProxyProfile.ProtocolVersion);
            command.Parameters.AddWithValue("$encoder", ReviewProxyProfile.Encoder);
            command.Parameters.AddWithValue("$format", ReviewProxyProfile.Format);
            command.Parameters.AddWithValue("$jpeg_quality", profile.JpegQuality);
            command.Parameters.AddWithValue("$maximum_long_edge", profile.MaximumLongEdge);
            command.Parameters.AddWithValue("$resize_policy", ReviewProxyProfile.ResizePolicy);
            command.Parameters.AddWithValue("$canonical_definition", profile.ToCanonicalText());
            command.Parameters.AddWithValue("$recorded_at_utc", Format(recordedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT canonical_definition
                FROM archive_review_proxy_profiles
                WHERE profile_id = $profile_id;
                """;
            command.Parameters.AddWithValue("$profile_id", profile.Id);
            object? canonical = await command.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(canonical as string, profile.ToCanonicalText(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Review proxy profile '{profile.Id}' is already registered with different settings. Use a new profile id for changed settings.");
            }
        }

        transaction.Commit();
    }

    public async Task<ReviewProxyProfile?> GetProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT protocol_version, encoder, format, jpeg_quality, maximum_long_edge,
                   resize_policy, canonical_definition
            FROM archive_review_proxy_profiles
            WHERE profile_id = $profile_id;
            """;
        command.Parameters.AddWithValue("$profile_id", profileId.Trim());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        EnsureCurrentProfileConstants(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(5));
        ReviewProxyProfile profile = new(profileId.Trim(), reader.GetInt32(4), reader.GetInt32(3));
        if (!string.Equals(profile.ToCanonicalText(), reader.GetString(6), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Stored review proxy profile '{profileId}' has inconsistent canonical settings.");
        }

        return profile;
    }

    public async Task<ArchiveReviewProxyRecord> RecordCompletionAsync(
        ArchiveReviewProxyRecord proxy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
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
                    $asset_revision_id,
                    $profile_id,
                    $encoded_byte_length,
                    $content_sha256,
                    $width,
                    $height,
                    $generated_at_utc,
                    $relative_path)
                ON CONFLICT(asset_revision_id, profile_id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$asset_revision_id", proxy.AssetRevisionId.ToString());
            command.Parameters.AddWithValue("$profile_id", proxy.ProfileId);
            command.Parameters.AddWithValue("$encoded_byte_length", proxy.EncodedByteLength);
            command.Parameters.AddWithValue("$content_sha256", proxy.ContentHash.ToString());
            command.Parameters.AddWithValue("$width", proxy.Width);
            command.Parameters.AddWithValue("$height", proxy.Height);
            command.Parameters.AddWithValue("$generated_at_utc", Format(proxy.GeneratedAtUtc));
            command.Parameters.AddWithValue("$relative_path", proxy.RelativePath);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        ArchiveReviewProxyRecord persisted = await ReadAsync(
            connection,
            transaction,
            proxy.AssetRevisionId,
            proxy.ProfileId,
            cancellationToken)
            ?? throw new InvalidOperationException("Review proxy completion was not available after persistence.");

        if (!SameDerivative(proxy, persisted))
        {
            throw new InvalidOperationException(
                $"Review proxy profile '{proxy.ProfileId}' is already complete for revision {proxy.AssetRevisionId} with different derivative metadata.");
        }

        transaction.Commit();
        return persisted;
    }

    public async Task<ArchiveReviewProxyRecord?> GetAsync(
        AssetRevisionId revisionId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadAsync(connection, null, revisionId, profileId.Trim(), cancellationToken);
    }

    public async Task<IReadOnlyList<AssetRevisionId>> GetPendingCurrentRevisionIdsAsync(
        SourceId sourceId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
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
            LEFT JOIN asset_revision_review_proxies AS proxy
                ON proxy.asset_revision_id = revision.id
               AND proxy.profile_id = $profile_id
            WHERE asset.source_id = $source_id
              AND asset.deleted_at_utc IS NULL
              AND proxy.asset_revision_id IS NULL
            ORDER BY asset.source_key;
            """;
        command.Parameters.AddWithValue("$source_id", sourceId.ToString());
        command.Parameters.AddWithValue("$profile_id", profileId.Trim());

        List<AssetRevisionId> revisions = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            revisions.Add(AssetRevisionId.From(Guid.Parse(reader.GetString(0))));
        }

        return revisions;
    }

    private static async Task<ArchiveReviewProxyRecord?> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        AssetRevisionId revisionId,
        string profileId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT asset_revision_id, profile_id, encoded_byte_length, content_sha256,
                   width, height, generated_at_utc, relative_path
            FROM asset_revision_review_proxies
            WHERE asset_revision_id = $asset_revision_id AND profile_id = $profile_id;
            """;
        command.Parameters.AddWithValue("$asset_revision_id", revisionId.ToString());
        command.Parameters.AddWithValue("$profile_id", profileId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ArchiveReviewProxyRecord(
                AssetRevisionId.From(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                reader.GetInt64(2),
                new Sha256Digest(reader.GetString(3)),
                reader.GetInt32(4),
                reader.GetInt32(5),
                Parse(reader.GetString(6)),
                reader.GetString(7))
            : null;
    }

    private static bool SameDerivative(
        ArchiveReviewProxyRecord requested,
        ArchiveReviewProxyRecord persisted) =>
        requested.AssetRevisionId == persisted.AssetRevisionId &&
        string.Equals(requested.ProfileId, persisted.ProfileId, StringComparison.Ordinal) &&
        requested.EncodedByteLength == persisted.EncodedByteLength &&
        requested.ContentHash == persisted.ContentHash &&
        requested.Width == persisted.Width &&
        requested.Height == persisted.Height &&
        string.Equals(requested.RelativePath, persisted.RelativePath, StringComparison.Ordinal);

    private static void EnsureCurrentProfileConstants(
        string protocolVersion,
        string encoder,
        string format,
        string resizePolicy)
    {
        if (!string.Equals(protocolVersion, ReviewProxyProfile.ProtocolVersion, StringComparison.Ordinal) ||
            !string.Equals(encoder, ReviewProxyProfile.Encoder, StringComparison.Ordinal) ||
            !string.Equals(format, ReviewProxyProfile.Format, StringComparison.Ordinal) ||
            !string.Equals(resizePolicy, ReviewProxyProfile.ResizePolicy, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "The stored review proxy profile uses a protocol that this build does not support.");
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
