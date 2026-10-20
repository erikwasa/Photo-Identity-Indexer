using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>No source metadata, private media locations or authentication material.</summary>
public sealed record VideoScopedAttemptGrant(
    Guid JobId, long Generation, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Explicit-call v37 operator grant. This is NOT a hosted worker or a
/// general video enable switch. The grant can authorize only the exact
/// pre-existing queued job and expires, even after a process crash.
/// </summary>
public sealed class PostgresVideoScopedAttemptControl(PostgresCatalogueDatabase database)
{
    public async Task<VideoScopedAttemptGrant?> ArmAsync(
        Guid jobId, string expectedToolSha256, string expectedPreset,
        TimeSpan authorizationWindow, CancellationToken token = default)
    {
        if (jobId == Guid.Empty || !VideoDerivativeArtifactPath.IsHash(expectedToolSha256) ||
            string.IsNullOrWhiteSpace(expectedPreset) ||
            authorizationWindow < TimeSpan.FromMinutes(1) ||
            authorizationWindow > TimeSpan.FromMinutes(15))
            throw new ArgumentException("Selected video job, tool, preset and bounded window required.");

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(token);

        // Source lock precedes control lock: the same order as fenced
        // publication and source exclusion, preventing deadlock inversions.
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
            if (await source.ExecuteScalarAsync(token) is not Guid) return null;
        }

        await using (NpgsqlCommand control = connection.CreateCommand())
        {
            control.Transaction = tx;
            control.CommandText = """
                SELECT NOT enabled AND paused AND selected_job_id IS NULL
                FROM video_processing_control
                WHERE singleton_id=1 FOR UPDATE;
                """;
            if (await control.ExecuteScalarAsync(token) is not true)
                return null;
        }

        VideoPhotoPrioritySnapshot priority =
            await PostgresVideoPhotoPriorityProbe.ReadAsync(connection, tx, token);
        if (priority.PhotoWorkActive) return null;

        await using (NpgsqlCommand active = connection.CreateCommand())
        {
            active.Transaction = tx;
            active.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM video_processing_jobs WHERE status='running');
                """;
            if (await active.ExecuteScalarAsync(token) is not false) return null;
        }

        await using (NpgsqlCommand selected = connection.CreateCommand())
        {
            selected.Transaction = tx;
            selected.CommandText = """
                SELECT EXISTS(
                  SELECT 1 FROM video_processing_jobs job
                  JOIN asset_revisions revision ON revision.id=job.asset_revision_id
                  JOIN assets asset ON asset.id=revision.asset_id
                  JOIN video_revision_metadata metadata
                    ON metadata.asset_revision_id=revision.id
                  WHERE job.id=@job AND job.status='queued'
                    AND job.derivative_kind='PlayableProxy'
                    AND job.tool_sha256=@tool AND job.preset_version=@preset
                    AND job.attempt_count < 3
                    AND job.available_at_utc<=now()
                    AND job.cancellation_requested_at_utc IS NULL
                    AND revision.media_kind='video'
                    AND asset.media_kind='video'
                    AND revision.size_bytes=job.source_bytes
                    AND asset.deleted_at_utc IS NULL
                    AND metadata.inspection_status='supported'
                    AND revision.id=(
                        SELECT latest.id FROM asset_revisions latest
                        WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                        ORDER BY latest.observed_at_utc DESC,latest.id DESC LIMIT 1)
                    AND NOT EXISTS (
                      SELECT 1 FROM source_copy_exclusions excluded
                      WHERE excluded.source_id=asset.source_id
                        AND excluded.source_key=asset.source_key)
                );
                """;
            selected.Parameters.AddWithValue("job", jobId);
            selected.Parameters.AddWithValue("tool", expectedToolSha256);
            selected.Parameters.AddWithValue("preset", expectedPreset);
            if (await selected.ExecuteScalarAsync(token) is not true)
                return null;
        }

        await using NpgsqlCommand grant = connection.CreateCommand();
        grant.Transaction = tx;
        grant.CommandText = """
            UPDATE video_processing_control
            SET enabled=true, paused=false, selected_job_id=@job,
                authorization_expires_at_utc=now()+(@seconds * interval '1 second'),
                generation=generation+1, last_action='arm-selected',
                updated_at_utc=now()
            WHERE singleton_id=1 AND NOT enabled AND paused
              AND selected_job_id IS NULL
            RETURNING generation, authorization_expires_at_utc;
            """;
        grant.Parameters.AddWithValue("job", jobId);
        grant.Parameters.AddWithValue("seconds", authorizationWindow.TotalSeconds);
        await using NpgsqlDataReader reader = await grant.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("Selected video grant was lost.");
        var result = new VideoScopedAttemptGrant(jobId, reader.GetInt64(0),
            reader.GetFieldValue<DateTimeOffset>(1));
        await reader.CloseAsync();
        await tx.CommitAsync(token);
        return result;
    }

    /// <summary>
    /// Disarms only the still-matching grant, preserves ALL unrelated queued
    /// jobs, and requests cancellation of its still-running selected lease.
    /// Expiration/restart also prevents further claims without disarm.
    /// </summary>
    public async Task<bool> DisarmAsync(
        VideoScopedAttemptGrant grant, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(token);
        await using (NpgsqlCommand close = connection.CreateCommand())
        {
            close.Transaction = tx;
            close.CommandText = """
                UPDATE video_processing_control
                SET enabled=false, paused=true, selected_job_id=NULL,
                    authorization_expires_at_utc=NULL,
                    generation=generation+1, last_action='disarm-selected',
                    updated_at_utc=now()
                WHERE singleton_id=1 AND selected_job_id=@job
                  AND generation=@generation;
                """;
            close.Parameters.AddWithValue("job", grant.JobId);
            close.Parameters.AddWithValue("generation", grant.Generation);
            if (await close.ExecuteNonQueryAsync(token) != 1) return false;
        }
        await using (NpgsqlCommand cancel = connection.CreateCommand())
        {
            cancel.Transaction = tx;
            cancel.CommandText = """
                UPDATE video_processing_jobs
                SET cancellation_requested_at_utc=
                        COALESCE(cancellation_requested_at_utc,now()),
                    updated_at_utc=now()
                WHERE id=@job AND status='running'
                  AND control_generation=@generation;
                """;
            cancel.Parameters.AddWithValue("job", grant.JobId);
            cancel.Parameters.AddWithValue("generation", grant.Generation);
            await cancel.ExecuteNonQueryAsync(token);
        }
        await tx.CommitAsync(token);
        return true;
    }
}
