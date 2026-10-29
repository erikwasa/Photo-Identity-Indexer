using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Derivative-specific adapter boundary. Checkpoint 12 includes an unregistered
/// integration adapter, but there is no approved production FFmpeg profile,
/// hosted registration or video enablement. WI-0189 must approve output format.
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
    bool Published, bool Retryable,
    VideoAttemptFailureDiagnostic? Failure = null);

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
        CancellationToken token = default,
        Guid? selectedJobId = null,
        bool requireArchiveCoverage = false)
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
        VideoProcessingJobLease? lease = await queue.TryClaimNextAsync(
            budget, photoWorkActive: false, token: token,
            selectedJobId: selectedJobId, requireArchiveCoverage: requireArchiveCoverage);
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
            // Use a separate, bounded acknowledgement deadline; the
            // per-attempt token has expired, but an unbounded database call
            // must not hang a worker that is supposed to be stopping.
            using CancellationTokenSource acknowledgment =
                new(TimeSpan.FromSeconds(5));
            return await RecordFailureAsync(
                queue, lease, retryable: true, acknowledgment.Token);
        }
        catch (Exception error)
        {
            // The adapter may attach only a known failure stage. Never expose
            // its exception message, source path, or native tool output.
            return await RecordFailureAsync(queue, lease, retryable: true,
                token, VideoAttemptFailureDiagnostics.FromException(error));
        }

        if (report is null)
            return await RecordFailureAsync(queue, lease, retryable: false,
                token, new("adapter-report", "missing-attempt-report"));
        if (!report.Published)
            return await RecordFailureAsync(queue, lease, report.Retryable,
                token, report.Failure ??
                    new VideoAttemptFailureDiagnostic("adapter-report", "not-published"));

        // The adapter cannot authorize success by returning true. It must
        // have committed a durable revision/exclusion/lease-fenced result.
        VideoProcessingJobState? state;
        VideoPublishedDerivativeReceipt? receipt;
        try
        {
            state = await queue.GetAsync(lease.Id, token);
            receipt = await new PostgresVideoPublishedDerivativeRepository(database)
                .GetCurrentAsync(lease.Id, token);
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
            !state.CancellationRequested && receipt is not null &&
            receipt.RevisionId == lease.RevisionId.Value)
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
        bool retryable, CancellationToken token,
        VideoAttemptFailureDiagnostic? failure = null)
    {
        try
        {
            bool recorded = await queue.TryFinishAttemptAsync(
                lease.Id, lease.LeaseToken, succeeded: false,
                retryable: retryable, token: token);
            return new(true, false, recorded
                ? (retryable ? "retry-pending" : "failed")
                : "lease-revoked", failure);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(true, false, "acknowledgement-unavailable",
                failure ?? new("acknowledgement", "acknowledgement-unavailable"));
        }
    }
}

/// <summary>No paths, identities, inputs, filenames or media content.</summary>
public sealed record VideoWorkerCycleResult(
    bool Claimed, bool Published, string ReasonCode,
    VideoAttemptFailureDiagnostic? Failure = null);

/// <summary>
/// Only first-party constant labels and numeric exit codes reach the API.
/// There is no source file name, tool output, native message or exception text.
/// In-memory evidence is retained for the current process only.
/// </summary>
public sealed record VideoAttemptFailureDiagnostic(
    string Stage, string FailureClass,
    string? SupervisorCheckpoint = null, int? ToolExitCode = null);

/// <summary>
/// Mark precisely which phase threw without wrapping the exception or changing
/// its cancellation, cleanup, or lease-acknowledgement semantics.
/// </summary>
public static class VideoAttemptFailureDiagnostics
{
    private const string StageKey = "PhotoIdentity.VideoAttempt.SanitizedStage";
    private static readonly HashSet<string> AllowedStages =
    [
        "source-staging", "tool-execution", "output-validation",
        "publication"
    ];

    public static void Mark(Exception exception, string stage)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!AllowedStages.Contains(stage))
            throw new ArgumentException("Video failure stage is not allowlisted.");
        if (!exception.Data.Contains(StageKey))
            exception.Data[StageKey] = stage;
    }

    public static VideoAttemptFailureDiagnostic FromException(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        string stage = error.Data[StageKey] is string marked &&
            AllowedStages.Contains(marked) ? marked : "unclassified";
        string failureClass = error switch
        {
            VideoProxyValidationException validation => validation.ReasonCode,
            System.ComponentModel.Win32Exception => "native-process-setup",
            TimeoutException => "timeout",
            UnauthorizedAccessException => "filesystem-access-denied",
            FileNotFoundException => "required-file-missing",
            DirectoryNotFoundException => "required-directory-missing",
            IOException => "filesystem-io",
            InvalidDataException => "invalid-media-evidence",
            ArgumentException => "configuration-rejected",
            InvalidOperationException => "execution-safety-rejected",
            OperationCanceledException => "cancelled",
            _ => "unexpected-failure"
        };
        return new(stage, failureClass, VideoToolFailureEvidence.Read(error),
            error is VideoProxyValidationException probe ? probe.ToolExitCode : null);
    }
}
