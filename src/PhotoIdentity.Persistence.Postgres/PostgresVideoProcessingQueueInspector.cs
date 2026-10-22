using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Sanitized, read-only operator view of the independent v34 video job queue.
/// No file paths, tags, coordinates, person IDs, source keys or media bytes.
/// Excluded/deleted media are hidden. This never initializes/migrates a database.
/// </summary>
public sealed record VideoProcessingQueueOverview(
    long Queued, long Running, long Succeeded, long Failed, long Cancelled,
    long CancellationRequested, long ExpiredLeases, long ReservedTemporaryBytes);

public sealed record VideoProcessingJobOperatorRow(
    Guid JobId, string Status, int Attempts, int ProgressPercent,
    bool CancellationRequested, string? FailureCode = null);

public sealed class PostgresVideoProcessingQueueInspector(PostgresCatalogueDatabase database)
{
    private const string VisibleVideoJobs = """
        FROM video_processing_jobs job
        JOIN asset_revisions revision ON revision.id = job.asset_revision_id
        JOIN assets asset ON asset.id = revision.asset_id
        WHERE asset.media_kind = 'video' AND revision.media_kind = 'video'
          AND asset.deleted_at_utc IS NULL
          AND NOT EXISTS (
              SELECT 1 FROM source_copy_exclusions excluded
              WHERE excluded.source_id = asset.source_id
                AND excluded.source_key = asset.source_key)
        """;

    public async Task<VideoProcessingQueueOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                count(*) FILTER (WHERE job.status = 'queued'),
                count(*) FILTER (WHERE job.status = 'running'),
                count(*) FILTER (WHERE job.status = 'succeeded'),
                count(*) FILTER (WHERE job.status = 'failed'),
                count(*) FILTER (WHERE job.status = 'cancelled'),
                count(*) FILTER (WHERE job.cancellation_requested_at_utc IS NOT NULL
                                 AND job.status = 'running'),
                count(*) FILTER (WHERE job.status = 'running'
                                 AND job.leased_until_utc <= now()),
                COALESCE(sum(job.reserved_temporary_bytes)
                    FILTER (WHERE job.status = 'running'), 0)::bigint
            """ + "\n" + VisibleVideoJobs;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Video queue summary query did not return a row.");
        return new VideoProcessingQueueOverview(
            reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5),
            reader.GetInt64(6), reader.GetInt64(7));
    }

    public async Task<IReadOnlyList<VideoProcessingJobOperatorRow>> ListRecentAsync(
        int maximumJobs = 20, CancellationToken cancellationToken = default)
    {
        if (maximumJobs is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(maximumJobs));
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT job.id, job.status, job.attempt_count, job.progress_percent,
                   job.cancellation_requested_at_utc IS NOT NULL,
                   job.failure_code
            """ + "\n" + VisibleVideoJobs + "\n" + """
            ORDER BY job.requested_at_utc DESC, job.id DESC
            LIMIT @maximum;
            """;
        command.Parameters.AddWithValue("maximum", maximumJobs);
        List<VideoProcessingJobOperatorRow> rows = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new VideoProcessingJobOperatorRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return rows;
    }
}
