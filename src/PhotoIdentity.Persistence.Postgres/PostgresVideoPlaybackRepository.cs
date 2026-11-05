using System.Security.Cryptography;
using PhotoIdentity.Core.Catalogue;
using Npgsql;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Owns an immutable read handle and the source-row authorization lock for
/// the full browser response. Disposal releases both on seek completion or
/// client cancellation; no original file is ever opened.
/// </summary>
public sealed class VideoPlaybackLease(
    FileStream stream, NpgsqlTransaction transaction, NpgsqlConnection connection,
    AssetRevisionId revisionId)
    : IAsyncDisposable
{
    public FileStream Stream { get; } = stream;

    /// <summary>
    /// Skip re-rendering an already registered still. This read is performed
    /// under the same source-row authorization lock as the proxy byte proof.
    /// It does not touch source media or existing JPEG bytes.
    /// </summary>
    public async Task<bool> HasRegisteredPosterAsync(CancellationToken token = default)
    {
        await using NpgsqlCommand query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM video_still_posters poster
                WHERE poster.asset_revision_id = @revision
                  AND poster.poster_profile_id IN (
                    'wi0151-sdr-still-v1', 'wi0211-proxy-still-v1'));
            """;
        query.Parameters.AddWithValue("revision", revisionId.Value);
        return await query.ExecuteScalarAsync(token) is true;
    }

    /// <summary>
    /// Publish one still under the SAME source lock that guarded verified proxy
    /// bytes. Calling code must verify bounded JPEG bytes and geometry first.
    /// This does not read originals and cannot replace any existing poster.
    /// </summary>
    public async Task<bool> PublishProxyPosterAsync(
        VideoStillPoster poster, CancellationToken token = default)
    {
        if (poster.RevisionId != revisionId ||
            poster.PosterProfileId != "wi0211-proxy-still-v1" ||
            poster.ToolSha256.Length != 64 || poster.PosterSha256.Length != 64 ||
            poster.EncodedBytes is < 100 or > 8 * 1024 * 1024 ||
            poster.Width is < 1 or > 960 || poster.Height is < 1 or > 960 ||
            !poster.PosterRelativePath.Equals(
                $"video-posters/wi0211-proxy-still-v1/{revisionId.Value:D}/{poster.ToolSha256}.jpg",
                StringComparison.Ordinal) ||
            !poster.ToolSha256.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            !poster.PosterSha256.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("Untrusted proxy-derived poster evidence.");

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO video_still_posters (
                asset_revision_id, poster_profile_id, tool_sha256,
                poster_sha256, poster_relative_path, encoded_bytes,
                width, height, generated_at_utc)
            SELECT @revision, @profile, @tool, @hash, @path,
                   @bytes, @width, @height, @generated
            WHERE EXISTS (
                SELECT 1 FROM asset_revisions revision
                JOIN assets asset ON asset.id = revision.asset_id
                JOIN video_revision_metadata m ON m.asset_revision_id = revision.id
                WHERE revision.id = @revision AND asset.media_kind = 'video'
                  AND revision.media_kind = 'video' AND asset.deleted_at_utc IS NULL
                  AND m.inspection_status = 'supported' AND m.color_transfer = 'bt709'
                  AND revision.id = (
                      SELECT newest.id FROM asset_revisions newest
                      WHERE newest.asset_id = asset.id AND newest.media_kind = 'video'
                      ORDER BY newest.observed_at_utc DESC, newest.id DESC LIMIT 1)
                  AND NOT EXISTS (
                      SELECT 1 FROM source_copy_exclusions excluded
                      WHERE excluded.source_id = asset.source_id
                        AND excluded.source_key = asset.source_key))
              AND NOT EXISTS (
                SELECT 1 FROM video_still_posters existing
                WHERE existing.asset_revision_id = @revision
                  AND existing.poster_profile_id IN (
                    'wi0151-sdr-still-v1', 'wi0211-proxy-still-v1'))
            ON CONFLICT (asset_revision_id, poster_profile_id, tool_sha256) DO NOTHING;
            """;
        command.Parameters.AddWithValue("revision", poster.RevisionId.Value);
        command.Parameters.AddWithValue("profile", poster.PosterProfileId);
        command.Parameters.AddWithValue("tool", poster.ToolSha256);
        command.Parameters.AddWithValue("hash", poster.PosterSha256);
        command.Parameters.AddWithValue("path", poster.PosterRelativePath);
        command.Parameters.AddWithValue("bytes", poster.EncodedBytes);
        command.Parameters.AddWithValue("width", poster.Width);
        command.Parameters.AddWithValue("height", poster.Height);
        command.Parameters.AddWithValue("generated", poster.GeneratedAtUtc.ToUniversalTime());
        bool inserted = await command.ExecuteNonQueryAsync(token) == 1;
        if (inserted) await transaction.CommitAsync(token);
        return inserted;
    }

    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}

