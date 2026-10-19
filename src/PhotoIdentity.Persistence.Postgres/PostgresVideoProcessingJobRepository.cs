using Npgsql;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>Sanitized control-only state; never contains original paths or source metadata.</summary>
public sealed record VideoProcessingJobState(
    Guid Id, AssetRevisionId RevisionId, string Status, int Attempts,
    int ProgressPercent, bool CancellationRequested, string? FailureCode);

/// <summary>
/// A database lease is a fence for state transitions, NOT permission to open or
/// publish source-derived files. The future worker must reverify local bytes,
/// exclusion and current revision under the appropriate source lock.
/// </summary>
public sealed record VideoProcessingJobLease(
    Guid Id, AssetRevisionId RevisionId, Guid LeaseToken,
    string DerivativeKind, string PolicyVersion, string PresetVersion,
    string ToolSha256, long SourceBytes, long TemporaryBytes, int RequestedFrames);

/// <summary>
/// WI-0188 durable job control. No hosted worker, public endpoint, file I/O,
/// OneDrive hydration or FFmpeg execution is registered by this repository.
/// </summary>
public sealed class PostgresVideoProcessingJobRepository(PostgresCatalogueDatabase database)
{
    private const int MaximumAttempts = 3;
    private const long ClaimAdvisoryLock = 504091708;
    private readonly PostgresSourceCopyExclusionRepository _exclusions = new(database);

    /// <summary>
    /// Record a deliberately selected, already locally verified video. The
    /// caller's availability assertion is NOT sufficient: the catalogue is
    /// independently checked under the source/exclusion serialization lock.
    /// A repeated key returns the original (possibly terminal) job unchanged.
    /// </summary>
    public async Task<Guid> EnqueueAsync(
        VideoDerivativeJobIdentity identity, VideoProcessingBudget budget,
        VideoProcessingCandidate candidate, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(candidate);
        var admission = VideoProcessingAdmissionPolicy.Evaluate(
            budget, candidate, new VideoProcessingActivity(false, 0, 0));
        if (!admission.Admitted)
            throw new InvalidOperationException("Video enqueue denied: " + admission.ReasonCode);

        await _exclusions.EnsureReadyAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);

        // The same source lock is used by video registration and poster publication;
        // a new source-copy exclusion's FK key-share lock cannot cross this lock.
        await using (NpgsqlCommand source = connection.CreateCommand())
        {
            source.Transaction = transaction;
            source.CommandText = """
                SELECT source.id
                FROM asset_revisions revision
                JOIN assets asset ON asset.id = revision.asset_id
                JOIN sources source ON source.id = asset.source_id
                WHERE revision.id = @revision AND revision.media_kind = 'video'
                  AND asset.media_kind = 'video' AND asset.deleted_at_utc IS NULL
                  AND source.kind = 'local-folder'
                FOR UPDATE OF source;
                """;
            source.Parameters.AddWithValue("revision", identity.RevisionId.Value);
            if (await source.ExecuteScalarAsync(token) is not Guid)
                throw new InvalidOperationException("The video revision is not currently accessible.");
        }

