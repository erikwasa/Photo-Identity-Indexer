using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Aggregate-only operator evidence. A registered byte total is not a hard
/// filesystem quota; invalid/unknown files may remain outside the registry.
/// </summary>
public sealed record VideoRegisteredMaintenanceReport(
    bool DryRun, int Inspected, int Planned, int Invalid, int Missing,
    int Corrupt, int QuotaSelected, int Revoked, int FilesDeleted,
    int DeleteFailures, long RegisteredBytesBefore, long ProjectedBytesAfter,
    bool MoreCandidates, string? PlanFingerprint = null);

/// <summary>
/// Bounded, explicitly invoked registered-derivative maintenance. PostgreSQL
/// is authoritative: remove the receipt and mark its job failed/revision-invalid
/// in ONE transaction before unlinking private bytes. File deletion cannot be
/// part of the DB transaction; failed unlinks become invisible orphans for the
/// pre-existing stopped-lane orphan janitor. Never deletes a photo or source.
/// </summary>
public sealed class VideoRegisteredDerivativeMaintenance(PostgresCatalogueDatabase database)
{
    private sealed record Entry(
        Guid JobId, string RelativePath, string Sha256, long Bytes,
        DateTime CreatedAtUtc, bool Current, string File, bool Missing,
        bool Corrupt, bool SelectedQuota);

    public async Task<VideoRegisteredMaintenanceReport> SweepAsync(
        string cacheRoot, int maximumRecords = 16, long? quotaMiB = null,
        TimeSpan? minimumQuotaAge = null, bool verifyHashes = false,
        bool apply = false, CancellationToken token = default,
        string? expectedPlanFingerprint = null)
    {
        TimeSpan minAge = minimumQuotaAge ?? TimeSpan.FromHours(48);
        if (maximumRecords is < 1 or > 32 ||
            quotaMiB is < 64 or > 16384 ||
            minAge < TimeSpan.FromHours(24) || minAge > TimeSpan.FromHours(720))
            throw new ArgumentOutOfRangeException(nameof(maximumRecords),
                "Maintenance is limited to 1..32 records, 64..16384 MiB and 24..720 hours.");

        long? quota = quotaMiB is null ? null : checked(quotaMiB.Value * 1024L * 1024);
        string root = await VideoDerivativeCacheGuard.ValidateAsync(database, cacheRoot, token);
        string derivatives = Path.Combine(root, "video-derivatives");
        if (Directory.Exists(derivatives))
            VideoDerivativeCacheGuard.AssertNoLinks(derivatives);
        else if (File.Exists(derivatives))
            throw new IOException("The private derivative location is not an owned directory.");

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);

        // Exclusive control lock synchronizes admission, in-flight publication
        // and this maintenance run; never accept merely a paused enabled lane.
        await using (NpgsqlCommand control = connection.CreateCommand())
        {
            control.Transaction = transaction;
            control.CommandText = """
                SELECT NOT enabled AND paused FROM video_processing_control
                WHERE singleton_id=1 FOR UPDATE;
                """;
            if (await control.ExecuteScalarAsync(token) is not true)
                throw new InvalidOperationException(
                    "Registered maintenance requires a stopped disabled+paused video lane.");
        }
        await using (NpgsqlCommand running = connection.CreateCommand())
        {
            running.Transaction = transaction;
            running.CommandText = """
                SELECT EXISTS(SELECT 1 FROM video_processing_jobs WHERE status='running');
                """;
            if (await running.ExecuteScalarAsync(token) is not false)
                throw new InvalidOperationException(
                    "All video jobs must be terminal or queued before maintenance.");
        }

        long total;
        await using (NpgsqlCommand sum = connection.CreateCommand())
        {
            sum.Transaction = transaction;
            sum.CommandText = """
                SELECT COALESCE(SUM(encoded_bytes),0)::bigint
                FROM video_published_derivatives;
                """;
            total = (long)(await sum.ExecuteScalarAsync(token)
                ?? throw new InvalidOperationException("Derivative registry total unavailable."));
        }

        List<Entry> entries = [];
        int inspected = 0, invalid = 0, missing = 0, corrupt = 0, quotaSelected = 0;
        long projected = total;
        bool moreCandidates = false;
        DateTime cutoff = DateTime.UtcNow - minAge;

