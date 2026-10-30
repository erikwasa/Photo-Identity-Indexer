using System.Data;
using Npgsql;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Aggregate-only, read-only WI-0189 projection of registered playable proxies.
/// This is NOT a filesystem quota or authorization to unlink a file.
/// </summary>
public sealed record VideoProxyRetentionPreviewReport(
    bool DryRun, bool Admitted, string Reason,
    int RegisteredRecords, int PinnedRecords,
    long RegisteredBytesBefore, long IncomingReservedBytes,
    int PlannedEvictions, long PlannedReclaimedBytes,
    long ProjectedRegisteredBytes);

/// <summary>
/// Builds a bounded retention preview from a consistent PostgreSQL receipt
/// snapshot. No source file, media decoder, OneDrive API, publisher, job
/// state or registered derivative is modified. Physical deletion remains
/// under the independent, stopped-lane maintenance boundary.
/// </summary>
public sealed class PostgresVideoProxyRetentionPreview(PostgresCatalogueDatabase database)
{
    public async Task<VideoProxyRetentionPreviewReport> PreviewAsync(
        string cacheRoot, VideoProxyRetentionLimits limits, long incomingBytes,
        TimeSpan? minimumAge = null, int maximumRecords = 1024,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        TimeSpan age = minimumAge ?? TimeSpan.FromHours(48);
        if (maximumRecords is < 1 or > 2048 ||
            age < TimeSpan.FromHours(24) || age > TimeSpan.FromHours(720))
            throw new ArgumentOutOfRangeException(nameof(maximumRecords),
                "Preview requires 1..2048 records and 24..720 hours of minimum retention age.");

        // The same source-overlap, ownership-marker and reparse guards used
        // by publication; never infer a cache from an archive or sync root.
        string root = await VideoDerivativeCacheGuard.ValidateAsync(
            database, cacheRoot, token);

        await using NpgsqlConnection connection =
            await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await using (NpgsqlCommand control = connection.CreateCommand())
        {
            control.Transaction = transaction;
            control.CommandText = """
                SELECT NOT enabled AND paused FROM video_processing_control
                WHERE singleton_id=1 FOR SHARE;
                """;
            if (await control.ExecuteScalarAsync(token) is not true)
                throw new InvalidOperationException(
                    "Retention preview requires disabled and paused video processing.");
        }
        await using (NpgsqlCommand running = connection.CreateCommand())
        {
            running.Transaction = transaction;
            running.CommandText = """
                SELECT EXISTS(SELECT 1 FROM video_processing_jobs WHERE status='running');
                """;
            if (await running.ExecuteScalarAsync(token) is not false)
                throw new InvalidOperationException(
                    "Retention preview requires no running video leases.");
        }

        List<VideoProxyRetentionEntry> entries = [];
        long total = 0;
        int pinned = 0;
        bool inconsistent = false, limitExceeded = false;
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - age;

        // Do not silently omit a tail of receipts: a partial inventory
        // cannot justify any admission or eviction recommendation.
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT record.job_id, record.artifact_relative_path,
                       record.encoded_bytes, record.created_at_utc,
                       (job.status='succeeded'
                        AND job.derivative_kind='PlayableProxy'
                        AND metadata.inspection_status='supported'
                        AND job.asset_revision_id=record.asset_revision_id
                        AND revision.media_kind='video'
                        AND asset.media_kind='video'
                        AND asset.deleted_at_utc IS NULL
                        AND source.kind='local-folder'
                        AND revision.id=(
                            SELECT latest.id FROM asset_revisions latest
                            WHERE latest.asset_id=asset.id
                              AND latest.media_kind='video'
                            ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                        AND NOT EXISTS (
                            SELECT 1 FROM source_copy_exclusions excluded
                            WHERE excluded.source_id=asset.source_id
                              AND excluded.source_key=asset.source_key)
                       ) AS is_current,
                       record.content_sha256
                FROM video_published_derivatives record
                JOIN video_processing_jobs job ON job.id=record.job_id
                JOIN asset_revisions revision ON revision.id=record.asset_revision_id
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN sources source ON source.id=asset.source_id
                JOIN video_revision_metadata metadata
                    ON metadata.asset_revision_id=revision.id
                ORDER BY record.job_id
                LIMIT @limit;
                """;
            command.Parameters.AddWithValue("limit", maximumRecords + 1);
            await using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                if (entries.Count == maximumRecords)
                {
                    limitExceeded = true;
                    break;
                }

                Guid id = reader.GetGuid(0);
                string relative = reader.GetString(1);
                long bytes = reader.GetInt64(2);
                DateTimeOffset created = new(
                    DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc));
                bool current = reader.GetBoolean(4);
                string sha = reader.GetString(5);
                bool isPinned = created > cutoff;

                // The database only supplies opaque paths of its own fixed
                // publication format. Any mismatch makes the entire preview
                // non-admissible; never inspect arbitrary recorded paths.
                if (!current || !VideoDerivativeArtifactPath.IsHash(sha) ||
                    bytes < 1 || !TryRegisteredPath(root, id, relative, out string file) ||
                    !TryMatchFileLength(file, bytes))
                    inconsistent = true;

                if (total > long.MaxValue - Math.Max(bytes, 0))
                    inconsistent = true;
                else if (bytes > 0)
                    total += bytes;
                if (isPinned) pinned++;
                entries.Add(new(id, bytes, created, isPinned));
            }
        }
        await transaction.CommitAsync(token);

        VideoProxyRetentionPreviewReport Failed(string reason) =>
            new(true, false, reason, entries.Count, pinned, total,
                incomingBytes, 0, 0, total);

        if (limitExceeded) return Failed("inventory-limit-exceeded");
        if (inconsistent) return Failed("unverifiable-registered-inventory");

        VideoProxyRetentionPlan plan =
            VideoProxyRetentionPolicy.Plan(limits, entries, incomingBytes);
        if (!plan.Admitted) return Failed(plan.Reason);

        long reclaimed = total - (plan.RemainingBytes - incomingBytes);
        return new(true, true, plan.Reason, entries.Count, pinned,
            total, incomingBytes, plan.EvictionIds.Count, reclaimed,
            plan.RemainingBytes);
    }

    private static bool TryRegisteredPath(
        string root, Guid job, string relative, out string file)
    {
        file = "";
        string[] components = relative.Split('/');
        if (components.Length != 3 ||
            !Guid.TryParseExact(components[1], "N", out Guid recordedJob) ||
            recordedJob != job ||
            components[2].Length != 36 ||
            !Guid.TryParseExact(components[2][..32], "N", out Guid lease) ||
            !VideoDerivativeArtifactPath.IsCanonical(relative, job, lease))
            return false;

        file = Path.Combine(root, "video-derivatives", job.ToString("N"),
            lease.ToString("N") + ".mp4");
        return true;
    }

    private static bool TryMatchFileLength(string file, long expected)
    {
        try
        {
            string parent = Path.GetDirectoryName(file)!;
            if (!Directory.Exists(parent) || !File.Exists(file))
                return false;
            VideoDerivativeCacheGuard.AssertNoLinks(parent);
            VideoDerivativeCacheGuard.AssertNoLinks(file);
            return new FileInfo(file).Length == expected;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