        await using (NpgsqlCommand authorized = connection.CreateCommand())
        {
            authorized.Transaction = transaction;
            authorized.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM asset_revisions revision
                    JOIN assets asset ON asset.id = revision.asset_id
                    JOIN video_revision_metadata metadata ON metadata.asset_revision_id = revision.id
                    WHERE revision.id = @revision
                      AND revision.size_bytes = @source_bytes
                      AND revision.media_kind = 'video' AND asset.media_kind = 'video'
                      AND asset.deleted_at_utc IS NULL
                      AND metadata.inspection_status = 'supported'
                      AND revision.id = (
                          SELECT latest.id FROM asset_revisions latest
                          WHERE latest.asset_id = asset.id AND latest.media_kind = 'video'
                          ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                      AND NOT EXISTS (
                          SELECT 1 FROM source_copy_exclusions excluded
                          WHERE excluded.source_id = asset.source_id
                            AND excluded.source_key = asset.source_key)
                );
                """;
            authorized.Parameters.AddWithValue("revision", identity.RevisionId.Value);
            authorized.Parameters.AddWithValue("source_bytes", candidate.SourceBytes);
            if (await authorized.ExecuteScalarAsync(token) is not true)
                throw new InvalidOperationException("Video revision changed, was excluded or lacks supported evidence.");
        }

        Guid proposedId = Guid.NewGuid();
        await using (NpgsqlCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO video_processing_jobs (
                    id, asset_revision_id, idempotency_key, derivative_kind,
                    policy_version, preset_version, tool_sha256,
                    source_bytes, reserved_temporary_bytes, requested_frames)
                VALUES (@id, @revision, @key, @kind, @policy, @preset, @tool,
                        @source_bytes, @temporary_bytes, @frames)
                ON CONFLICT (idempotency_key) DO NOTHING;
                """;
            insert.Parameters.AddWithValue("id", proposedId);
            insert.Parameters.AddWithValue("revision", identity.RevisionId.Value);
            insert.Parameters.AddWithValue("key", identity.IdempotencyKey());
            insert.Parameters.AddWithValue("kind", identity.Kind.ToString());
            insert.Parameters.AddWithValue("policy", identity.PolicyVersion);
            insert.Parameters.AddWithValue("preset", identity.PresetVersion);
            insert.Parameters.AddWithValue("tool", identity.ToolSha256);
            insert.Parameters.AddWithValue("source_bytes", candidate.SourceBytes);
            insert.Parameters.AddWithValue("temporary_bytes", candidate.ExpectedTemporaryBytes);
            insert.Parameters.AddWithValue("frames", candidate.RequestedFrames);
            await insert.ExecuteNonQueryAsync(token);
        }
        await using NpgsqlCommand find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = """
            SELECT id FROM video_processing_jobs WHERE idempotency_key = @key;
            """;
        find.Parameters.AddWithValue("key", identity.IdempotencyKey());
        Guid id = (Guid)(await find.ExecuteScalarAsync(token)
            ?? throw new InvalidOperationException("The job could not be found after insertion."));
        await transaction.CommitAsync(token);
        return id;
    }

    /// <summary>
    /// Recover expired leases; a late worker's old token no longer authorizes
    /// acknowledgement. Three expired attempts become a bounded terminal failure.
    /// </summary>
    public async Task<int> RecoverExpiredAsync(CancellationToken token = default)
    {
        await database.InitializeAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE video_processing_jobs
            SET status = CASE
                    WHEN cancellation_requested_at_utc IS NOT NULL THEN 'cancelled'
                    WHEN attempt_count >= 3 THEN 'failed'
                    ELSE 'queued' END,
                lease_token = NULL,
                leased_until_utc = NULL,
                completed_at_utc = CASE
                    WHEN cancellation_requested_at_utc IS NOT NULL OR attempt_count >= 3
                    THEN now() ELSE NULL END,
                failure_code = CASE WHEN cancellation_requested_at_utc IS NULL
                    AND attempt_count >= 3 THEN 'attempt-limit' ELSE NULL END,
                updated_at_utc = now(),
                available_at_utc = now()
            WHERE status = 'running' AND leased_until_utc <= now();
            """;
        return await command.ExecuteNonQueryAsync(token);
    }

    /// <summary>
    /// Atomic, global serialized claim respects concurrent job count and
    /// temporary reservations. No source is opened or hydrated. Photo-priority
    /// must be supplied by the separate runtime coordinator and checked again.
    /// </summary>
    public async Task<VideoProcessingJobLease?> TryClaimNextAsync(
        VideoProcessingBudget budget, bool photoWorkActive,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.Validate();
        if (!budget.Enabled || budget.Paused || photoWorkActive) return null;

        await _exclusions.EnsureReadyAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);

        await using (NpgsqlCommand serialize = connection.CreateCommand())
        {
            serialize.Transaction = transaction;
            serialize.CommandText = "SELECT pg_advisory_xact_lock(@key);";
            serialize.Parameters.AddWithValue("key", ClaimAdvisoryLock);
            await serialize.ExecuteNonQueryAsync(token);
        }

        // Recover inside the claim lock to prevent competing claim/recovery races.
        await using (NpgsqlCommand recover = connection.CreateCommand())
        {
            recover.Transaction = transaction;
            recover.CommandText = """
                UPDATE video_processing_jobs
                SET status = CASE
                        WHEN cancellation_requested_at_utc IS NOT NULL THEN 'cancelled'
                        WHEN attempt_count >= 3 THEN 'failed'
                        ELSE 'queued' END,
                    lease_token = NULL, leased_until_utc = NULL,
                    completed_at_utc = CASE
                        WHEN cancellation_requested_at_utc IS NOT NULL OR attempt_count >= 3
                        THEN now() ELSE NULL END,
                    failure_code = CASE WHEN cancellation_requested_at_utc IS NULL
                        AND attempt_count >= 3 THEN 'attempt-limit' ELSE NULL END,
                    updated_at_utc = now(), available_at_utc = now()
                WHERE status = 'running' AND leased_until_utc <= now();
                """;
            await recover.ExecuteNonQueryAsync(token);
        }

        long reserved;
        int active;
        await using (NpgsqlCommand count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = """
                SELECT count(*)::integer, COALESCE(sum(reserved_temporary_bytes),0)::bigint
                FROM video_processing_jobs WHERE status = 'running';
                """;
            await using NpgsqlDataReader reader = await count.ExecuteReaderAsync(token);
            await reader.ReadAsync(token);
            active = reader.GetInt32(0);
            reserved = reader.GetInt64(1);
        }
        if (active >= budget.MaximumConcurrentJobs ||
            reserved >= budget.MaximumTemporaryBytes)
        {
            await transaction.CommitAsync(token);
            return null;
        }
        long available = budget.MaximumTemporaryBytes - reserved;

        Guid id = Guid.Empty, revisionId = Guid.Empty;
        string kind = "", policy = "", preset = "", hash = "";
        long sourceBytes = 0, temporaryBytes = 0;
        int frames = 0;
        bool found;
        await using (NpgsqlCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT job.id, job.asset_revision_id, job.derivative_kind,
                       job.policy_version, job.preset_version, job.tool_sha256,
                       job.source_bytes, job.reserved_temporary_bytes, job.requested_frames
                FROM video_processing_jobs job
                JOIN asset_revisions revision ON revision.id = job.asset_revision_id
                JOIN assets asset ON asset.id = revision.asset_id
                JOIN video_revision_metadata metadata ON metadata.asset_revision_id = revision.id
                WHERE job.status = 'queued' AND job.cancellation_requested_at_utc IS NULL
                  AND job.attempt_count < 3 AND job.available_at_utc <= now()
                  AND job.source_bytes <= @max_source
                  AND job.reserved_temporary_bytes <= @remaining
                  AND job.requested_frames <= @max_frames
                  AND revision.media_kind = 'video' AND asset.media_kind = 'video'
                  AND revision.size_bytes = job.source_bytes
                  AND asset.deleted_at_utc IS NULL
                  AND metadata.inspection_status = 'supported'
                  AND revision.id = (
                      SELECT latest.id FROM asset_revisions latest
                      WHERE latest.asset_id = asset.id AND latest.media_kind = 'video'
                      ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                  AND NOT EXISTS (
                      SELECT 1 FROM source_copy_exclusions excluded
                      WHERE excluded.source_id = asset.source_id
                        AND excluded.source_key = asset.source_key)
                ORDER BY job.requested_at_utc, job.id
                FOR UPDATE OF job SKIP LOCKED
                LIMIT 1;
                """;
            select.Parameters.AddWithValue("max_source", budget.MaximumSourceBytes);
            select.Parameters.AddWithValue("remaining", available);
            select.Parameters.AddWithValue("max_frames", budget.MaximumExtractedFrames);
            await using NpgsqlDataReader reader = await select.ExecuteReaderAsync(token);
            found = await reader.ReadAsync(token);
            if (found)
            {
                id = reader.GetGuid(0);
                revisionId = reader.GetGuid(1);
                kind = reader.GetString(2);
                policy = reader.GetString(3);
                preset = reader.GetString(4);
                hash = reader.GetString(5);
                sourceBytes = reader.GetInt64(6);
                temporaryBytes = reader.GetInt64(7);
                frames = reader.GetInt32(8);
            }
        }
        if (!found)
        {
            await transaction.CommitAsync(token);
            return null;
        }

        Guid lease = Guid.NewGuid();
        await using (NpgsqlCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE video_processing_jobs
                SET status='running', attempt_count=attempt_count+1,
                    lease_token=@lease, leased_until_utc=now()+interval '60 seconds',
                    updated_at_utc=now(), progress_percent=0
                WHERE id=@id AND status='queued';
                """;
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("lease", lease);
            if (await update.ExecuteNonQueryAsync(token) != 1)
                throw new InvalidOperationException("Video job lease changed during claim.");
        }
        await transaction.CommitAsync(token);
        return new VideoProcessingJobLease(id, AssetRevisionId.From(revisionId),
            lease, kind, policy, preset, hash, sourceBytes, temporaryBytes, frames);
    }

    public async Task<bool> RequestCancellationAsync(Guid jobId, CancellationToken token = default)
    {
        await database.InitializeAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE video_processing_jobs
            SET cancellation_requested_at_utc = COALESCE(cancellation_requested_at_utc, now()),
                status=CASE WHEN status='queued' THEN 'cancelled' ELSE status END,
                completed_at_utc=CASE WHEN status='queued' THEN now() ELSE completed_at_utc END,
                updated_at_utc=now()
            WHERE id=@id AND status IN ('queued','running');
            """;
        command.Parameters.AddWithValue("id", jobId);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    public async Task<bool> TryHeartbeatAsync(
        Guid jobId, Guid leaseToken, int progressPercent,
        CancellationToken token = default)
    {
        if (progressPercent is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(progressPercent));
        await database.InitializeAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE video_processing_jobs
            SET progress_percent=GREATEST(progress_percent, @progress),
                leased_until_utc=now()+interval '60 seconds',
                updated_at_utc=now()
            WHERE id=@id AND lease_token=@lease AND status='running'
              AND leased_until_utc>now() AND cancellation_requested_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("id", jobId);
        command.Parameters.AddWithValue("lease", leaseToken);
        command.Parameters.AddWithValue("progress", progressPercent);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    /// <summary>Fenced state acknowledgement only; NOT atomic derivative publication.</summary>
    public async Task<bool> TryFinishAttemptAsync(
        Guid jobId, Guid leaseToken, bool succeeded, bool retryable,
        CancellationToken token = default)
    {
        await _exclusions.EnsureReadyAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE video_processing_jobs job
            SET status=CASE
                    WHEN @success THEN 'succeeded'
                    WHEN @retryable AND attempt_count<3 THEN 'queued'
                    ELSE 'failed' END,
                progress_percent=CASE WHEN @success THEN 100
                    WHEN @retryable AND attempt_count<3 THEN 0
                    ELSE progress_percent END,
                failure_code=CASE
                    WHEN @success OR (@retryable AND attempt_count<3) THEN NULL
                    WHEN attempt_count>=3 THEN 'attempt-limit'
                    ELSE 'worker-error' END,
                available_at_utc=CASE WHEN @retryable AND NOT @success AND attempt_count<3
                    THEN now()+interval '5 seconds' ELSE available_at_utc END,
                lease_token=NULL, leased_until_utc=NULL,
                completed_at_utc=CASE WHEN @success OR NOT @retryable OR attempt_count>=3
                    THEN now() ELSE NULL END,
                updated_at_utc=now()
            WHERE job.id=@id AND job.lease_token=@lease AND job.status='running'
              AND job.leased_until_utc>now() AND job.cancellation_requested_at_utc IS NULL
              AND EXISTS (
                  SELECT 1 FROM asset_revisions revision
                  JOIN assets asset ON asset.id=revision.asset_id
                  WHERE revision.id=job.asset_revision_id
                    AND revision.media_kind='video' AND asset.media_kind='video'
                    AND asset.deleted_at_utc IS NULL
                    AND revision.id=(
                        SELECT latest.id FROM asset_revisions latest
                        WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                        ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                    AND NOT EXISTS (
                        SELECT 1 FROM source_copy_exclusions excluded
                        WHERE excluded.source_id=asset.source_id
                          AND excluded.source_key=asset.source_key)
              );
            """;
        command.Parameters.AddWithValue("id", jobId);
        command.Parameters.AddWithValue("lease", leaseToken);
        command.Parameters.AddWithValue("success", succeeded);
        command.Parameters.AddWithValue("retryable", retryable);
        return await command.ExecuteNonQueryAsync(token) == 1;
    }

    public async Task<VideoProcessingJobState?> GetAsync(
        Guid jobId, CancellationToken token = default)
    {
        await database.InitializeAsync(token);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT job.id, job.asset_revision_id, job.status, job.attempt_count,
                   job.progress_percent, job.cancellation_requested_at_utc IS NOT NULL,
                   job.failure_code
            FROM video_processing_jobs job
            JOIN asset_revisions revision ON revision.id=job.asset_revision_id
            JOIN assets asset ON asset.id=revision.asset_id
            WHERE job.id=@id AND asset.media_kind='video' AND revision.media_kind='video'
              AND asset.deleted_at_utc IS NULL
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id=asset.source_id
                    AND excluded.source_key=asset.source_key);
            """;
        command.Parameters.AddWithValue("id", jobId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        return new VideoProcessingJobState(
            reader.GetGuid(0), AssetRevisionId.From(reader.GetGuid(1)),
            reader.GetString(2), reader.GetInt32(3),
            reader.GetInt32(4), reader.GetBoolean(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }
}
