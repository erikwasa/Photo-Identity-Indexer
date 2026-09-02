using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Sqlite;

/// <summary>
/// Stores durable processing runs, leases work and guards worker transitions with lease tokens.
/// </summary>
public sealed class SqliteProcessingRepository : IProcessingExecutionRepository, IProcessingRunRepository
{
    private readonly SqliteCatalogueDatabase _database;

    public SqliteProcessingRepository(SqliteCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <summary>
    /// Creates a pending run and its queued jobs in one transaction.
    /// Repeating the same run ID or idempotency key returns the existing durable rows.
    /// </summary>
    public async Task<CatalogueProcessingBatch> CreateRunAsync(
        CatalogueProcessingRun run,
        IReadOnlyCollection<CatalogueProcessingJob> jobs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(jobs);

        if (run.Status != ProcessingRunStatus.Pending)
        {
            throw new ArgumentException("New processing runs must be pending.", nameof(run));
        }

        foreach (CatalogueProcessingJob job in jobs)
        {
            if (job.ProcessingRunId != run.Id)
            {
                throw new ArgumentException("Every job must belong to the supplied run.", nameof(jobs));
            }

            if (job.Status != ProcessingJobStatus.Queued ||
                job.AttemptCount != 0 ||
                job.Error is not null ||
                job.LeaseToken is not null ||
                job.CheckpointJson is not null)
            {
                throw new ArgumentException("New processing jobs must be clean, unattempted queued jobs.", nameof(jobs));
            }
        }

        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        await InsertRunAsync(connection, transaction, run, cancellationToken);
        foreach (CatalogueProcessingJob job in jobs)
        {
            await InsertJobAsync(connection, transaction, job, cancellationToken);
        }

        CatalogueProcessingRun persistedRun = await ReadRunAsync(
            connection,
            transaction,
            run.Id,
            cancellationToken)
            ?? throw new InvalidOperationException("The processing run was unavailable after it was persisted.");
        IReadOnlyList<CatalogueProcessingJob> persistedJobs = await ReadJobsAsync(
            connection,
            transaction,
            run.Id,
            cancellationToken);

        transaction.Commit();
        return new CatalogueProcessingBatch(persistedRun, persistedJobs);
    }

    public async Task<CatalogueProcessingRun?> GetRunAsync(
        ProcessingRunId id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadRunAsync(connection, transaction: null, id, cancellationToken);
    }

    public async Task<CatalogueProcessingJob?> GetJobAsync(
        ProcessingJobId id,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = JobSelect + " WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    public async Task<IReadOnlyList<CatalogueProcessingJob>> GetJobsAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        return await ReadJobsAsync(connection, transaction: null, runId, cancellationToken);
    }

    public async Task<ProcessingRunSummary> GetRunSummaryAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        CatalogueProcessingRun run = await ReadRunAsync(connection, transaction: null, runId, cancellationToken)
            ?? throw new KeyNotFoundException($"Processing run {runId} was not found.");

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COUNT(*),
                COALESCE(SUM(CASE WHEN status = 'queued' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN status = 'running' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN status = 'succeeded' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN status = 'failed' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN status = 'cancelled' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(attempt_count), 0),
                MIN(CASE WHEN status = 'queued' THEN available_at_utc ELSE NULL END)
            FROM processing_jobs
            WHERE processing_run_id = $run_id;
            """;
        command.Parameters.AddWithValue("$run_id", runId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new ProcessingRunSummary(
            runId,
            run.Status,
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.IsDBNull(7) ? null : ParseTimestamp(reader.GetString(7)));
    }

    /// <summary>
    /// Claims the oldest due queued job or reclaims the oldest expired running job.
    /// The returned token is required for every subsequent transition.
    /// </summary>
    public async Task<CatalogueProcessingJob?> ClaimNextJobAsync(
        ProcessingRunId runId,
        DateTimeOffset claimedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "The lease duration must be positive.");
        }

        DateTimeOffset claimedAt = claimedAtUtc.ToUniversalTime();
        DateTimeOffset leasedUntil = claimedAt.Add(leaseDuration);
        ProcessingLeaseToken leaseToken = ProcessingLeaseToken.New();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        CatalogueProcessingJob? claimed;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE processing_jobs
                SET status = 'running',
                    attempt_count = attempt_count + 1,
                    started_at_utc = $claimed_at_utc,
                    completed_at_utc = NULL,
                    error = NULL,
                    last_failure_kind = CASE
                        WHEN status = 'running' THEN 'transient'
                        ELSE last_failure_kind
                    END,
                    lease_token = $lease_token,
                    leased_until_utc = $leased_until_utc
                WHERE id = (
                    SELECT job.id
                    FROM processing_jobs AS job
                    INNER JOIN processing_runs AS run
                        ON run.id = job.processing_run_id
                    WHERE job.processing_run_id = $run_id
                      AND run.status IN ('pending', 'running')
                      AND run.cancellation_requested_at_utc IS NULL
                      AND (
                          (job.status = 'queued' AND job.available_at_utc <= $claimed_at_utc)
                          OR
                          (job.status = 'running' AND job.leased_until_utc <= $claimed_at_utc)
                      )
                    ORDER BY
                        CASE job.status WHEN 'queued' THEN 0 ELSE 1 END,
                        job.available_at_utc,
                        job.id
                    LIMIT 1
                )
                  AND (
                      (status = 'queued' AND available_at_utc <= $claimed_at_utc)
                      OR
                      (status = 'running' AND leased_until_utc <= $claimed_at_utc)
                  )
                RETURNING id, processing_run_id, asset_revision_id, status, attempt_count,
                          available_at_utc, started_at_utc, completed_at_utc, error,
                          idempotency_key, lease_token, leased_until_utc, checkpoint_json,
                          last_failure_kind;
                """;
            command.Parameters.AddWithValue("$run_id", runId.ToString());
            command.Parameters.AddWithValue("$claimed_at_utc", Format(claimedAt));
            command.Parameters.AddWithValue("$lease_token", leaseToken.ToString());
            command.Parameters.AddWithValue("$leased_until_utc", Format(leasedUntil));

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            claimed = await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
        }

        if (claimed is not null)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE processing_runs
                SET status = 'running', completed_at_utc = NULL, error = NULL
                WHERE id = $run_id AND status = 'pending';
                """;
            command.Parameters.AddWithValue("$run_id", runId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return claimed;
    }

    public Task<CatalogueProcessingJob> RenewLeaseAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        DateTimeOffset renewedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "The lease duration must be positive.");
        }

        DateTimeOffset renewedAt = renewedAtUtc.ToUniversalTime();
        return TransitionLeasedJobAsync(
            jobId,
            leaseToken,
            renewedAt,
            """
            UPDATE processing_jobs
            SET leased_until_utc = $leased_until_utc
            WHERE id = $id
              AND status = 'running'
              AND lease_token = $lease_token
              AND leased_until_utc > $transition_at_utc
            RETURNING id, processing_run_id, asset_revision_id, status, attempt_count,
                      available_at_utc, started_at_utc, completed_at_utc, error,
                      idempotency_key, lease_token, leased_until_utc, checkpoint_json,
                      last_failure_kind;
            """,
            error: null,
            checkpointJson: null,
            failureKind: null,
            retryAtUtc: null,
            leasedUntilUtc: renewedAt.Add(leaseDuration),
            cancellationToken);
    }

    public Task<CatalogueProcessingJob> SaveCheckpointAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        string checkpointJson,
        DateTimeOffset savedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointJson);
        using JsonDocument _ = JsonDocument.Parse(checkpointJson);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "The lease duration must be positive.");
        }

        DateTimeOffset savedAt = savedAtUtc.ToUniversalTime();
        return TransitionLeasedJobAsync(
            jobId,
            leaseToken,
            savedAt,
            """
            UPDATE processing_jobs
            SET checkpoint_json = $checkpoint_json,
                leased_until_utc = $leased_until_utc
            WHERE id = $id
              AND status = 'running'
              AND lease_token = $lease_token
              AND leased_until_utc > $transition_at_utc
            RETURNING id, processing_run_id, asset_revision_id, status, attempt_count,
                      available_at_utc, started_at_utc, completed_at_utc, error,
                      idempotency_key, lease_token, leased_until_utc, checkpoint_json,
                      last_failure_kind;
            """,
            error: null,
            checkpointJson: checkpointJson.Trim(),
            failureKind: null,
            retryAtUtc: null,
            leasedUntilUtc: savedAt.Add(leaseDuration),
            cancellationToken);
    }

    public Task<CatalogueProcessingJob> CompleteJobAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default) =>
        TransitionLeasedJobAsync(
            jobId,
            leaseToken,
            completedAtUtc.ToUniversalTime(),
            """
            UPDATE processing_jobs
            SET status = 'succeeded',
                completed_at_utc = $transition_at_utc,
                error = NULL,
                last_failure_kind = NULL,
                lease_token = NULL,
                leased_until_utc = NULL
            WHERE id = $id
              AND status = 'running'
              AND lease_token = $lease_token
              AND leased_until_utc > $transition_at_utc
            RETURNING id, processing_run_id, asset_revision_id, status, attempt_count,
                      available_at_utc, started_at_utc, completed_at_utc, error,
                      idempotency_key, lease_token, leased_until_utc, checkpoint_json,
                      last_failure_kind;
            """,
            error: null,
            checkpointJson: null,
            failureKind: null,
            retryAtUtc: null,
            leasedUntilUtc: null,
            cancellationToken);

    /// <summary>
    /// Records a classified failure. A retry time returns a transient failure to the queue;
    /// omitting it makes the failure terminal.
    /// </summary>
    public Task<CatalogueProcessingJob> FailJobAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        ProcessingFailureKind failureKind,
        string error,
        DateTimeOffset failedAtUtc,
        DateTimeOffset? retryAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        if (retryAtUtc is not null && failureKind != ProcessingFailureKind.Transient)
        {
            throw new ArgumentException("Only transient failures can be scheduled for retry.", nameof(retryAtUtc));
        }

        string sql = retryAtUtc is null
            ? """
              UPDATE processing_jobs
              SET status = 'failed',
                  completed_at_utc = $transition_at_utc,
                  error = $error,
                  last_failure_kind = $failure_kind,
                  lease_token = NULL,
                  leased_until_utc = NULL
              WHERE id = $id
                AND status = 'running'
                AND lease_token = $lease_token
                AND leased_until_utc > $transition_at_utc
              RETURNING id, processing_run_id, asset_revision_id, status, attempt_count,
                        available_at_utc, started_at_utc, completed_at_utc, error,
                        idempotency_key, lease_token, leased_until_utc, checkpoint_json,
                        last_failure_kind;
              """
            : """
              UPDATE processing_jobs
              SET status = 'queued',
                  available_at_utc = $retry_at_utc,
                  started_at_utc = NULL,
                  completed_at_utc = NULL,
                  error = $error,
                  last_failure_kind = $failure_kind,
                  lease_token = NULL,
                  leased_until_utc = NULL
              WHERE id = $id
                AND status = 'running'
                AND lease_token = $lease_token
                AND leased_until_utc > $transition_at_utc
              RETURNING id, processing_run_id, asset_revision_id, status, attempt_count,
                        available_at_utc, started_at_utc, completed_at_utc, error,
                        idempotency_key, lease_token, leased_until_utc, checkpoint_json,
                        last_failure_kind;
              """;

        return TransitionLeasedJobAsync(
            jobId,
            leaseToken,
            failedAtUtc.ToUniversalTime(),
            sql,
            error.Trim(),
            checkpointJson: null,
            failureKind,
            retryAtUtc,
            leasedUntilUtc: null,
            cancellationToken);
    }

    /// <summary>
    /// Cancels the run and invalidates every queued or active job lease in one transaction.
    /// Completed and failed jobs remain unchanged for reporting.
    /// </summary>
    public async Task<CatalogueProcessingRun> RequestCancellationAsync(
        ProcessingRunId runId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset requestedAt = requestedAtUtc.ToUniversalTime();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        CatalogueProcessingRun run = await ReadRunAsync(connection, transaction, runId, cancellationToken)
            ?? throw new KeyNotFoundException($"Processing run {runId} was not found.");
        if (run.Status is ProcessingRunStatus.Completed or ProcessingRunStatus.Failed or ProcessingRunStatus.Cancelled)
        {
            transaction.Commit();
            return run;
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE processing_runs
                SET status = 'cancelled',
                    cancellation_requested_at_utc = $requested_at_utc,
                    completed_at_utc = $requested_at_utc,
                    error = NULL
                WHERE id = $run_id;
                """;
            command.Parameters.AddWithValue("$run_id", runId.ToString());
            command.Parameters.AddWithValue("$requested_at_utc", Format(requestedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE processing_jobs
                SET status = 'cancelled',
                    completed_at_utc = $requested_at_utc,
                    lease_token = NULL,
                    leased_until_utc = NULL
                WHERE processing_run_id = $run_id
                  AND status IN ('queued', 'running');
                """;
            command.Parameters.AddWithValue("$run_id", runId.ToString());
            command.Parameters.AddWithValue("$requested_at_utc", Format(requestedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueProcessingRun cancelled = await ReadRunAsync(
            connection,
            transaction,
            runId,
            cancellationToken)
            ?? throw new InvalidOperationException("The processing run disappeared while it was cancelled.");
        transaction.Commit();
        return cancelled;
    }

    /// <summary>
    /// Finalizes a run after every job is terminal. Any failed job makes the run fail.
    /// </summary>
    public async Task<CatalogueProcessingRun> CompleteRunAsync(
        ProcessingRunId runId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset completedAt = completedAtUtc.ToUniversalTime();
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteTransaction transaction = connection.BeginTransaction();

        CatalogueProcessingRun run = await ReadRunAsync(connection, transaction, runId, cancellationToken)
            ?? throw new KeyNotFoundException($"Processing run {runId} was not found.");
        if (run.Status is ProcessingRunStatus.Completed or ProcessingRunStatus.Failed or ProcessingRunStatus.Cancelled)
        {
            transaction.Commit();
            return run;
        }

        long unfinished;
        long failed;
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT
                    COALESCE(SUM(CASE WHEN status IN ('queued', 'running') THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN status = 'failed' THEN 1 ELSE 0 END), 0)
                FROM processing_jobs
                WHERE processing_run_id = $run_id;
                """;
            command.Parameters.AddWithValue("$run_id", runId.ToString());

            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            unfinished = reader.GetInt64(0);
            failed = reader.GetInt64(1);
        }

        if (unfinished != 0)
        {
            throw new InvalidOperationException("A processing run cannot complete while jobs remain queued or running.");
        }

        string status = failed == 0 ? "completed" : "failed";
        string? error = failed == 0 ? null : $"{failed} processing job(s) failed.";
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE processing_runs
                SET status = $status, completed_at_utc = $completed_at_utc, error = $error
                WHERE id = $run_id;
                """;
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$completed_at_utc", Format(completedAt));
            command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
            command.Parameters.AddWithValue("$run_id", runId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueProcessingRun persisted = await ReadRunAsync(
            connection,
            transaction,
            runId,
            cancellationToken)
            ?? throw new InvalidOperationException("The processing run disappeared while it was finalized.");
        transaction.Commit();
        return persisted;
    }

    private async Task<CatalogueProcessingJob> TransitionLeasedJobAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        DateTimeOffset transitionAtUtc,
        string sql,
        string? error,
        string? checkpointJson,
        ProcessingFailureKind? failureKind,
        DateTimeOffset? retryAtUtc,
        DateTimeOffset? leasedUntilUtc,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await _database.OpenConnectionAsync(cancellationToken);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", jobId.ToString());
        command.Parameters.AddWithValue("$lease_token", leaseToken.ToString());
        command.Parameters.AddWithValue("$transition_at_utc", Format(transitionAtUtc));
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$checkpoint_json", (object?)checkpointJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$failure_kind", failureKind is null ? DBNull.Value : Format(failureKind.Value));
        command.Parameters.AddWithValue("$retry_at_utc", retryAtUtc is null ? DBNull.Value : Format(retryAtUtc.Value));
        command.Parameters.AddWithValue("$leased_until_utc", leasedUntilUtc is null ? DBNull.Value : Format(leasedUntilUtc.Value));

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ProcessingLeaseLostException(jobId);
        }

        return ReadJob(reader);
    }

    private static async Task InsertRunAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogueProcessingRun run,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO processing_runs (
                id, status, configuration_json, started_at_utc, completed_at_utc,
                error, cancellation_requested_at_utc)
            VALUES ($id, $status, $configuration_json, $started_at_utc, NULL, NULL, NULL)
            ON CONFLICT(id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", run.Id.ToString());
        command.Parameters.AddWithValue("$status", Format(run.Status));
        command.Parameters.AddWithValue("$configuration_json", run.ConfigurationJson);
        command.Parameters.AddWithValue("$started_at_utc", Format(run.StartedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertJobAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CatalogueProcessingJob job,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO processing_jobs (
                id, processing_run_id, asset_revision_id, status, attempt_count,
                available_at_utc, started_at_utc, completed_at_utc, error,
                idempotency_key, lease_token, leased_until_utc, checkpoint_json,
                last_failure_kind)
            VALUES ($id, $processing_run_id, $asset_revision_id, 'queued', 0,
                    $available_at_utc, NULL, NULL, NULL,
                    $idempotency_key, NULL, NULL, NULL, NULL)
            ON CONFLICT(idempotency_key) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", job.Id.ToString());
        command.Parameters.AddWithValue("$processing_run_id", job.ProcessingRunId.ToString());
        command.Parameters.AddWithValue("$asset_revision_id", job.AssetRevisionId.ToString());
        command.Parameters.AddWithValue("$available_at_utc", Format(job.AvailableAtUtc));
        command.Parameters.AddWithValue("$idempotency_key", job.IdempotencyKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CatalogueProcessingRun?> ReadRunAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProcessingRunId id,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RunSelect + " WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    private static async Task<IReadOnlyList<CatalogueProcessingJob>> ReadJobsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        ProcessingRunId runId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = JobSelect + " WHERE processing_run_id = $run_id ORDER BY available_at_utc, id;";
        command.Parameters.AddWithValue("$run_id", runId.ToString());

        List<CatalogueProcessingJob> jobs = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadJob(reader));
        }

        return jobs;
    }

    private static CatalogueProcessingRun ReadRun(SqliteDataReader reader) =>
        new(
            ProcessingRunId.From(Guid.Parse(reader.GetString(0))),
            ParseRunStatus(reader.GetString(1)),
            reader.GetString(2),
            ParseTimestamp(reader.GetString(3)),
            reader.IsDBNull(4) ? null : ParseTimestamp(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6)));

    private static CatalogueProcessingJob ReadJob(SqliteDataReader reader) =>
        new(
            ProcessingJobId.From(Guid.Parse(reader.GetString(0))),
            ProcessingRunId.From(Guid.Parse(reader.GetString(1))),
            AssetRevisionId.From(Guid.Parse(reader.GetString(2))),
            ParseJobStatus(reader.GetString(3)),
            reader.GetInt32(4),
            ParseTimestamp(reader.GetString(5)),
            reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6)),
            reader.IsDBNull(7) ? null : ParseTimestamp(reader.GetString(7)),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : ProcessingLeaseToken.From(Guid.Parse(reader.GetString(10))),
            reader.IsDBNull(11) ? null : ParseTimestamp(reader.GetString(11)),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : ParseFailureKind(reader.GetString(13)));

    private static string Format(ProcessingRunStatus status) => status switch
    {
        ProcessingRunStatus.Pending => "pending",
        ProcessingRunStatus.Running => "running",
        ProcessingRunStatus.Completed => "completed",
        ProcessingRunStatus.Failed => "failed",
        ProcessingRunStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static ProcessingRunStatus ParseRunStatus(string status) => status switch
    {
        "pending" => ProcessingRunStatus.Pending,
        "running" => ProcessingRunStatus.Running,
        "completed" => ProcessingRunStatus.Completed,
        "failed" => ProcessingRunStatus.Failed,
        "cancelled" => ProcessingRunStatus.Cancelled,
        _ => throw new InvalidDataException($"Unknown processing run status '{status}'."),
    };

    private static ProcessingJobStatus ParseJobStatus(string status) => status switch
    {
        "queued" => ProcessingJobStatus.Queued,
        "running" => ProcessingJobStatus.Running,
        "succeeded" => ProcessingJobStatus.Succeeded,
        "failed" => ProcessingJobStatus.Failed,
        "cancelled" => ProcessingJobStatus.Cancelled,
        _ => throw new InvalidDataException($"Unknown processing job status '{status}'."),
    };

    private static string Format(ProcessingFailureKind failureKind) => failureKind switch
    {
        ProcessingFailureKind.Transient => "transient",
        ProcessingFailureKind.Permanent => "permanent",
        _ => throw new ArgumentOutOfRangeException(nameof(failureKind)),
    };

    private static ProcessingFailureKind ParseFailureKind(string failureKind) => failureKind switch
    {
        "transient" => ProcessingFailureKind.Transient,
        "permanent" => ProcessingFailureKind.Permanent,
        _ => throw new InvalidDataException($"Unknown processing failure kind '{failureKind}'."),
    };

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private const string RunSelect = """
        SELECT id, status, configuration_json, started_at_utc, completed_at_utc,
               error, cancellation_requested_at_utc
        FROM processing_runs
        """;

    private const string JobSelect = """
        SELECT id, processing_run_id, asset_revision_id, status, attempt_count,
               available_at_utc, started_at_utc, completed_at_utc, error,
               idempotency_key, lease_token, leased_until_utc, checkpoint_json,
               last_failure_kind
        FROM processing_jobs
        """;
}