        // Invalidated first, then oldest currently registered. The result
        // remains strictly bounded regardless of table size. Source locks
        // serialize exclusion insertions and revision changes while the
        // receipt/job state is observed and later revoked.
        await using (NpgsqlCommand candidates = connection.CreateCommand())
        {
            candidates.Transaction = transaction;
            candidates.CommandText = """
                SELECT record.job_id, record.artifact_relative_path,
                       record.content_sha256, record.encoded_bytes,
                       record.created_at_utc, job.status,
                       (job.status='succeeded'
                        AND metadata.inspection_status='supported'
                        AND job.asset_revision_id=record.asset_revision_id
                        AND revision.media_kind='video'
                        AND asset.media_kind='video'
                        AND asset.deleted_at_utc IS NULL
                        AND revision.id=(
                            SELECT latest.id FROM asset_revisions latest
                            WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                            ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                        AND NOT EXISTS (
                            SELECT 1 FROM source_copy_exclusions excluded
                            WHERE excluded.source_id=asset.source_id
                              AND excluded.source_key=asset.source_key)) AS is_current
                FROM video_published_derivatives record
                JOIN video_processing_jobs job ON job.id=record.job_id
                JOIN asset_revisions revision ON revision.id=record.asset_revision_id
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN sources source ON source.id=asset.source_id
                JOIN video_revision_metadata metadata
                    ON metadata.asset_revision_id=revision.id
                ORDER BY is_current ASC, record.created_at_utc, record.job_id
                LIMIT @count FOR UPDATE OF job, source, record;
                """;
            candidates.Parameters.AddWithValue("count", maximumRecords + 1);
            // Do not try a nested connection command while the reader is open.
            await using NpgsqlDataReader reader = await candidates.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                if (++inspected > maximumRecords)
                {
                    moreCandidates = true;
                    break;
                }
                Guid job = reader.GetGuid(0);
                string relative = reader.GetString(1);
                if (!TryPath(relative, job, root, out string file))
                    throw new InvalidDataException(
                        "Noncanonical registered video artifact: no maintenance writes were made.");
                bool current = reader.GetBoolean(6);
                if (reader.GetString(5) != "succeeded")
                    throw new InvalidOperationException(
                        "Registered derivative does not belong to a succeeded job.");
                string sha = reader.GetString(2);
                long bytes = reader.GetInt64(3);
                DateTime created = reader.GetDateTime(4);
                if (!VideoDerivativeArtifactPath.IsHash(sha) || bytes < 1)
                    throw new InvalidDataException("Invalid registered video evidence.");

                // Candidate receipt paths must NEVER route through a junction.
                string parent = Path.GetDirectoryName(file)!;
                if (Directory.Exists(parent))
                    VideoDerivativeCacheGuard.AssertNoLinks(parent);
                else if (File.Exists(parent))
                    throw new IOException("Registered derivative parent is not a directory.");

                bool missingFile = !File.Exists(file);
                bool corruptFile = false;
                if (!missingFile)
                {
                    VideoDerivativeCacheGuard.AssertNoLinks(file);
                    using FileStream stream = new(file, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 65536, FileOptions.SequentialScan);
                    corruptFile = stream.Length != bytes;
                    if (!corruptFile && verifyHashes)
                    {
                        string actual = Convert.ToHexString(
                            await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
                        corruptFile = actual != sha;
                    }
                }

                bool quotaCandidate = current && !missingFile && !corruptFile &&
                    quota is long target && projected > target &&
                    created <= cutoff;
                bool select = !current || missingFile || corruptFile || quotaCandidate;
                if (select)
                {
                    entries.Add(new(job, relative, sha, bytes, created, current,
                        file, missingFile, corruptFile, quotaCandidate));
                    projected -= bytes;
                    if (!current) invalid++;
                    if (missingFile) missing++;
                    if (corruptFile) corrupt++;
                    if (quotaCandidate) quotaSelected++;
                }
            }
        }

