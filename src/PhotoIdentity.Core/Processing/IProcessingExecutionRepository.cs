using PhotoIdentity.Core.Identifiers;

namespace PhotoIdentity.Core.Processing;

/// <summary>
/// Database-neutral durable execution boundary used by resumable processing workers.
/// Implementations must preserve lease-token, checkpoint, retry and run-finalization semantics.
/// </summary>
public interface IProcessingExecutionRepository
{
    Task<CatalogueProcessingJob?> ClaimNextJobAsync(
        ProcessingRunId runId,
        DateTimeOffset claimedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<ProcessingRunSummary> GetRunSummaryAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default);

    Task<CatalogueProcessingJob> SaveCheckpointAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        string checkpointJson,
        DateTimeOffset savedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<CatalogueProcessingJob> CompleteJobAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);

    Task<CatalogueProcessingJob> FailJobAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        ProcessingFailureKind failureKind,
        string error,
        DateTimeOffset failedAtUtc,
        DateTimeOffset? retryAtUtc = null,
        CancellationToken cancellationToken = default);

    Task<CatalogueProcessingRun> CompleteRunAsync(
        ProcessingRunId runId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessingLeaseLostException : InvalidOperationException
{
    public ProcessingLeaseLostException(ProcessingJobId jobId)
        : base($"The lease for processing job {jobId} is no longer valid.")
    {
        JobId = jobId;
    }

    public ProcessingJobId JobId { get; }
}
