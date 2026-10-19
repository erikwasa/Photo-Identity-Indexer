using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Deliberately unregistered, explicit-call scaffold for an approved future
/// derivative adapter. Stages only a current locally verified input, runs
/// an OS-limited child against the STAGED copy, maintains the lease, and
/// preempts on photo activity or lost operator control. It never marks a
/// job successful or publishes files: a later adapter must provide a fenced
/// atomic database/file publication transaction before final acknowledgement.
/// </summary>
public sealed class VideoUnpublishedToolAttempt(PostgresCatalogueDatabase database)
{
    public async Task<VideoGuardedToolExecutionResult> RunAsync(
        VideoProcessingJobLease lease,
        string executablePath,
        string scratchRoot,
        VideoToolExecutionBudget limits,
        Func<string, string, IReadOnlyList<string>> approvedArguments,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(approvedArguments);
        limits.Validate();
        if (!OperatingSystem.IsWindows() || limits.WindowsJobLimits is null)
            throw new InvalidOperationException(
                "Video attempts require mandatory Windows Job Object resource limits.");
        if (limits.MaximumWallTime > TimeSpan.FromMinutes(15) ||
            limits.MaximumTemporaryBytes > 2L * 1024 * 1024 * 1024 ||
            lease.SourceBytes > 1024L * 1024 * 1024 ||
            limits.MaximumTemporaryBytes + lease.SourceBytes > lease.TemporaryBytes)
            throw new InvalidOperationException(
                "Video execution exceeds its leased source/scratch reservation.");

        var queue = new PostgresVideoProcessingJobRepository(database);
        var safety = new PostgresVideoLeaseSafetyProbe(database);
        using CancellationTokenSource workToken =
            CancellationTokenSource.CreateLinkedTokenSource(token);

        // The v35 lease lasts 60 seconds. Renew every 15s during staging
        // and subprocess execution. A heartbeat refusal is a mandatory
        // cancellation, never an invitation to continue.
        Task heartbeat = HeartbeatAsync(queue, lease, workToken);
        try
        {
            VideoExecutionPermit initial = await safety.CheckAsync(lease, workToken.Token);
            if (!initial.Allowed)
                return new(false, false, initial.ReasonCode, null);

            await using VideoStagedLocalCopy staged =
                await new VideoLeasedInputPreparer(database).PrepareAsync(
                    lease, scratchRoot, workToken.Token);
            VideoExecutionPermit afterStage = await safety.CheckAsync(
                lease, workToken.Token);
            if (!afterStage.Allowed)
                return new(false, false, afterStage.ReasonCode, null);

            var guarded = new VideoGuardedToolExecutor(new VideoToolProcessSupervisor());
            VideoGuardedToolExecutionResult execution = await guarded.RunAsync(
                executablePath, lease.ToolSha256, scratchRoot,
                workspace => approvedArguments(staged.FilePath, workspace),
                limits, checkToken => safety.CheckAsync(lease, checkToken),
                inspectionInterval: TimeSpan.FromMilliseconds(200),
                workToken.Token);

            // This deliberately remains an uncommitted execution receipt.
            // Do not call TryFinishAttemptAsync(success:true) from here:
            // no derivative is atomically persisted by WI-0188.
            return execution;
        }
        finally
        {
            workToken.Cancel();
            try { await heartbeat; }
            catch (OperationCanceledException) when (workToken.IsCancellationRequested)
            {
                // Worker is stopping after process and staged bytes cleanup.
            }
        }
    }

    private static async Task HeartbeatAsync(
        PostgresVideoProcessingJobRepository queue, VideoProcessingJobLease lease,
        CancellationTokenSource work)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(work.Token))
            {
                bool allowed;
                try
                {
                    allowed = await queue.TryHeartbeatAsync(
                        lease.Id, lease.LeaseToken, 0, work.Token);
                }
                catch (OperationCanceledException) when (work.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    allowed = false;
                }
                if (!allowed)
                {
                    work.Cancel();
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
