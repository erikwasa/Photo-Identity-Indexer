using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// Conservatively detects requested or active photo work from the authoritative
/// PostgreSQL control/job state. This is a queue admission gate, not proof that
/// all interactive photo searches, slideshow displays, or other CPU users are idle.
/// </summary>
public sealed record VideoPhotoPrioritySnapshot(
    bool ArchiveWorkRequested,
    bool PhotoRunPendingOrRunning,
    bool PhotoJobQueuedOrRunning)
{
    public bool PhotoWorkActive =>
        ArchiveWorkRequested || PhotoRunPendingOrRunning || PhotoJobQueuedOrRunning;
}

/// <summary>
/// Read-only observer. Never changes or claims photo work, initializes a schema,
/// queues videos, or accesses media. Missing/unavailable state fails closed via
/// the PostgreSQL error rather than being treated as "photos idle".
/// </summary>
public sealed class PostgresVideoPhotoPriorityProbe(PostgresCatalogueDatabase database)
{
    public async Task<VideoPhotoPrioritySnapshot> GetAsync(
        CancellationToken token = default)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        return await ReadAsync(connection, null, token);
    }

    // 'video-processing' is set only by the shared archive worker AFTER its
    // photo work classifier finds no remaining photo work. A new sync request
    // or pending/running photo job is still a hard veto.
    internal static async Task<VideoPhotoPrioritySnapshot> ReadAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction,
        CancellationToken token)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                EXISTS (
                    SELECT 1 FROM archive_advancement_control
                    WHERE sync_required OR
                      (desired_state = 'running' AND
                       runtime_state <> 'video-processing')
                ),
                EXISTS (
                    SELECT 1 FROM processing_runs
                    WHERE status IN ('pending', 'running')
                ),
                EXISTS (
                    SELECT 1 FROM processing_jobs AS job
                    JOIN processing_runs AS run ON run.id = job.processing_run_id
                    WHERE job.status IN ('queued', 'running')
                      AND run.status IN ('pending', 'running')
                );
            """;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("Photo priority observation did not return a row.");
        return new VideoPhotoPrioritySnapshot(
            reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }
}
