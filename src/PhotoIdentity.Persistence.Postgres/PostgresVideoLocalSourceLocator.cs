using Npgsql;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Private local source locator for an exact live video lease. NEVER serialize
/// this result into operator status, logs or API responses: it carries the
/// maintained archive's root, relative key and source digest.
/// </summary>
public sealed record VideoLeasedLocalSource(
    Guid SourceId, string RootLocator, string SourceKey,
    string Sha256, long SizeBytes);

/// <summary>
/// Read-only, non-hydrating PostgreSQL authority used immediately before
/// video input staging. A caller-supplied source path or hash is not trusted:
/// everything is bound to the current revision and leased job.
/// </summary>
public sealed class PostgresVideoLocalSourceLocator(PostgresCatalogueDatabase database)
{
    public async Task<VideoLeasedLocalSource?> ResolveAsync(
        VideoProcessingJobLease lease, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT source.id, source.root_locator, asset.source_key,
                   revision.content_sha256, revision.size_bytes
            FROM video_processing_jobs job
            JOIN asset_revisions revision ON revision.id=job.asset_revision_id
            JOIN assets asset ON asset.id=revision.asset_id
            JOIN sources source ON source.id=asset.source_id
            JOIN video_revision_metadata metadata ON metadata.asset_revision_id=revision.id
            JOIN video_processing_control control ON control.singleton_id=1
            WHERE job.id=@job AND job.lease_token=@lease
              AND job.asset_revision_id=@revision
              AND job.control_generation=@generation
              AND control.enabled AND NOT control.paused
              AND control.generation=job.control_generation
              AND (control.selected_job_id IS NULL OR
                   (control.selected_job_id=job.id
                    AND control.authorization_expires_at_utc>now()))
              AND job.status='running' AND job.cancellation_requested_at_utc IS NULL
              AND job.leased_until_utc>now()
              AND source.kind='local-folder'
              AND revision.media_kind='video' AND asset.media_kind='video'
              AND revision.size_bytes=job.source_bytes
              AND asset.deleted_at_utc IS NULL
              AND metadata.inspection_status='supported'
              AND revision.id=(
                  SELECT latest.id FROM asset_revisions latest
                  WHERE latest.asset_id=asset.id AND latest.media_kind='video'
                  ORDER BY latest.observed_at_utc DESC, latest.id DESC LIMIT 1)
              AND NOT EXISTS (
                  SELECT 1 FROM source_copy_exclusions excluded
                  WHERE excluded.source_id=asset.source_id
                    AND excluded.source_key=asset.source_key);
            """;
        command.Parameters.AddWithValue("job", lease.Id);
        command.Parameters.AddWithValue("lease", lease.LeaseToken);
        command.Parameters.AddWithValue("revision", lease.RevisionId.Value);
        command.Parameters.AddWithValue("generation", lease.ControlGeneration);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return null;
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetInt64(4));
    }
}
