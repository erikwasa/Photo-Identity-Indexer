using Npgsql;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Read-only, sanitized, single-statement safety observation for a previously
/// claimed video lease. Does not initialize schema, claim work, inspect files,
/// hydrate OneDrive media, or write photo/video state. Any database error is
/// a failed observation and callers must terminate execution.
/// </summary>
public sealed class PostgresVideoLeaseSafetyProbe(PostgresCatalogueDatabase database)
{
    public async Task<VideoExecutionPermit> CheckAsync(
        VideoProcessingJobLease lease, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.Id == Guid.Empty || lease.LeaseToken == Guid.Empty ||
            lease.ControlGeneration < 0)
            return VideoExecutionPermit.Deny("lease-invalid");

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT control.enabled, control.paused, control.generation,
                   EXISTS (
                       SELECT 1 FROM archive_advancement_control
                       WHERE desired_state='running' OR sync_required
                   ) OR EXISTS (
                       SELECT 1 FROM processing_runs
                       WHERE status IN ('pending', 'running')
                   ) OR EXISTS (
                       SELECT 1 FROM processing_jobs job
                       JOIN processing_runs run ON run.id=job.processing_run_id
                       WHERE job.status IN ('queued', 'running')
                         AND run.status IN ('pending', 'running')
                   ) AS photo_busy,
                   EXISTS (
                       SELECT 1 FROM video_processing_jobs job
                       JOIN asset_revisions revision ON revision.id=job.asset_revision_id
                       JOIN assets asset ON asset.id=revision.asset_id
                       JOIN video_revision_metadata metadata
                         ON metadata.asset_revision_id=revision.id
                       JOIN sources source ON source.id=asset.source_id
                       WHERE job.id=@job AND job.lease_token=@lease
                         AND job.asset_revision_id=@revision
                         AND job.status='running'
                         AND job.control_generation=@generation
                         AND job.cancellation_requested_at_utc IS NULL
                         AND job.leased_until_utc>now()
                         AND job.source_bytes=revision.size_bytes
                         AND revision.media_kind='video'
                         AND asset.media_kind='video'
                         AND asset.deleted_at_utc IS NULL
                         AND source.kind='local-folder'
                         AND metadata.inspection_status='supported'
                         AND revision.id=(
                            SELECT latest.id FROM asset_revisions latest
                            WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                            ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
                         AND NOT EXISTS (
                            SELECT 1 FROM source_copy_exclusions excluded
                            WHERE excluded.source_id=asset.source_id
                              AND excluded.source_key=asset.source_key)
                   ) AS current_lease,
                   (control.selected_job_id IS NULL OR
                    (control.selected_job_id=@job
                     AND control.authorization_expires_at_utc>now())) AS scope_valid
            FROM video_processing_control control
            WHERE control.singleton_id=1;
            """;
        command.Parameters.AddWithValue("job", lease.Id);
        command.Parameters.AddWithValue("lease", lease.LeaseToken);
        command.Parameters.AddWithValue("revision", lease.RevisionId.Value);
        command.Parameters.AddWithValue("generation", lease.ControlGeneration);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            return VideoExecutionPermit.Deny("control-unavailable");

        if (!reader.GetBoolean(0)) return VideoExecutionPermit.Deny("video-disabled");
        if (reader.GetBoolean(1)) return VideoExecutionPermit.Deny("video-paused");
        if (reader.GetInt64(2) != lease.ControlGeneration)
            return VideoExecutionPermit.Deny("control-generation-changed");
        if (reader.GetBoolean(3))
            return VideoExecutionPermit.Deny("photo-priority");
        if (!reader.GetBoolean(4))
            return VideoExecutionPermit.Deny("lease-invalid");
        if (!reader.GetBoolean(5))
            return VideoExecutionPermit.Deny("selected-authorization-expired");
        return VideoExecutionPermit.Ready;
    }
}
