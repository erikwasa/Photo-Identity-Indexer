using Npgsql;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Private receipt for a completed video artifact, with no exposed storage
/// location or media source path. Visibility is still subject to exclusion,
/// deletion and current-revision checks. No browser endpoint consumes it.
/// </summary>
public sealed record VideoPublishedDerivativeReceipt(
    Guid JobId, Guid RevisionId, string Sha256, long EncodedBytes);

/// <summary>
/// Transactional PostgreSQL success fence for an app-owned *already materialized*
/// local artifact. Only this repository can mark a video job succeeded.
/// Callers must first verify the output bytes and perform same-volume atomic
/// rename into the private derivative root. Database commit can fail AFTER the
/// rename, leaving an invisible orphan for a future conservative janitor; no
/// consumer may assume disk and PostgreSQL are one distributed transaction.
/// </summary>
/// <summary>
/// A known, non-ambiguous registered-byte quota refusal. No receipt has
/// committed; callers may safely discard their *owned* temporary output.
/// This is not a hard filesystem quota or a signal to delete originals.
/// </summary>
public sealed class VideoProxyPublicationQuotaExceededException()
    : InvalidOperationException("Video proxy publication exceeds its configured registered-byte quota.");

public sealed class PostgresVideoPublishedDerivativeRepository(PostgresCatalogueDatabase database)
{
    internal async Task<bool> TryCommitAsync(
        VideoProcessingJobLease lease, string artifactRelativePath,
        string sha256, long encodedBytes, CancellationToken token = default,
        VideoProxyRetentionLimits? publicationQuota = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!VideoDerivativeArtifactPath.IsCanonical(artifactRelativePath,
                lease.Id, lease.LeaseToken) ||
            !VideoDerivativeArtifactPath.IsHash(sha256) ||
            encodedBytes < 1 || encodedBytes > 2147483648L ||
            encodedBytes > lease.TemporaryBytes)
            throw new ArgumentException("Video derivative evidence is not bounded or canonical.");

