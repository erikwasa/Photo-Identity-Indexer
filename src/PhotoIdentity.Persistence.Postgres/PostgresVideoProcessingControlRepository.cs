using Npgsql;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>Only sanitized, persisted video-lane controls; no photo state.</summary>
public sealed record VideoProcessingControlState(
    bool Enabled, bool Paused, long Generation, string LastAction);

public sealed record VideoStopResult(int QueuedCancelled, int RunningCancellationRequested);

/// <summary>
/// Durable, video-only control. Disabled+paused on every fresh or upgraded
/// catalogue. Caller requests cannot enable processing; a later reviewed
/// activation gate will have to implement explicit enablement separately.
/// </summary>
public sealed class PostgresVideoProcessingControlRepository(PostgresCatalogueDatabase database)
{
    public async Task<VideoProcessingControlState> GetAsync(CancellationToken token = default)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT enabled, paused, generation, last_action
            FROM video_processing_control WHERE singleton_id = 1;
            """;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("Video lane control row is missing: fail closed.");
        return new(reader.GetBoolean(0), reader.GetBoolean(1),
            reader.GetInt64(2), reader.GetString(3));
    }

    /// <summary>Pause new video claims; the future worker must also observe
    /// generation changes and interrupt any running subprocess safely.</summary>
    public Task<VideoProcessingControlState> PauseAsync(CancellationToken token = default) =>
        SetPausedAsync(paused: true, "pause", token);

    /// <summary>Remove the pause veto, but never enable an otherwise disabled
    /// video lane. Future explicit enablement must be a separately reviewed gate.</summary>
    public Task<VideoProcessingControlState> ResumeAsync(CancellationToken token = default) =>
        SetPausedAsync(paused: false, "resume", token);

    private async Task<VideoProcessingControlState> SetPausedAsync(
        bool paused, string action, CancellationToken token)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE video_processing_control
            SET paused=@paused, last_action=@action, updated_at_utc=now(),
                generation=generation+1
            WHERE singleton_id=1
            RETURNING enabled, paused, generation, last_action;
            """;
        command.Parameters.AddWithValue("paused", paused);
        command.Parameters.AddWithValue("action", action);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("Video lane control row is missing: fail closed.");
        return new(reader.GetBoolean(0), reader.GetBoolean(1),
            reader.GetInt64(2), reader.GetString(3));
    }

    /// <summary>Atomically disable+pause admission and cancel all queued jobs.
    /// Running jobs receive cancellation intent; this does not kill subprocesses
    /// because none is registered yet. Existing photo jobs remain untouched.</summary>
    public async Task<VideoStopResult> StopAsync(CancellationToken token = default)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using (NpgsqlCommand control = connection.CreateCommand())
        {
            control.Transaction = transaction;
            control.CommandText = """
                UPDATE video_processing_control
                SET enabled=false, paused=true, last_action='stop',
                    generation=generation+1, updated_at_utc=now()
                WHERE singleton_id=1;
                """;
            if (await control.ExecuteNonQueryAsync(token) != 1)
                throw new InvalidOperationException("Video lane control row is missing: fail closed.");
        }

        int queued, running;
        await using (NpgsqlCommand cancel = connection.CreateCommand())
        {
            cancel.Transaction = transaction;
            cancel.CommandText = """
                WITH updated AS (
                    UPDATE video_processing_jobs
                    SET cancellation_requested_at_utc =
                            COALESCE(cancellation_requested_at_utc, now()),
                        status=CASE WHEN status='queued' THEN 'cancelled' ELSE status END,
                        completed_at_utc=CASE WHEN status='queued' THEN now()
                                              ELSE completed_at_utc END,
                        updated_at_utc=now()
                    WHERE status IN ('queued','running')
                    RETURNING status
                )
                SELECT
                    count(*) FILTER (WHERE status='cancelled')::integer,
                    count(*) FILTER (WHERE status='running')::integer
                FROM updated;
                """;
            await using NpgsqlDataReader reader = await cancel.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
                throw new InvalidOperationException("Video cancellation summary unavailable.");
            queued = reader.GetInt32(0);
            running = reader.GetInt32(1);
        }
        await transaction.CommitAsync(token);
        return new VideoStopResult(queued, running);
    }

    /// <summary>Reads under a row lock shared with the serialized video claim
    /// transaction; concurrent pause/stop cannot overtake the claim decision.
    /// Fails closed for missing rows or inaccessible state.</summary>
    internal static async Task<VideoProcessingControlState> ReadForClaimAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT enabled, paused, generation, last_action
            FROM video_processing_control
            WHERE singleton_id=1 FOR SHARE;
            """;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new InvalidOperationException("Video lane control row is missing: fail closed.");
        return new(reader.GetBoolean(0), reader.GetBoolean(1),
            reader.GetInt64(2), reader.GetString(3));
    }
}
