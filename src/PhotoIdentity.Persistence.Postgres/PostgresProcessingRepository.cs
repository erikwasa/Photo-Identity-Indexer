using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;

namespace PhotoIdentity.Persistence.Postgres;

/// <summary>
/// PostgreSQL implementation of durable processing run lifecycle, leasing, checkpointing,
/// retry and restart-safe execution semantics.
/// </summary>
public sealed class PostgresProcessingRepository :
    IProcessingExecutionRepository,
    IProcessingRunRepository
{
    private readonly PostgresCatalogueDatabase _database;

    public PostgresProcessingRepository(PostgresCatalogueDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    public async Task<CatalogueProcessingBatch> CreateRunAsync(
        CatalogueProcessingRun run,
        IReadOnlyCollection<CatalogueProcessingJob> jobs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(jobs);

        if (run.Status != ProcessingRunStatus.Pending)
        {
            throw new ArgumentException(
                "New processing runs must be pending.",
                nameof(run));
        }

        foreach (CatalogueProcessingJob job in jobs)
        {
            if (job.ProcessingRunId != run.Id)
            {
                throw new ArgumentException(
                    "Every job must belong to the supplied run.",
                    nameof(jobs));
            }

            if (job.Status != ProcessingJobStatus.Queued ||
                job.AttemptCount != 0 ||
                job.Error is not null ||
                job.LeaseToken is not null ||
                job.CheckpointJson is not null)
            {
                throw new ArgumentException(
                    "New processing jobs must be clean, unattempted queued jobs.",
                    nameof(jobs));
            }
        }

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await InsertRunAsync(connection, transaction, run, cancellationToken);
        foreach (CatalogueProcessingJob job in jobs)
        {
            await InsertJobAsync(connection, transaction, job, cancellationToken);
        }

        CatalogueProcessingRun persistedRun =
            await ReadRunAsync(
                connection,
                transaction,
                run.Id,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The processing run was unavailable after it was persisted.");

        IReadOnlyList<CatalogueProcessingJob> persistedJobs =
            await ReadJobsAsync(
                connection,
                transaction,
                run.Id,
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CatalogueProcessingBatch(persistedRun, persistedJobs);
    }

    public async Task<CatalogueProcessingRun?> GetRunAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        return await ReadRunAsync(
            connection,
            transaction: null,
            runId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogueProcessingJob>> GetJobsAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        return await ReadJobsAsync(
            connection,
            transaction: null,
            runId,
            cancellationToken);
    }

    public async Task<ProcessingRunSummary> GetRunSummaryAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default)
    {
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        CatalogueProcessingRun run =
            await ReadRunAsync(
                connection,
                transaction: null,
                runId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Processing run {runId} was not found.");

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                COUNT(*)::integer,
                COUNT(*) FILTER (WHERE status = 'queued')::integer,
                COUNT(*) FILTER (WHERE status = 'running')::integer,
                COUNT(*) FILTER (WHERE status = 'succeeded')::integer,
                COUNT(*) FILTER (WHERE status = 'failed')::integer,
                COUNT(*) FILTER (WHERE status = 'cancelled')::integer,
                COALESCE(SUM(attempt_count), 0)::integer,
                MIN(available_at_utc) FILTER (WHERE status = 'queued')
            FROM processing_jobs
            WHERE processing_run_id = @run_id;
            """;
        command.Parameters.AddWithValue(
            "run_id",
            Guid.Parse(runId.ToString()));

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
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
            reader.IsDBNull(7)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(7));
    }

    /// <summary>
    /// Atomically claims one due queued job, or reclaims an expired running job.
    /// PostgreSQL row locking prevents multiple workers from claiming the same job.
    /// </summary>
    public async Task<CatalogueProcessingJob?> ClaimNextJobAsync(
        ProcessingRunId runId,
        DateTimeOffset claimedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "The lease duration must be positive.");
        }

        DateTimeOffset claimedAt = claimedAtUtc.ToUniversalTime();
        DateTimeOffset leasedUntil = claimedAt.Add(leaseDuration);
        ProcessingLeaseToken leaseToken = ProcessingLeaseToken.New();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        CatalogueProcessingJob? claimed;
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                WITH candidate AS (
                    SELECT job.id
                    FROM processing_jobs AS job
                    INNER JOIN processing_runs AS run
                        ON run.id = job.processing_run_id
                    WHERE job.processing_run_id = @run_id
                      AND run.status IN ('pending', 'running')
                      AND run.cancellation_requested_at_utc IS NULL
                      AND (
                          (job.status = 'queued'
                           AND job.available_at_utc <= @claimed_at_utc)
                          OR
                          (job.status = 'running'
                           AND job.leased_until_utc <= @claimed_at_utc)
                      )
                    ORDER BY
                        CASE job.status WHEN 'queued' THEN 0 ELSE 1 END,
                        job.available_at_utc,
                        job.id
                    FOR UPDATE OF job SKIP LOCKED
                    LIMIT 1
                )
                UPDATE processing_jobs AS job
                SET status = 'running',
                    attempt_count = job.attempt_count + 1,
                    started_at_utc = @claimed_at_utc,
                    completed_at_utc = NULL,
                    error = NULL,
                    last_failure_kind = CASE
                        WHEN job.status = 'running' THEN 'transient'
                        ELSE job.last_failure_kind
                    END,
                    lease_token = @lease_token,
                    leased_until_utc = @leased_until_utc
                FROM candidate
                WHERE job.id = candidate.id
                RETURNING
                    job.id,
                    job.processing_run_id,
                    job.asset_revision_id,
                    job.status,
                    job.attempt_count,
                    job.available_at_utc,
                    job.started_at_utc,
                    job.completed_at_utc,
                    job.error,
                    job.idempotency_key,
                    job.lease_token,
                    job.leased_until_utc,
                    job.checkpoint_json,
                    job.last_failure_kind;
                """;
            command.Parameters.AddWithValue(
                "run_id",
                Guid.Parse(runId.ToString()));
            command.Parameters.AddWithValue(
                "claimed_at_utc",
                claimedAt);
            command.Parameters.AddWithValue(
                "lease_token",
                Guid.Parse(leaseToken.ToString()));
            command.Parameters.AddWithValue(
                "leased_until_utc",
                leasedUntil);

            await using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);
            claimed = await reader.ReadAsync(cancellationToken)
                ? ReadJob(reader)
                : null;
        }

        if (claimed is not null)
        {
            await using NpgsqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE processing_runs
                SET status = 'running',
                    completed_at_utc = NULL,
                    error = NULL
                WHERE id = @run_id
                  AND status = 'pending';
                """;
            command.Parameters.AddWithValue(
                "run_id",
                Guid.Parse(runId.ToString()));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return claimed;
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
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "The lease duration must be positive.");
        }

        DateTimeOffset savedAt = savedAtUtc.ToUniversalTime();
        return TransitionLeasedJobAsync(
            jobId,
            leaseToken,
            savedAt,
            """
            UPDATE processing_jobs
            SET checkpoint_json = @checkpoint_json::jsonb,
                leased_until_utc = @leased_until_utc
            WHERE id = @id
              AND status = 'running'
              AND lease_token = @lease_token
              AND leased_until_utc > @transition_at_utc
            RETURNING
                id,
                processing_run_id,
                asset_revision_id,
                status,
                attempt_count,
                available_at_utc,
                started_at_utc,
                completed_at_utc,
                error,
                idempotency_key,
                lease_token,
                leased_until_utc,
                checkpoint_json,
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
                completed_at_utc = @transition_at_utc,
                error = NULL,
                last_failure_kind = NULL,
                lease_token = NULL,
                leased_until_utc = NULL
            WHERE id = @id
              AND status = 'running'
              AND lease_token = @lease_token
              AND leased_until_utc > @transition_at_utc
            RETURNING
                id,
                processing_run_id,
                asset_revision_id,
                status,
                attempt_count,
                available_at_utc,
                started_at_utc,
                completed_at_utc,
                error,
                idempotency_key,
                lease_token,
                leased_until_utc,
                checkpoint_json,
                last_failure_kind;
            """,
            error: null,
            checkpointJson: null,
            failureKind: null,
            retryAtUtc: null,
            leasedUntilUtc: null,
            cancellationToken);

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
        if (retryAtUtc is not null &&
            failureKind != ProcessingFailureKind.Transient)
        {
            throw new ArgumentException(
                "Only transient failures can be scheduled for retry.",
                nameof(retryAtUtc));
        }

        string sql = retryAtUtc is null
            ? """
              UPDATE processing_jobs
              SET status = 'failed',
                  completed_at_utc = @transition_at_utc,
                  error = @error,
                  last_failure_kind = @failure_kind,
                  lease_token = NULL,
                  leased_until_utc = NULL
              WHERE id = @id
                AND status = 'running'
                AND lease_token = @lease_token
                AND leased_until_utc > @transition_at_utc
              RETURNING
                  id,
                  processing_run_id,
                  asset_revision_id,
                  status,
                  attempt_count,
                  available_at_utc,
                  started_at_utc,
                  completed_at_utc,
                  error,
                  idempotency_key,
                  lease_token,
                  leased_until_utc,
                  checkpoint_json,
                  last_failure_kind;
              """
            : """
              UPDATE processing_jobs
              SET status = 'queued',
                  available_at_utc = @retry_at_utc,
                  started_at_utc = NULL,
                  completed_at_utc = NULL,
                  error = @error,
                  last_failure_kind = @failure_kind,
                  lease_token = NULL,
                  leased_until_utc = NULL
              WHERE id = @id
                AND status = 'running'
                AND lease_token = @lease_token
                AND leased_until_utc > @transition_at_utc
              RETURNING
                  id,
                  processing_run_id,
                  asset_revision_id,
                  status,
                  attempt_count,
                  available_at_utc,
                  started_at_utc,
                  completed_at_utc,
                  error,
                  idempotency_key,
                  lease_token,
                  leased_until_utc,
                  checkpoint_json,
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
            retryAtUtc?.ToUniversalTime(),
            leasedUntilUtc: null,
            cancellationToken);
    }

    public async Task<CatalogueProcessingRun> RequestCancellationAsync(
        ProcessingRunId runId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset requestedAt = requestedAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        CatalogueProcessingRun run =
            await ReadRunAsync(
                connection,
                transaction,
                runId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Processing run {runId} was not found.");

        if (run.Status is ProcessingRunStatus.Completed
            or ProcessingRunStatus.Failed
            or ProcessingRunStatus.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            return run;
        }

        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE processing_runs
                SET status = 'cancelled',
                    cancellation_requested_at_utc = @requested_at_utc,
                    completed_at_utc = @requested_at_utc,
                    error = NULL
                WHERE id = @run_id;
                """;
            command.Parameters.AddWithValue(
                "run_id",
                Guid.Parse(runId.ToString()));
            command.Parameters.AddWithValue(
                "requested_at_utc",
                requestedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE processing_jobs
                SET status = 'cancelled',
                    completed_at_utc = @requested_at_utc,
                    lease_token = NULL,
                    leased_until_utc = NULL
                WHERE processing_run_id = @run_id
                  AND status IN ('queued', 'running');
                """;
            command.Parameters.AddWithValue(
                "run_id",
                Guid.Parse(runId.ToString()));
            command.Parameters.AddWithValue(
                "requested_at_utc",
                requestedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueProcessingRun cancelled =
            await ReadRunAsync(
                connection,
                transaction,
                runId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The processing run disappeared while it was cancelled.");

        await transaction.CommitAsync(cancellationToken);
        return cancelled;
    }

    public async Task<CatalogueProcessingRun> CompleteRunAsync(
        ProcessingRunId runId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset completedAt = completedAtUtc.ToUniversalTime();

        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        CatalogueProcessingRun run =
            await ReadRunAsync(
                connection,
                transaction,
                runId,
                cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Processing run {runId} was not found.");

        if (run.Status is ProcessingRunStatus.Completed
            or ProcessingRunStatus.Failed
            or ProcessingRunStatus.Cancelled)
        {
            await transaction.CommitAsync(cancellationToken);
            return run;
        }

        long unfinished;
        long failed;
        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT
                    COUNT(*) FILTER (
                        WHERE status IN ('queued', 'running')),
                    COUNT(*) FILTER (
                        WHERE status = 'failed')
                FROM processing_jobs
                WHERE processing_run_id = @run_id;
                """;
            command.Parameters.AddWithValue(
                "run_id",
                Guid.Parse(runId.ToString()));

            await using NpgsqlDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            unfinished = reader.GetInt64(0);
            failed = reader.GetInt64(1);
        }

        if (unfinished != 0)
        {
            throw new InvalidOperationException(
                "A processing run cannot complete while jobs remain queued or running.");
        }

        string status = failed == 0 ? "completed" : "failed";
        string? error = failed == 0
            ? null
            : $"{failed} processing job(s) failed.";

        await using (NpgsqlCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE processing_runs
                SET status = @status,
                    completed_at_utc = @completed_at_utc,
                    error = @error
                WHERE id = @run_id;
                """;
            command.Parameters.AddWithValue("status", status);
            command.Parameters.AddWithValue(
                "completed_at_utc",
                completedAt);
            command.Parameters.AddWithValue(
                "error",
                (object?)error ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "run_id",
                Guid.Parse(runId.ToString()));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        CatalogueProcessingRun persisted =
            await ReadRunAsync(
                connection,
                transaction,
                runId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The processing run disappeared while it was finalized.");

        await transaction.CommitAsync(cancellationToken);
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
        await using NpgsqlConnection connection =
            await _database.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(jobId.ToString()));
        command.Parameters.AddWithValue(
            "lease_token",
            Guid.Parse(leaseToken.ToString()));
        command.Parameters.AddWithValue(
            "transition_at_utc",
            transitionAtUtc.ToUniversalTime());
        command.Parameters.AddWithValue(
            "error",
            (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "failure_kind",
            failureKind is null
                ? DBNull.Value
                : Format(failureKind.Value));
        command.Parameters.AddWithValue(
            "retry_at_utc",
            retryAtUtc is null
                ? DBNull.Value
                : retryAtUtc.Value.ToUniversalTime());
        command.Parameters.AddWithValue(
            "leased_until_utc",
            leasedUntilUtc is null
                ? DBNull.Value
                : leasedUntilUtc.Value.ToUniversalTime());

        NpgsqlParameter checkpointParameter =
            command.Parameters.Add(
                "checkpoint_json",
                NpgsqlDbType.Jsonb);
        checkpointParameter.Value =
            checkpointJson is null ? DBNull.Value : checkpointJson;

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ProcessingLeaseLostException(jobId);
        }

        return ReadJob(reader);
    }

    private static async Task InsertRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CatalogueProcessingRun run,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO processing_runs (
                id,
                status,
                configuration_json,
                started_at_utc,
                completed_at_utc,
                error,
                cancellation_requested_at_utc)
            VALUES (
                @id,
                @status,
                @configuration_json,
                @started_at_utc,
                NULL,
                NULL,
                NULL)
            ON CONFLICT(id) DO NOTHING;
            """;
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(run.Id.ToString()));
        command.Parameters.AddWithValue(
            "status",
            Format(run.Status));
        command.Parameters.AddWithValue(
            "started_at_utc",
            run.StartedAtUtc);

        NpgsqlParameter configuration =
            command.Parameters.Add(
                "configuration_json",
                NpgsqlDbType.Jsonb);
        configuration.Value = run.ConfigurationJson;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertJobAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CatalogueProcessingJob job,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO processing_jobs (
                id,
                processing_run_id,
                asset_revision_id,
                status,
                attempt_count,
                available_at_utc,
                started_at_utc,
                completed_at_utc,
                error,
                idempotency_key,
                lease_token,
                leased_until_utc,
                checkpoint_json,
                last_failure_kind)
            VALUES (
                @id,
                @processing_run_id,
                @asset_revision_id,
                'queued',
                0,
                @available_at_utc,
                NULL,
                NULL,
                NULL,
                @idempotency_key,
                NULL,
                NULL,
                NULL,
                NULL)
            ON CONFLICT(idempotency_key) DO NOTHING;
            """;
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(job.Id.ToString()));
        command.Parameters.AddWithValue(
            "processing_run_id",
            Guid.Parse(job.ProcessingRunId.ToString()));
        command.Parameters.AddWithValue(
            "asset_revision_id",
            Guid.Parse(job.AssetRevisionId.ToString()));
        command.Parameters.AddWithValue(
            "available_at_utc",
            job.AvailableAtUtc);
        command.Parameters.AddWithValue(
            "idempotency_key",
            job.IdempotencyKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CatalogueProcessingRun?> ReadRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ProcessingRunId runId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            RunSelect + " WHERE id = @id;";
        command.Parameters.AddWithValue(
            "id",
            Guid.Parse(runId.ToString()));

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRun(reader)
            : null;
    }

    private static async Task<IReadOnlyList<CatalogueProcessingJob>> ReadJobsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        ProcessingRunId runId,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            JobSelect +
            " WHERE processing_run_id = @run_id ORDER BY available_at_utc, id;";
        command.Parameters.AddWithValue(
            "run_id",
            Guid.Parse(runId.ToString()));

        List<CatalogueProcessingJob> jobs = [];
        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadJob(reader));
        }

        return jobs;
    }

    private static CatalogueProcessingRun ReadRun(
        NpgsqlDataReader reader) =>
        new(
            ProcessingRunId.From(reader.GetGuid(0)),
            ParseRunStatus(reader.GetString(1)),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.IsDBNull(4)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5)
                ? null
                : reader.GetString(5),
            reader.IsDBNull(6)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(6));

    private static CatalogueProcessingJob ReadJob(
        NpgsqlDataReader reader) =>
        new(
            ProcessingJobId.From(reader.GetGuid(0)),
            ProcessingRunId.From(reader.GetGuid(1)),
            AssetRevisionId.From(reader.GetGuid(2)),
            ParseJobStatus(reader.GetString(3)),
            reader.GetInt32(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(7),
            reader.IsDBNull(8)
                ? null
                : reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10)
                ? null
                : ProcessingLeaseToken.From(reader.GetGuid(10)),
            reader.IsDBNull(11)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12)
                ? null
                : reader.GetString(12),
            reader.IsDBNull(13)
                ? null
                : ParseFailureKind(reader.GetString(13)));

    private static string Format(
        ProcessingRunStatus status) => status switch
    {
        ProcessingRunStatus.Pending => "pending",
        ProcessingRunStatus.Running => "running",
        ProcessingRunStatus.Completed => "completed",
        ProcessingRunStatus.Failed => "failed",
        ProcessingRunStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static ProcessingRunStatus ParseRunStatus(
        string status) => status switch
    {
        "pending" => ProcessingRunStatus.Pending,
        "running" => ProcessingRunStatus.Running,
        "completed" => ProcessingRunStatus.Completed,
        "failed" => ProcessingRunStatus.Failed,
        "cancelled" => ProcessingRunStatus.Cancelled,
        _ => throw new InvalidDataException(
            $"Unknown processing run status '{status}'."),
    };

    private static ProcessingJobStatus ParseJobStatus(
        string status) => status switch
    {
        "queued" => ProcessingJobStatus.Queued,
        "running" => ProcessingJobStatus.Running,
        "succeeded" => ProcessingJobStatus.Succeeded,
        "failed" => ProcessingJobStatus.Failed,
        "cancelled" => ProcessingJobStatus.Cancelled,
        _ => throw new InvalidDataException(
            $"Unknown processing job status '{status}'."),
    };

    private static string Format(
        ProcessingFailureKind failureKind) => failureKind switch
    {
        ProcessingFailureKind.Transient => "transient",
        ProcessingFailureKind.Permanent => "permanent",
        _ => throw new ArgumentOutOfRangeException(nameof(failureKind)),
    };

    private static ProcessingFailureKind ParseFailureKind(
        string failureKind) => failureKind switch
    {
        "transient" => ProcessingFailureKind.Transient,
        "permanent" => ProcessingFailureKind.Permanent,
        _ => throw new InvalidDataException(
            $"Unknown processing failure kind '{failureKind}'."),
    };

    private const string RunSelect =
        """
        SELECT
            id,
            status,
            configuration_json,
            started_at_utc,
            completed_at_utc,
            error,
            cancellation_requested_at_utc
        FROM processing_runs
        """;

    private const string JobSelect =
        """
        SELECT
            id,
            processing_run_id,
            asset_revision_id,
            status,
            attempt_count,
            available_at_utc,
            started_at_utc,
            completed_at_utc,
            error,
            idempotency_key,
            lease_token,
            leased_until_utc,
            checkpoint_json,
            last_failure_kind
        FROM processing_jobs
        """;
}
