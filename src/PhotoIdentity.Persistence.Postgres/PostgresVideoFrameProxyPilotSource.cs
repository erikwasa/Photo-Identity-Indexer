using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Source-free pilot authorization for one *already published* playable
/// proxy. It NEVER resolves or opens an original, queues a job, hydrates a
/// placeholder, creates a cache or updates catalogue rows.
/// </summary>
public sealed record VideoFrameProxySnapshot(Guid RevisionId, decimal DurationSeconds,
    long ProxyBytes);

public sealed class PostgresVideoFrameProxyPilotSource(PostgresCatalogueDatabase database)
{
    public const long MaximumPilotProxyBytes = 256L * 1024 * 1024;

    public static bool IsCanonicalProxyPath(string? path, Guid jobId)
    {
        if (jobId == Guid.Empty || path is null) return false;
        string[] parts = path.Split('/');
        return parts.Length == 3 && parts[0] == "video-derivatives" &&
            parts[1] == jobId.ToString("N") &&
            Regex.IsMatch(parts[2], @"^[0-9a-f]{32}[.]mp4$", RegexOptions.CultureInvariant);
    }

    public async Task<VideoFrameProxySnapshot?> CopyCurrentProxyAsync(
        Guid jobId, string cacheRoot, string stagedCopyPath,
        CancellationToken token = default)
    {
        if (jobId == Guid.Empty || !Path.IsPathFullyQualified(stagedCopyPath) ||
            File.Exists(stagedCopyPath))
            throw new ArgumentException("An unoccupied absolute staging destination and job are required.");
        string root = await VideoDerivativeCacheGuard.ValidateAsync(database, cacheRoot, token);
        // Destination is an owned workspace created by the caller, not a
        // catalogued original. Reject linked roots rather than following them.
        VideoDerivativeCacheGuard.AssertNoLinks(Path.GetDirectoryName(stagedCopyPath)!);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using NpgsqlCommand query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT published.artifact_relative_path, published.content_sha256,
                   published.encoded_bytes, meta.duration_seconds,
                   published.asset_revision_id
            FROM video_published_derivatives published
            JOIN video_processing_jobs job ON job.id = published.job_id
            JOIN asset_revisions revision ON revision.id = published.asset_revision_id
            JOIN assets asset ON asset.id = revision.asset_id
            JOIN sources source ON source.id = asset.source_id
            JOIN video_revision_metadata meta ON meta.asset_revision_id = revision.id
            WHERE job.id = @job AND job.status = 'succeeded'
              AND job.derivative_kind = 'PlayableProxy'
              AND job.asset_revision_id = revision.id
              AND revision.media_kind = 'video' AND asset.media_kind = 'video'
              AND asset.deleted_at_utc IS NULL AND source.kind = 'local-folder'
              AND meta.inspection_status = 'supported'
              AND meta.color_transfer = 'bt709'
              AND meta.duration_seconds BETWEEN 0.2 AND 3600
              AND revision.id = (
                  SELECT newest.id FROM asset_revisions newest
                  WHERE newest.asset_id=asset.id AND newest.media_kind='video'
                  ORDER BY newest.observed_at_utc DESC,newest.id DESC LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id=asset.source_id
                    AND excluded.source_key=asset.source_key)
            FOR UPDATE OF source;
            """;
        query.Parameters.AddWithValue("job", jobId);
        string? relative = null, sha256 = null;
        long size = 0;
        decimal duration = 0;
        Guid revisionId = Guid.Empty;
        await using (NpgsqlDataReader reader = await query.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token))
            {
                relative = reader.GetString(0);
                sha256 = reader.GetString(1);
                size = reader.GetInt64(2);
                duration = reader.GetDecimal(3);
                revisionId = reader.GetGuid(4);
            }
        }
        if (relative is null) return null;
        if (!IsCanonicalProxyPath(relative, jobId) ||
            !VideoDerivativeArtifactPath.IsHash(sha256) ||
            size is < 100 or > MaximumPilotProxyBytes)
            throw new InvalidDataException("The registered local video proxy is not admissible.");

        string path = Path.GetFullPath(Path.Combine(root,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("The registered proxy escapes its cache.");
        VideoDerivativeCacheGuard.AssertNoLinks(Path.GetDirectoryName(path)!);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The registered playable proxy is missing or linked.");

        try
        {
            // Under the source FK-serialization lock, copy registered bytes,
            // then hash the temporary copy before allowing FFmpeg to read it.
            await using (FileStream input = new(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (FileStream output = new(stagedCopyPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                if (input.Length != size)
                    throw new InvalidDataException("Proxy size no longer matches registration.");
                await input.CopyToAsync(output, 65536, token);
            }
            await using FileStream staged = new(stagedCopyPath, FileMode.Open,
                FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (staged.Length != size ||
                Convert.ToHexString(await SHA256.HashDataAsync(staged, token))
                    .ToLowerInvariant() != sha256)
                throw new InvalidDataException("Proxy bytes no longer match registered SHA-256.");
            await transaction.CommitAsync(token);
            // No original is accessed. No revision/job/source identifier is
            // returned to the operator from the eventual CLI JSON report.
            return new VideoFrameProxySnapshot(revisionId, duration, size);
        }
        catch
        {
            if (File.Exists(stagedCopyPath))
                File.Delete(stagedCopyPath);
            throw;
        }
    }

    public Task<bool> StillCurrentAsync(Guid jobId, Guid revisionId,
        CancellationToken token = default) => CheckAsync(jobId, revisionId, token);

    private async Task<bool> CheckAsync(Guid jobId, Guid revisionId,
        CancellationToken token)
    {
        VideoPublishedDerivativeReceipt? receipt =
            await new PostgresVideoPublishedDerivativeRepository(database)
                .GetCurrentAsync(jobId, token);
        return receipt?.RevisionId == revisionId;
    }
}