/// <summary>
/// Source-free, read-only WI-0152 boundary for registered current MP4 proxies.
/// A receipt alone is not sufficient: every request verifies the private cache,
/// revision, exclusion, file identity and digest before exposing a seekable stream.
/// </summary>
public sealed class PostgresVideoPlaybackRepository(PostgresCatalogueDatabase database)
{
    public const long MaximumPlaybackBytes = 256L * 1024 * 1024;

    public async Task<VideoPlaybackLease?> OpenCurrentAsync(
        AssetRevisionId revisionId, string cacheRoot,
        CancellationToken token = default)
    {
        if (revisionId.IsEmpty) return null;
        string root = await VideoDerivativeCacheGuard.ValidateAsync(
            database, cacheRoot, token);

        NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        NpgsqlTransaction? transaction = null;
        FileStream? stream = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(token);
            // Lock BEFORE the eligibility query. A lock waited on during a
            // single SELECT could otherwise use a snapshot taken before an
            // exclusion committed. The next statement gets a fresh snapshot.
            await using (NpgsqlCommand sourceLock = connection.CreateCommand())
            {
                sourceLock.Transaction = transaction;
                sourceLock.CommandText = """
                    SELECT source.id
                    FROM asset_revisions revision
                    JOIN assets asset ON asset.id = revision.asset_id
                    JOIN sources source ON source.id = asset.source_id
                    WHERE revision.id = @revision AND source.kind = 'local-folder'
                    FOR UPDATE OF source;
                    """;
                sourceLock.Parameters.AddWithValue("revision", revisionId.Value);
                if (await sourceLock.ExecuteScalarAsync(token) is not Guid)
                {
                    await transaction.DisposeAsync();
                    await connection.DisposeAsync();
                    return null;
                }
            }
            await using NpgsqlCommand query = connection.CreateCommand();
            query.Transaction = transaction;
            query.CommandText = """
                SELECT receipt.job_id, receipt.artifact_relative_path,
                       receipt.content_sha256, receipt.encoded_bytes
                FROM video_published_derivatives receipt
                JOIN video_processing_jobs job ON job.id = receipt.job_id
                JOIN asset_revisions revision ON revision.id = receipt.asset_revision_id
                JOIN assets asset ON asset.id = revision.asset_id
                JOIN sources source ON source.id = asset.source_id
                JOIN video_revision_metadata metadata
                  ON metadata.asset_revision_id = revision.id
                WHERE receipt.asset_revision_id = @revision
                  AND job.asset_revision_id = revision.id
                  AND job.status = 'succeeded'
                  AND job.derivative_kind = 'PlayableProxy'
                  AND job.policy_version = 'wi0188-v1'
                  AND job.preset_version IN (
                    'wi0188-sdr-h264-aac-720p-pilot-v1',
                    'wi0189-sdr-h264-aac-540p-compact-v1',
                    'wi0210-rec709-fullrange-720p-production-v1')
                  AND revision.media_kind = 'video'
                  AND asset.media_kind = 'video'
                  AND asset.deleted_at_utc IS NULL
                  AND source.kind = 'local-folder'
                  AND metadata.inspection_status = 'supported'
                  AND metadata.color_transfer = 'bt709'
                  AND revision.id = (
                    SELECT latest.id FROM asset_revisions latest
                    WHERE latest.asset_id = asset.id AND latest.media_kind = 'video'
                    ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                  AND NOT EXISTS (
                    SELECT 1 FROM source_copy_exclusions excluded
                    WHERE excluded.source_id = asset.source_id
                      AND excluded.source_key = asset.source_key)
                ORDER BY receipt.created_at_utc DESC, receipt.job_id DESC
                LIMIT 1;
                """;
            query.Parameters.AddWithValue("revision", revisionId.Value);
            Guid jobId = Guid.Empty;
            string? relative = null, sha = null;
            long expectedLength = 0;
            await using (NpgsqlDataReader reader = await query.ExecuteReaderAsync(token))
            {
                if (await reader.ReadAsync(token))
                {
                    jobId = reader.GetGuid(0);
                    relative = reader.GetString(1);
                    sha = reader.GetString(2);
                    expectedLength = reader.GetInt64(3);
                }
            }

            if (relative is null)
            {
                await transaction.DisposeAsync();
                await connection.DisposeAsync();
                return null;
            }
            if (!TryCanonicalPath(root, jobId, relative, out string path) ||
                !VideoDerivativeArtifactPath.IsHash(sha) ||
                expectedLength is < 100 or > MaximumPlaybackBytes)
                throw new InvalidDataException("Untrusted video proxy receipt.");

            VideoDerivativeCacheGuard.AssertNoLinks(Path.GetDirectoryName(path)!);
            if (!File.Exists(path) ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new FileNotFoundException("The retained video proxy is unavailable.");

            // FileShare.Read denies normal writers/evictors on Windows while
            // ranges are served; source lock fences exclusion and revision
            // changes for the same duration.
            stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length != expectedLength)
                throw new InvalidDataException("Proxy size differs from receipt.");

            // Hash BEFORE sending any bytes, not merely the requested range.
            // Keep the same read handle through range delivery. The existing
            // 256 MiB derivative cap bounds the validation work.
            string actual = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
            if (!string.Equals(actual, sha, StringComparison.Ordinal))
                throw new InvalidDataException("Proxy digest differs from receipt.");

            stream.Position = 0;
            byte[] mp4Header = new byte[8];
            await stream.ReadExactlyAsync(mp4Header, token);
            if (mp4Header[4] != (byte)'f' || mp4Header[5] != (byte)'t' ||
                mp4Header[6] != (byte)'y' || mp4Header[7] != (byte)'p')
                throw new InvalidDataException("Registered proxy is not an MP4.");
            stream.Position = 0;
            return new VideoPlaybackLease(stream, transaction, connection, revisionId);
        }
        catch
        {
            if (stream is not null) await stream.DisposeAsync();
            if (transaction is not null) await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Reconstruct an exact publisher-owned path, never use arbitrary
    /// stored filesystem path fragments.</summary>
    public static bool TryCanonicalPath(
        string root, Guid jobId, string? relative, out string path)
    {
        path = "";
        if (jobId == Guid.Empty || relative is null) return false;
        string[] parts = relative.Split('/');
        if (parts.Length != 3 ||
            !Guid.TryParseExact(parts[1], "N", out Guid storedJob) ||
            storedJob != jobId || parts[2].Length != 36 ||
            !Guid.TryParseExact(parts[2][..32], "N", out Guid lease) ||
            !VideoDerivativeArtifactPath.IsCanonical(relative, jobId, lease))
            return false;
        path = Path.Combine(root, "video-derivatives", jobId.ToString("N"),
            lease.ToString("N") + ".mp4");
        return true;
    }
}
