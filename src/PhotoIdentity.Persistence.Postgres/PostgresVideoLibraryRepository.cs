using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Persistence.Postgres;

public sealed record VideoLibraryEntry(
    AssetRevisionId RevisionId,
    string? InspectionStatus,
    decimal? DurationSeconds,
    int? Width,
    int? Height,
    int? RotationDegrees,
    DateTimeOffset? CaptureAtUtc,
    bool HasPoster);

public sealed record VideoLibraryPage(
    IReadOnlyList<VideoLibraryEntry> Items, int Offset, int Limit, bool HasMore);

/// <summary>
/// Explicit video-only read boundary. No photo, search, collection or slideshow
/// query uses it. Poster payloads are checked under the same source-row lock
/// that serializes source-copy exclusion and publication.
/// </summary>
public sealed class PostgresVideoLibraryRepository(PostgresCatalogueDatabase database)
{
    private readonly PostgresSourceCopyExclusionRepository _exclusions = new(database);
    private const string PosterProfile = "wi0151-sdr-still-v1";
    private const long MaxPosterBytes = 8L * 1024 * 1024;

    public async Task<VideoLibraryPage> ListAsync(
        int offset, int limit, CancellationToken token = default)
    {
        if (offset < 0 || limit is < 1 or > 60)
            throw new ArgumentOutOfRangeException(nameof(limit), "Video library page must be bounded.");
        await _exclusions.EnsureReadyAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT latest.id, metadata.inspection_status,
                   metadata.duration_seconds, metadata.coded_width,
                   metadata.coded_height, metadata.display_rotation_degrees,
                   metadata.capture_at_utc,
                   (poster.asset_revision_id IS NOT NULL) AS has_poster
            FROM assets asset
            JOIN LATERAL (
                SELECT revision.id, revision.observed_at_utc
                FROM asset_revisions revision
                WHERE revision.asset_id = asset.id AND revision.media_kind = 'video'
                ORDER BY revision.observed_at_utc DESC, revision.id DESC
                LIMIT 1
            ) latest ON TRUE
            LEFT JOIN video_revision_metadata metadata
                ON metadata.asset_revision_id = latest.id
            LEFT JOIN LATERAL (
                SELECT poster.asset_revision_id
                FROM video_still_posters poster
                WHERE poster.asset_revision_id = latest.id
                  AND poster.poster_profile_id = 'wi0151-sdr-still-v1'
                  AND metadata.inspection_status = 'supported'
                  AND metadata.color_transfer = 'bt709'
                ORDER BY poster.generated_at_utc DESC, poster.tool_sha256
                LIMIT 1
            ) poster ON TRUE
            WHERE asset.media_kind = 'video' AND asset.deleted_at_utc IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions exclusion
                  WHERE exclusion.source_id = asset.source_id
                    AND exclusion.source_key = asset.source_key)
            ORDER BY COALESCE(metadata.capture_at_utc, latest.observed_at_utc) DESC,
                     latest.id DESC
            LIMIT @take OFFSET @skip;
            """;
        command.Parameters.AddWithValue("take", limit + 1);
        command.Parameters.AddWithValue("skip", offset);
        List<VideoLibraryEntry> rows = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            rows.Add(new VideoLibraryEntry(
                AssetRevisionId.From(reader.GetGuid(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetBoolean(7)));
        }
        return new VideoLibraryPage(rows.Take(limit).ToArray(), offset, limit,
            rows.Count > limit);
    }

    /// <summary>
    /// Returns validated JPEG bytes, never a filesystem path/handle. Source
    /// lock spans the authorization check and file read, so an exclusion
    /// cannot commit between them.
    /// </summary>
    public async Task<byte[]?> GetPosterBytesAsync(
        AssetRevisionId revisionId, string derivativeRoot,
        CancellationToken token = default)
    {
        if (revisionId.IsEmpty) return null;
        ArgumentException.ThrowIfNullOrWhiteSpace(derivativeRoot);
        await _exclusions.EnsureReadyAsync(token);

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT poster.poster_relative_path, poster.poster_sha256,
                   poster.encoded_bytes, source.root_locator
            FROM asset_revisions revision
            JOIN assets asset ON asset.id = revision.asset_id
            JOIN sources source ON source.id = asset.source_id
            JOIN video_revision_metadata metadata ON metadata.asset_revision_id = revision.id
            JOIN LATERAL (
                SELECT stored.poster_relative_path, stored.poster_sha256,
                       stored.encoded_bytes, stored.tool_sha256
                FROM video_still_posters stored
                WHERE stored.asset_revision_id = revision.id
                  AND stored.poster_profile_id = 'wi0151-sdr-still-v1'
                ORDER BY stored.generated_at_utc DESC, stored.tool_sha256
                LIMIT 1
            ) poster ON TRUE
            WHERE revision.id = @revision
              AND revision.media_kind = 'video' AND asset.media_kind = 'video'
              AND asset.deleted_at_utc IS NULL
              AND source.kind = 'local-folder'
              AND metadata.inspection_status = 'supported'
              AND metadata.color_transfer = 'bt709'
              AND revision.id = (
                  SELECT newest.id FROM asset_revisions newest
                  WHERE newest.asset_id = asset.id AND newest.media_kind = 'video'
                  ORDER BY newest.observed_at_utc DESC, newest.id DESC LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id = asset.source_id
                    AND excluded.source_key = asset.source_key)
            FOR UPDATE OF source;
            """;
        command.Parameters.Add(new NpgsqlParameter("revision", NpgsqlDbType.Uuid)
            { Value = revisionId.Value });
        string? relative = null, sha = null, archiveRoot = null;
        long bytes = 0;
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
            {
                relative = reader.GetString(0);
                sha = reader.GetString(1);
                bytes = reader.GetInt64(2);
                archiveRoot = reader.GetString(3);
            }
        }
        if (relative is null || sha is null || archiveRoot is null)
            return null;

        byte[] result = await ReadVerifiedJpegAsync(
            derivativeRoot, archiveRoot, revisionId, relative, sha, bytes, token);
        await transaction.CommitAsync(token);
        return result;
    }

    private static async Task<byte[]> ReadVerifiedJpegAsync(
        string derivativeRoot, string originalRoot, AssetRevisionId revision,
        string relative, string sha256, long expectedBytes, CancellationToken token)
    {
        string root = Path.GetFullPath(derivativeRoot);
        string originals = Path.GetFullPath(originalRoot);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        static string Prefix(string path) =>
            path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (root.Equals(originals, comparison) ||
            root.StartsWith(Prefix(originals), comparison) ||
            originals.StartsWith(Prefix(root), comparison))
            throw new InvalidOperationException("Video poster root overlaps originals.");

        if (sha256.Length != 64 || sha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            expectedBytes is < 100 or > MaxPosterBytes)
            throw new InvalidDataException("Untrusted video poster evidence.");

        string[] parts = relative.Split('/');
        if (parts.Length != 4 ||
            parts[0] != "video-posters" ||
            parts[1] != PosterProfile ||
            parts[2] != revision.Value.ToString("D") ||
            parts[3].Length != 68 ||
            !parts[3].EndsWith(".jpg", StringComparison.Ordinal) ||
            parts[3][..64].Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Video poster path is not canonical.");

        string absolute = Path.GetFullPath(Path.Combine(root,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!absolute.StartsWith(Prefix(root), comparison))
            throw new InvalidDataException("Poster path escapes derivative directory.");
        for (string? check = Path.GetDirectoryName(absolute); check is not null;
             check = Path.GetDirectoryName(check))
        {
            if (Directory.Exists(check) &&
                (File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Unsafe video derivative directory.");
            if (check == Path.GetPathRoot(check)) break;
        }
        if (!File.Exists(absolute) ||
            (File.GetAttributes(absolute) & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException("The indexed video poster is missing.");

        await using FileStream input = new(absolute, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != expectedBytes)
            throw new InvalidDataException("Poster bytes do not match recorded evidence.");
        byte[] data = new byte[checked((int)input.Length)];
        await input.ReadExactlyAsync(data, token);
        if (data[0] != 0xff || data[1] != 0xd8 ||
            data[^2] != 0xff || data[^1] != 0xd9 ||
            !Convert.ToHexString(SHA256.HashData(data)).Equals(sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Poster JPEG signature or checksum mismatch.");
        return data;
    }
}