        // A confirmation is bound to the precise bounded victim set,
        // registered-byte snapshot, quota parameters and cache root.
        // Five-minute time-bucket expiry forces a new preview after delay.
        // This is a stale-plan guard, not an authorization credential.
        // Legacy operator CLI calls without a fingerprint keep their
        // existing explicit --apply behavior; HTTP always requires one.
        string? fingerprint = entries.Count > 0
            ? Fingerprint(root, entries, maximumRecords, quota, minAge,
                verifyHashes, total, projected, moreCandidates)
            : null;
        if (apply && expectedPlanFingerprint is not null &&
            (!IsFingerprint(expectedPlanFingerprint) ||
             fingerprint is null ||
             !string.Equals(fingerprint, expectedPlanFingerprint,
                 StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Video retention preview changed or expired; rerun the dry run.");

        // Dry run never changes a DB receipt or filesystem. When the budget
        // is exhausted callers may run another bounded sweep.
        if (!apply)
        {
            await transaction.CommitAsync(token);
            return new(true, inspected > maximumRecords ? maximumRecords : inspected,
                entries.Count, invalid, missing, corrupt, quotaSelected, 0, 0, 0,
                total, projected, moreCandidates, fingerprint);
        }

        foreach (Entry entry in entries)
        {
            token.ThrowIfCancellationRequested();
            await using NpgsqlCommand revoke = connection.CreateCommand();
            revoke.Transaction = transaction;
            revoke.CommandText = """
                WITH removed AS (
                    DELETE FROM video_published_derivatives
                    WHERE job_id=@job AND artifact_relative_path=@path
                    RETURNING job_id
                ), invalidated AS (
                    UPDATE video_processing_jobs job
                    SET status='failed', failure_code='revision-invalid',
                        progress_percent=0, completed_at_utc=now(),
                        lease_token=NULL, leased_until_utc=NULL, updated_at_utc=now()
                    WHERE job.id IN (SELECT job_id FROM removed)
                      AND job.status='succeeded'
                    RETURNING job.id
                )
                SELECT (SELECT count(*) FROM removed)::integer,
                       (SELECT count(*) FROM invalidated)::integer;
                """;
            revoke.Parameters.AddWithValue("job", entry.JobId);
            revoke.Parameters.AddWithValue("path", entry.RelativePath);
            await using NpgsqlDataReader changed = await revoke.ExecuteReaderAsync(token);
            if (!await changed.ReadAsync(token) ||
                changed.GetInt32(0) != 1 || changed.GetInt32(1) != 1)
                throw new InvalidOperationException(
                    "Registered derivative changed during revocation; roll back the maintenance batch.");
        }

        // Revocation MUST commit before the first unlink. A crash after commit
        // can leave only invisible files, which the orphan janitor can reclaim.
        await transaction.CommitAsync(token);
        int deleted = 0, failures = 0;
        foreach (Entry entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Missing) continue;
            try
            {
                // Do not follow linked parents/files even if someone mutates
                // the cache outside the app while we are deleting.
                if (File.Exists(entry.File))
                {
                    VideoDerivativeCacheGuard.AssertNoLinks(
                        Path.GetDirectoryName(entry.File)!);
                    VideoDerivativeCacheGuard.AssertNoLinks(entry.File);
                    File.Delete(entry.File);
                    deleted++;
                }
            }
            catch (IOException) { failures++; }
            catch (UnauthorizedAccessException) { failures++; }
        }
        return new(false, inspected > maximumRecords ? maximumRecords : inspected,
            entries.Count, invalid, missing, corrupt, quotaSelected,
            entries.Count, deleted, failures, total, projected, moreCandidates);
    }

    private static bool IsFingerprint(string? fingerprint) =>
        fingerprint is { Length: 64 } &&
        fingerprint.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Fingerprint(
        string root, IReadOnlyList<Entry> victims, int maximumRecords,
        long? quota, TimeSpan minimumAge, bool verifyHashes, long total,
        long projected, bool moreCandidates)
    {
        // Never put original paths or file names in the operator report.
        // The hash binds exactly the DB receipt and file validation result
        // observed by the existing stopped-lane maintenance scan.
        var payload = new StringBuilder("wi0189-retention-reclaim-v1");
        void Part(string part) => payload.Append('|').Append(part.Length)
            .Append(':').Append(part);
        Part(root);
        Part(maximumRecords.ToString(CultureInfo.InvariantCulture));
        Part(quota?.ToString(CultureInfo.InvariantCulture) ?? "none");
        Part(minimumAge.Ticks.ToString(CultureInfo.InvariantCulture));
        Part(verifyHashes ? "sha256" : "length");
        Part(total.ToString(CultureInfo.InvariantCulture));
        Part(projected.ToString(CultureInfo.InvariantCulture));
        Part(moreCandidates ? "more" : "complete");
        Part((DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300)
            .ToString(CultureInfo.InvariantCulture));
        foreach (Entry e in victims)
        {
            Part(e.JobId.ToString("N"));
            Part(e.RelativePath);
            Part(e.Sha256);
            Part(e.Bytes.ToString(CultureInfo.InvariantCulture));
            Part(e.CreatedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            Part(e.Current ? "current" : "stale");
            Part(e.Missing ? "missing" : "present");
            Part(e.Corrupt ? "corrupt" : "verified");
            Part(e.SelectedQuota ? "quota" : "invalid");
        }
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Deliberate regeneration of a revoked exact identity. Resets no source
    /// bytes and claims no work; the lane remains stopped and disabled.
    /// Source/revision/inspection/exclusion are all checked in PostgreSQL.
    /// </summary>
    public async Task<bool> RequeueRevokedAsync(Guid jobId, CancellationToken token = default)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("Specify a nonempty job ID.");
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(token);
        await using (NpgsqlCommand control = connection.CreateCommand())
        {
            control.Transaction = tx;
            control.CommandText = """
                SELECT NOT enabled AND paused FROM video_processing_control
                WHERE singleton_id=1 FOR UPDATE;
                """;
            if (await control.ExecuteScalarAsync(token) is not true)
                throw new InvalidOperationException("Video lane must be fully stopped for requeue.");
        }
        await using (NpgsqlCommand active = connection.CreateCommand())
        {
            active.Transaction = tx;
            active.CommandText = """
                SELECT EXISTS(SELECT 1 FROM video_processing_jobs WHERE status='running');
                """;
            if (await active.ExecuteScalarAsync(token) is not false)
                throw new InvalidOperationException("Running leases must finish before requeue.");
        }
        // The active source row is locked to serialize exclusion changes.
        await using (NpgsqlCommand source = connection.CreateCommand())
        {
            source.Transaction = tx;
            source.CommandText = """
                SELECT source.id FROM video_processing_jobs job
                JOIN asset_revisions revision ON revision.id=job.asset_revision_id
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN sources source ON source.id=asset.source_id
                WHERE job.id=@job AND source.kind='local-folder'
                FOR UPDATE OF source;
                """;
            source.Parameters.AddWithValue("job", jobId);
            if (await source.ExecuteScalarAsync(token) is not Guid) return false;
        }

        await using NpgsqlCommand reset = connection.CreateCommand();
        reset.Transaction = tx;
        reset.CommandText = """
            UPDATE video_processing_jobs job
            SET status='queued', failure_code=NULL,
                attempt_count=0, progress_percent=0, completed_at_utc=NULL,
                cancellation_requested_at_utc=NULL, lease_token=NULL,
                leased_until_utc=NULL, available_at_utc=now(), updated_at_utc=now()
            WHERE job.id=@job AND job.status='failed'
              AND job.failure_code='revision-invalid'
              AND job.derivative_kind='PlayableProxy'
              AND NOT EXISTS (
                  SELECT 1 FROM video_published_derivatives record
                  WHERE record.job_id=job.id)
              AND EXISTS (
                  SELECT 1 FROM asset_revisions revision
                  JOIN assets asset ON asset.id=revision.asset_id
                  JOIN video_revision_metadata metadata
                    ON metadata.asset_revision_id=revision.id
                  WHERE revision.id=job.asset_revision_id
                    AND revision.media_kind='video' AND asset.media_kind='video'
                    AND asset.deleted_at_utc IS NULL
                    AND metadata.inspection_status='supported'
                    AND revision.size_bytes=job.source_bytes
                    AND revision.id=(
                        SELECT latest.id FROM asset_revisions latest
                        WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                        ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                    AND NOT EXISTS (
                        SELECT 1 FROM source_copy_exclusions excluded
                        WHERE excluded.source_id=asset.source_id
                          AND excluded.source_key=asset.source_key));
            """;
        reset.Parameters.AddWithValue("job", jobId);
        bool changed = await reset.ExecuteNonQueryAsync(token) == 1;
        await tx.CommitAsync(token);
        return changed;
    }

    private static bool TryPath(
        string relative, Guid jobId, string root, out string file)
    {
        file = "";
        string[] components = relative.Split('/');
        if (components.Length != 3 ||
            !Guid.TryParseExact(components[1], "N", out Guid job) || job != jobId ||
            components[2].Length != 36 ||
            !Guid.TryParseExact(components[2][..32], "N", out Guid lease) ||
            !VideoDerivativeArtifactPath.IsCanonical(relative, job, lease))
            return false;

        file = Path.Combine(root, "video-derivatives",
            job.ToString("N"), lease.ToString("N") + ".mp4");
        return true;
    }
}
