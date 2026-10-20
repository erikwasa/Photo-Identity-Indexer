using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Derivative-specific, reviewed adapter boundary. There is deliberately no
/// production implementation or hosted registration in WI-0188; downstream
/// derivative milestones must supply an approved source/format publisher.
/// Implementations may report Published only after they have atomically
/// committed the revision-bound derivative AND fenced success acknowledgement.
/// </summary>
public interface IVideoPublishedDerivativeAdapter
{
    Task<VideoDerivativeAttemptReport> ProcessAsync(
        VideoProcessingJobLease lease, CancellationToken token);
}

/// <summary>
/// Sanitized adapter report. 'Published' is independently cross-checked with
/// durable PostgreSQL job state; claiming success in memory isn't sufficient.
/// </summary>
public sealed record VideoDerivativeAttemptReport(
    bool Published, bool Retryable);

/// <summary>
/// A single bounded admission/attempt cycle. No hidden timer, service, hosted
/// worker, ffmpeg executable, source hydration or automatic enablement.
/// One cycle processes AT MOST one job, strictly when both caller budget and
/// authoritative v35 persisted video control explicitly allow it.
///
/// The adapter is responsible for the exact staged source, OS-limited tool,
/// photo-first interruption, cleanup, and atomic derivative publication.
/// The coordinator NEVER marks a job succeeded. It only asks the existing
/// PostgreSQL queue to record failed/retryable attempts, and cross-checks
/// any claimed durable success. All returned reasons are path-free.
/// </summary>
public sealed class VideoWorkerAttemptCoordinator(PostgresCatalogueDatabase database)
{
    public async Task<VideoWorkerCycleResult> RunOneAsync(
        VideoProcessingBudget budget,
        IVideoPublishedDerivativeAdapter adapter,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(adapter);
        budget.Validate();

        // Until the mixed-load acceptance gate, heavy worker parallelism
        // cannot be increased, even by an accidentally permissive caller.
        if (budget.MaximumConcurrentJobs != 1)
            throw new InvalidOperationException(
                "Unverified video processing concurrency must be exactly one.");
        if (!budget.Enabled || budget.Paused)
            return new(false, false, "caller-disabled");

        var queue = new PostgresVideoProcessingJobRepository(database);
        VideoProcessingJobLease? lease = await queue.TryClaimNextAsync(budget, token);
        if (lease is null)
            return new(false, false, "no-admissible-job");

        var safety = new PostgresVideoLeaseSafetyProbe(database);
        VideoExecutionPermit permit;
        try
        {
            permit = await safety.CheckAsync(lease, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Lease will expire and be recovered; a database error is never
            // interpreted as valid authority or successful publication.
            return new(true, false, "safety-observation-failed");
        }

        if (!permit.Allowed)
            return new(true, false, permit.ReasonCode);

        // Bound cooperative adapter lifetime independently from the lease's
        // 60-second renewed heartbeats. Adapters MUST terminate their child
        // process and staged copy before acknowledging cancellation.
        using CancellationTokenSource attempt = CancellationTokenSource
            .CreateLinkedTokenSource(token);
        attempt.CancelAfter(TimeSpan.FromSeconds(budget.MaximumJobSeconds));

        VideoDerivativeAttemptReport report;
        try
        {
            report = await adapter.ProcessAsync(lease, attempt.Token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // External stop: the adapter must have awaited safe child
            // termination before returning/throwing.
            throw;
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        {
            return await RecordFailureAsync(
                queue, lease, retryable: true, CancellationToken.None);
        }
        catch
        {
            // Do not expose source names, tool output or exception content.
            return await RecordFailureAsync(
                queue, lease, retryable: true, token);
        }

        if (!report.Published)
            return await RecordFailureAsync(queue, lease, report.Retryable, token);

        // The adapter cannot authorize success by returning true. It must
        // have committed a durable revision/exclusion/lease-fenced result.
        VideoProcessingJobState? state;
        try
        {
            state = await queue.GetAsync(lease.Id, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(true, false, "completion-observation-failed");
        }

        if (state?.Status == "succeeded" && state.ProgressPercent == 100 &&
            !state.CancellationRequested)
            return new(true, true, "published");

        // Never grant a successful result to an adapter that forgot or
        // failed to atomically commit its durable derivative.
        await queue.TryFinishAttemptAsync(
            lease.Id, lease.LeaseToken, succeeded: false,
            retryable: false, token: token);
        return new(true, false, "publication-required");
    }

    private static async Task<VideoWorkerCycleResult> RecordFailureAsync(
        PostgresVideoProcessingJobRepository queue, VideoProcessingJobLease lease,
        bool retryable, CancellationToken token)
    {
        try
        {
            bool recorded = await queue.TryFinishAttemptAsync(
                lease.Id, lease.LeaseToken, succeeded: false,
                retryable: retryable, token: token);
            return new(true, false, recorded
                ? (retryable ? "retry-pending" : "failed")
                : "lease-revoked");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(true, false, "acknowledgement-unavailable");
        }
    }
}

/// <summary>No paths, identities, inputs, filenames or media content.</summary>
public sealed record VideoWorkerCycleResult(
    bool Claimed, bool Published, string ReasonCode);
