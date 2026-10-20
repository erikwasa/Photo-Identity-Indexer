using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Aggregate only: no private paths, source identifiers, hashes or job IDs.
/// Protected entries include registered/current receipts and active jobs.
/// </summary>
public sealed record VideoDerivativeRecoveryReport(
    int Considered, int Eligible, int Deleted, int Protected, int Refused,
    int InvalidatedRegistered, bool DryRun);

/// <summary>
/// Opt-in, bounded, DB-aware reconciler for private v36 video outputs.
/// This never recursively deletes, follows links, starts tools, hydrates
/// sources, or modifies photo state. An exclusive control-row lock and
/// disabled+paused+no-running-jobs preconditions prevent a publisher from
/// committing while an operator is reclaiming old files.
/// </summary>
public sealed class VideoDerivativeRecoveryJanitor(PostgresCatalogueDatabase database)
{
    public async Task<VideoDerivativeRecoveryReport> SweepAsync(
        string cacheRoot, TimeSpan minimumAge, int maximumFiles = 32,
        bool apply = false, CancellationToken token = default)
    {
        if (minimumAge < TimeSpan.FromHours(48) ||
            minimumAge > TimeSpan.FromHours(720) ||
            maximumFiles is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(minimumAge),
                "Derivative recovery requires 48..720 hours and 1..64 files.");

        string root = await VideoDerivativeCacheGuard.ValidateAsync(
            database, cacheRoot, token);
        string derivativeRoot = Path.Combine(root, "video-derivatives");
        if (Directory.Exists(derivativeRoot))
            VideoDerivativeCacheGuard.AssertNoLinks(derivativeRoot);
        else if (File.Exists(derivativeRoot))
            throw new IOException("The private derivative directory is not a directory.");

        // All operator work happens under the same v35 control-row lock
        // observed by job claims and the v36 publisher. Refuse even dry-run
        // on an enabled lane so preview and apply have equivalent semantics.
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using (NpgsqlCommand control = connection.CreateCommand())
        {
            control.Transaction = transaction;
            control.CommandText = """
                SELECT NOT enabled AND paused
                FROM video_processing_control
                WHERE singleton_id=1 FOR UPDATE;
                """;
            if (await control.ExecuteScalarAsync(token) is not true)
                throw new InvalidOperationException(
                    "Derivative recovery requires an explicitly stopped, disabled and paused lane.");
        }

        await using (NpgsqlCommand running = connection.CreateCommand())
        {
            running.Transaction = transaction;
            running.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM video_processing_jobs
                    WHERE status='running');
                """;
            if (await running.ExecuteScalarAsync(token) is not false)
                throw new InvalidOperationException(
                    "Recover or cancel all running video leases before derivative cleanup.");
        }

        if (!Directory.Exists(derivativeRoot))
        {
            await transaction.CommitAsync(token);
            return new(0, 0, 0, 0, 0, 0, !apply);
        }

        int considered = 0, eligible = 0, deleted = 0, protectedCount = 0, refused = 0;
        int invalidatedRegistered = 0;
        DateTime cutoff = DateTime.UtcNow - minimumAge;

        // Discover the entire bounded top-level candidate set BEFORE any
        // filesystem mutation. Exceeding an operator limit must not cause
        // a partial apply, where earlier files have already been removed.
        int examinedEntries = 0;
        List<(string File, Guid JobId)> candidates = [];
        foreach (string jobDir in Directory.EnumerateFileSystemEntries(derivativeRoot))
        {
            token.ThrowIfCancellationRequested();
            if (++examinedEntries > 256)
                throw new InvalidOperationException("Too many private derivative entries.");
            if (!Directory.Exists(jobDir) ||
                (File.GetAttributes(jobDir) & FileAttributes.ReparsePoint) != 0 ||
                !TryOpaqueGuid(Path.GetFileName(jobDir), out Guid jobId))
            {
                refused++;
                continue;
            }

            foreach (string file in Directory.EnumerateFileSystemEntries(jobDir))
            {
                token.ThrowIfCancellationRequested();
                if (++examinedEntries > 256 || ++considered > maximumFiles)
                    throw new InvalidOperationException(
                        "Derivative scan budget exceeded; nothing was deleted.");
                candidates.Add((file, jobId));
            }
        }

        foreach ((string file, Guid jobId) in candidates)
        {
            token.ThrowIfCancellationRequested();
            string filename = Path.GetFileName(file);
            if (!File.Exists(file) ||
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 ||
                filename.Length != 36 ||
                !filename.EndsWith(".mp4", StringComparison.Ordinal) ||
                !TryOpaqueGuid(filename[..32], out Guid leaseToken))
            {
                refused++;
                continue;
            }

            string relative = "video-derivatives/" +
                jobId.ToString("N") + "/" + filename;
            if (!VideoDerivativeArtifactPath.IsCanonical(relative, jobId, leaseToken) ||
                File.GetLastWriteTimeUtc(file) > cutoff)
            {
                protectedCount++;
                continue;
            }

            // This observation includes excluded/deleted source receipts.
            // Never delete any registered evidence via orphan recovery.
            (bool known, string status, bool registered, bool current) =
                await InspectAsync(connection, transaction, jobId, relative, token);
            if (!known || status is "running" or "queued" || registered)
            {
                protectedCount++;
                if (registered && !current) invalidatedRegistered++;
                continue;
            }

            eligible++;
            if (!apply) continue;

            // Check once more after DB work: refuse a replaced link.
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Video output changed during cleanup.");
            File.Delete(file);
            deleted++;
        }

        await transaction.CommitAsync(token);
        return new(considered, eligible, deleted, protectedCount, refused,
            invalidatedRegistered, !apply);
    }

    private static async Task<(bool Known, string Status, bool Registered, bool Current)>
        InspectAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            Guid jobId, string relative, CancellationToken token)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT job.status,
              EXISTS (
                SELECT 1 FROM video_published_derivatives receipt
                WHERE receipt.job_id=job.id AND receipt.artifact_relative_path=@path),
              EXISTS (
                SELECT 1 FROM video_published_derivatives receipt
                JOIN asset_revisions revision ON revision.id=receipt.asset_revision_id
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN video_revision_metadata metadata
                  ON metadata.asset_revision_id=revision.id
                WHERE receipt.job_id=job.id AND receipt.artifact_relative_path=@path
                  AND job.status='succeeded' AND job.asset_revision_id=revision.id
                  AND revision.media_kind='video' AND asset.media_kind='video'
                  AND asset.deleted_at_utc IS NULL
                  AND metadata.inspection_status='supported'
                  AND revision.id=(
                    SELECT latest.id FROM asset_revisions latest
                    WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                    ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                  AND NOT EXISTS (
                    SELECT 1 FROM source_copy_exclusions excluded
                    WHERE excluded.source_id=asset.source_id
                      AND excluded.source_key=asset.source_key))
            FROM video_processing_jobs job WHERE job.id=@job FOR UPDATE OF job;
            """;
        command.Parameters.AddWithValue("job", jobId);
        command.Parameters.AddWithValue("path", relative);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return (false, "", false, false);
        return (true, reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    private static bool TryOpaqueGuid(string value, out Guid id)
    {
        id = Guid.Empty;
        return value.Length == 32 && value.All(c =>
            c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
            Guid.TryParseExact(value, "N", out id);
    }
}
