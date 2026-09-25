using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;

namespace PhotoIdentity.Api;

/// <summary>
/// Enforces durable source-copy exclusion at the final processing-job claim boundary.
/// Jobs queued before an exclusion are completed without invoking detector/embedder or other
/// processing handlers; no analysis completion record is written, so restoring the source copy
/// can make the revision eligible for processing again.
/// </summary>
public sealed class ExclusionAwareProcessingExecutionRepository : IProcessingExecutionRepository
{
    private readonly IProcessingExecutionRepository _inner;
    private readonly ISourceCopyExclusionRepository _exclusions;

    public ExclusionAwareProcessingExecutionRepository(
        IProcessingExecutionRepository inner,
        ISourceCopyExclusionRepository exclusions)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _exclusions = exclusions ?? throw new ArgumentNullException(nameof(exclusions));
    }

    public async Task<CatalogueProcessingJob?> ClaimNextJobAsync(
        ProcessingRunId runId,
        DateTimeOffset claimedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            CatalogueProcessingJob? job = await _inner.ClaimNextJobAsync(
                runId,
                claimedAtUtc,
                leaseDuration,
                cancellationToken);
            if (job is null)
            {
                return null;
            }

            if (!await _exclusions.IsRevisionExcludedAsync(job.AssetRevisionId, cancellationToken))
            {
                return job;
            }

            ProcessingLeaseToken leaseToken = job.LeaseToken
                ?? throw new InvalidOperationException("A claimed processing job did not carry a lease token.");
            _ = await _inner.CompleteJobAsync(
                job.Id,
                leaseToken,
                claimedAtUtc,
                cancellationToken);
        }
    }

    public Task<ProcessingRunSummary> GetRunSummaryAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default) =>
        _inner.GetRunSummaryAsync(runId, cancellationToken);

    public Task<CatalogueProcessingJob> SaveCheckpointAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        string checkpointJson,
        DateTimeOffset savedAtUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default) =>
        _inner.SaveCheckpointAsync(
            jobId,
            leaseToken,
            checkpointJson,
            savedAtUtc,
            leaseDuration,
            cancellationToken);

    public Task<CatalogueProcessingJob> CompleteJobAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default) =>
        _inner.CompleteJobAsync(jobId, leaseToken, completedAtUtc, cancellationToken);

    public Task<CatalogueProcessingJob> FailJobAsync(
        ProcessingJobId jobId,
        ProcessingLeaseToken leaseToken,
        ProcessingFailureKind failureKind,
        string error,
        DateTimeOffset failedAtUtc,
        DateTimeOffset? retryAtUtc = null,
        CancellationToken cancellationToken = default) =>
        _inner.FailJobAsync(
            jobId,
            leaseToken,
            failureKind,
            error,
            failedAtUtc,
            retryAtUtc,
            cancellationToken);

    public Task<CatalogueProcessingRun> CompleteRunAsync(
        ProcessingRunId runId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default) =>
        _inner.CompleteRunAsync(runId, completedAtUtc, cancellationToken);
}