        publicationQuota?.Validate();
        if (publicationQuota is not null &&
            encodedBytes > publicationQuota.MaximumProxyBytes)
            throw new VideoProxyPublicationQuotaExceededException();

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);

        // Lock the source FIRST, as with exclusion insertion, queue finish,
        // and video revision registration. Never allow publication to race
        // a source-copy exclusion's source FK KEY SHARE lock.
        await using (NpgsqlCommand source = connection.CreateCommand())
        {
            source.Transaction = transaction;
            source.CommandText = """
                SELECT source.id FROM video_processing_jobs job
                JOIN asset_revisions revision ON revision.id=job.asset_revision_id
                JOIN assets asset ON asset.id=revision.asset_id
                JOIN sources source ON source.id=asset.source_id
                WHERE job.id=@id AND job.lease_token=@lease
                  AND job.status='running' AND source.kind='local-folder'
                FOR UPDATE OF source;
                """;
            source.Parameters.AddWithValue("id", lease.Id);
            source.Parameters.AddWithValue("lease", lease.LeaseToken);
            if (await source.ExecuteScalarAsync(token) is not Guid)
                return false;
        }

        // A paused/stopped control row locks out this commit. The shared
        // control lock also serializes a concurrent generation increment.
        await using (NpgsqlCommand control = connection.CreateCommand())
        {
            control.Transaction = transaction;
            control.CommandText = """
                SELECT generation FROM video_processing_control
                WHERE singleton_id=1 AND enabled AND NOT paused
                  AND generation=@generation
                  AND (selected_job_id IS NULL OR
                       (selected_job_id=@job
                        AND authorization_expires_at_utc>now()))
                FOR UPDATE;
                """;
            control.Parameters.AddWithValue("generation", lease.ControlGeneration);
            control.Parameters.AddWithValue("job", lease.Id);
            if (await control.ExecuteScalarAsync(token) is not long)
                return false;
        }

        // The control-row exclusive lock serializes every registered proxy
        // publisher (and stopped-lane maintenance). This sum includes stale
        // receipts because their physical bytes may still be retained.
        // A known quota refusal happens BEFORE job success/receipt insertion.
        // Unknown unregistered/orphan files remain outside this policy.
        if (publicationQuota is not null)
        {
            await using NpgsqlCommand occupied = connection.CreateCommand();
            occupied.Transaction = transaction;
            occupied.CommandText = """
                SELECT COALESCE(SUM(encoded_bytes),0)::bigint
                FROM video_published_derivatives;
                """;
            long registeredBytes = (long)(await occupied.ExecuteScalarAsync(token)
                ?? throw new InvalidOperationException("Video registered-byte total unavailable."));
            if (registeredBytes < 0 ||
                registeredBytes > publicationQuota.MaximumCacheBytes - encodedBytes)
                throw new VideoProxyPublicationQuotaExceededException();
        }

        // Source, cancellation, latest revision, inspection and lease are
        // rechecked while the source/control locks are held. The UPDATE row
        // lock fences concurrent expiry recovery and operator cancellation.
        await using (NpgsqlCommand complete = connection.CreateCommand())
        {
            complete.Transaction = transaction;
            complete.CommandText = """
                UPDATE video_processing_jobs job
                SET status='succeeded', progress_percent=100,
                    completed_at_utc=now(), updated_at_utc=now(),
                    lease_token=NULL, leased_until_utc=NULL, failure_code=NULL
                WHERE job.id=@id AND job.lease_token=@lease
                  AND job.asset_revision_id=@revision
                  AND job.derivative_kind=@kind
                  AND job.policy_version=@policy
                  AND job.preset_version=@preset
                  AND job.tool_sha256=@tool
                  AND job.source_bytes=@source_bytes
                  AND job.reserved_temporary_bytes=@temporary_bytes
                  AND job.requested_frames=@frames
                  AND job.reserved_temporary_bytes>=@encoded_bytes
                  AND job.control_generation=@generation
                  AND job.status='running' AND job.leased_until_utc>now()
                  AND job.cancellation_requested_at_utc IS NULL
                  AND EXISTS (
                    SELECT 1 FROM asset_revisions revision
                    JOIN assets asset ON asset.id=revision.asset_id
                    JOIN video_revision_metadata metadata
                      ON metadata.asset_revision_id=revision.id
                    WHERE revision.id=job.asset_revision_id
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
                RETURNING job.id;
                """;
            complete.Parameters.AddWithValue("id", lease.Id);
            complete.Parameters.AddWithValue("lease", lease.LeaseToken);
            complete.Parameters.AddWithValue("revision", lease.RevisionId.Value);
            complete.Parameters.AddWithValue("kind", lease.DerivativeKind);
            complete.Parameters.AddWithValue("policy", lease.PolicyVersion);
            complete.Parameters.AddWithValue("preset", lease.PresetVersion);
            complete.Parameters.AddWithValue("tool", lease.ToolSha256);
            complete.Parameters.AddWithValue("source_bytes", lease.SourceBytes);
            complete.Parameters.AddWithValue("temporary_bytes", lease.TemporaryBytes);
            complete.Parameters.AddWithValue("frames", lease.RequestedFrames);
            complete.Parameters.AddWithValue("encoded_bytes", encodedBytes);
            complete.Parameters.AddWithValue("generation", lease.ControlGeneration);
            if (await complete.ExecuteScalarAsync(token) is not Guid)
                return false;
        }

        await using (NpgsqlCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO video_published_derivatives
                    (job_id, asset_revision_id, artifact_relative_path,
                     content_sha256, encoded_bytes)
                VALUES (@job,@revision,@path,@sha,@bytes);
                """;
            insert.Parameters.AddWithValue("job", lease.Id);
            insert.Parameters.AddWithValue("revision", lease.RevisionId.Value);
            insert.Parameters.AddWithValue("path", artifactRelativePath);
            insert.Parameters.AddWithValue("sha", sha256);
            insert.Parameters.AddWithValue("bytes", encodedBytes);
            await insert.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<VideoPublishedDerivativeReceipt?> GetCurrentAsync(
        Guid jobId, CancellationToken token = default)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT record.job_id, record.asset_revision_id,
                   record.content_sha256, record.encoded_bytes
            FROM video_published_derivatives record
            JOIN video_processing_jobs job ON job.id=record.job_id
            JOIN asset_revisions revision ON revision.id=record.asset_revision_id
            JOIN assets asset ON asset.id=revision.asset_id
            JOIN video_revision_metadata metadata ON metadata.asset_revision_id=revision.id
            WHERE record.job_id=@id AND job.status='succeeded'
              AND metadata.inspection_status='supported'
              AND job.asset_revision_id=record.asset_revision_id
              AND revision.media_kind='video' AND asset.media_kind='video'
              AND asset.deleted_at_utc IS NULL
              AND revision.id=(
                SELECT latest.id FROM asset_revisions latest
                WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
              AND NOT EXISTS (
                SELECT 1 FROM source_copy_exclusions excluded
                WHERE excluded.source_id=asset.source_id
                  AND excluded.source_key=asset.source_key);
            """;
        command.Parameters.AddWithValue("id", jobId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        return new(reader.GetGuid(0), reader.GetGuid(1),
            reader.GetString(2), reader.GetInt64(3));
    }
}

/// <summary>Opaque, fixed-format internal key; never contains source paths.</summary>
public static class VideoDerivativeArtifactPath
{
    public static string ForLease(VideoProcessingJobLease lease) =>
        "video-derivatives/" + lease.Id.ToString("N") + "/" +
        lease.LeaseToken.ToString("N") + ".mp4";

    public static bool IsCanonical(string? value, Guid job, Guid token) =>
        job != Guid.Empty && token != Guid.Empty &&
        string.Equals(value,
            "video-derivatives/" + job.ToString("N") + "/" +
            token.ToString("N") + ".mp4", StringComparison.Ordinal);

    public static bool IsHash(string? value) =>
        value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
