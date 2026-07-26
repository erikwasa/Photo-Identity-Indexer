using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Worker;

public sealed record ResumableBatchProcessorOptions
{
    public ResumableBatchProcessorOptions(
        TimeSpan? leaseDuration = null,
        ProcessingRetryPolicy? retryPolicy = null,
        int maxAttemptsPerInvocation = int.MaxValue)
    {
        TimeSpan lease = leaseDuration ?? TimeSpan.FromMinutes(5);
        if (lease <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "The lease duration must be positive.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttemptsPerInvocation, 1);
        LeaseDuration = lease;
        RetryPolicy = retryPolicy ?? new ProcessingRetryPolicy();
        MaxAttemptsPerInvocation = maxAttemptsPerInvocation;
    }

    public TimeSpan LeaseDuration { get; }
    public ProcessingRetryPolicy RetryPolicy { get; }
    public int MaxAttemptsPerInvocation { get; }
}

public sealed record ResumableBatchProcessorResult(
    ProcessingRunSummary Summary,
    int AttemptsProcessed);

/// <summary>
/// Runs due durable jobs until the run becomes terminal, no work is currently due,
/// or the invocation limit is reached.
/// </summary>
public sealed class ResumableBatchProcessor
{
    private readonly SqliteProcessingRepository _repository;
    private readonly IProcessingJobHandler _handler;
    private readonly TimeProvider _timeProvider;

    public ResumableBatchProcessor(
        SqliteProcessingRepository repository,
        IProcessingJobHandler handler,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(handler);
        _repository = repository;
        _handler = handler;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ResumableBatchProcessorResult> RunUntilIdleAsync(
        ProcessingRunId runId,
        ResumableBatchProcessorOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ResumableBatchProcessorOptions resolved = options ?? new ResumableBatchProcessorOptions();
        int processed = 0;

        while (processed < resolved.MaxAttemptsPerInvocation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset now = _timeProvider.GetUtcNow();
            CatalogueProcessingJob? job = await _repository.ClaimNextJobAsync(
                runId,
                now,
                resolved.LeaseDuration,
                cancellationToken);
            if (job is null)
            {
                break;
            }

            ProcessingLeaseToken leaseToken = job.LeaseToken
                ?? throw new InvalidOperationException("A claimed job did not include a lease token.");
            ProcessingJobContext context = new(
                job.ProcessingRunId,
                job.Id,
                job.AssetRevisionId,
                job.AttemptCount,
                job.IdempotencyKey,
                job.CheckpointJson);
            RepositoryCheckpointWriter checkpointWriter = new(
                _repository,
                job.Id,
                leaseToken,
                resolved.LeaseDuration,
                _timeProvider);

            try
            {
                await _handler.ProcessAsync(context, checkpointWriter, cancellationToken);
                await _repository.CompleteJobAsync(
                    job.Id,
                    leaseToken,
                    _timeProvider.GetUtcNow(),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ProcessingLeaseLostException)
            {
                // Cancellation or another worker's reclaim invalidated this attempt.
                // Its output must be protected by the job idempotency key.
            }
            catch (ProcessingJobFailureException exception)
            {
                await RecordFailureAsync(job, leaseToken, exception, resolved, cancellationToken);
            }
            catch (Exception exception)
            {
                ProcessingJobFailureException permanent = new(
                    ProcessingFailureKind.Permanent,
                    exception.Message,
                    exception);
                await RecordFailureAsync(job, leaseToken, permanent, resolved, cancellationToken);
            }

            processed++;
        }

        ProcessingRunSummary summary = await _repository.GetRunSummaryAsync(runId, cancellationToken);
        if (!summary.IsTerminal && summary.QueuedJobs == 0 && summary.RunningJobs == 0)
        {
            await _repository.CompleteRunAsync(runId, _timeProvider.GetUtcNow(), cancellationToken);
            summary = await _repository.GetRunSummaryAsync(runId, cancellationToken);
        }

        return new ResumableBatchProcessorResult(summary, processed);
    }

    private async Task RecordFailureAsync(
        CatalogueProcessingJob job,
        ProcessingLeaseToken leaseToken,
        ProcessingJobFailureException exception,
        ResumableBatchProcessorOptions options,
        CancellationToken cancellationToken)
    {
        DateTimeOffset failedAt = _timeProvider.GetUtcNow();
        DateTimeOffset? retryAt = exception.FailureKind == ProcessingFailureKind.Transient &&
            job.AttemptCount < options.RetryPolicy.MaxAttempts
            ? failedAt.Add(options.RetryPolicy.GetDelayAfterAttempt(job.AttemptCount))
            : null;

        try
        {
            await _repository.FailJobAsync(
                job.Id,
                leaseToken,
                exception.FailureKind,
                exception.Message,
                failedAt,
                retryAt,
                cancellationToken);
        }
        catch (ProcessingLeaseLostException)
        {
            // A concurrent cancellation or reclaim already decided the durable state.
        }
    }

    private sealed class RepositoryCheckpointWriter : IProcessingCheckpointWriter
    {
        private readonly SqliteProcessingRepository _repository;
        private readonly ProcessingJobId _jobId;
        private readonly ProcessingLeaseToken _leaseToken;
        private readonly TimeSpan _leaseDuration;
        private readonly TimeProvider _timeProvider;

        public RepositoryCheckpointWriter(
            SqliteProcessingRepository repository,
            ProcessingJobId jobId,
            ProcessingLeaseToken leaseToken,
            TimeSpan leaseDuration,
            TimeProvider timeProvider)
        {
            _repository = repository;
            _jobId = jobId;
            _leaseToken = leaseToken;
            _leaseDuration = leaseDuration;
            _timeProvider = timeProvider;
        }

        public async Task WriteAsync(string checkpointJson, CancellationToken cancellationToken)
        {
            await _repository.SaveCheckpointAsync(
                _jobId,
                _leaseToken,
                checkpointJson,
                _timeProvider.GetUtcNow(),
                _leaseDuration,
                cancellationToken);
        }
    }
}
