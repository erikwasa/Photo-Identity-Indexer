namespace PhotoIdentity.Core.Processing;

/// <summary>
/// A sanitized runtime safety decision: no source names, media paths, job
/// identifiers or exception messages are persisted in the reason code.
/// </summary>
public sealed record VideoExecutionPermit(bool Allowed, string ReasonCode)
{
    public static VideoExecutionPermit Ready { get; } = new(true, "accepted");
    public static VideoExecutionPermit Deny(string reasonCode) =>
        new(false, reasonCode);
}

public sealed record VideoGuardedToolExecutionResult(
    bool Started, bool Completed, string ReasonCode, int? ExitCode);

/// <summary>
/// Explicit-call bridge between periodic safety observations and the
/// VideoToolProcessSupervisor. This is NOT a registered video worker, process
/// sandbox or file publisher. It refuses to start when observation fails;
/// while running it revokes the child token on photo priority, job cancellation,
/// pause/stop, generation change or probe failure. The supervisor owns process
/// tree termination and scratch cleanup. A final safety read is required
/// before acknowledging even a successfully exited child.
/// </summary>
public sealed class VideoGuardedToolExecutor(VideoToolProcessSupervisor supervisor)
{
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ProbeDeadline = TimeSpan.FromSeconds(2);

    public async Task<VideoGuardedToolExecutionResult> RunAsync(
        string executablePath,
        string expectedSha256,
        string scratchRoot,
        Func<string, IReadOnlyList<string>> buildArguments,
        VideoToolExecutionBudget budget,
        Func<CancellationToken, Task<VideoExecutionPermit>> inspect,
        TimeSpan inspectionInterval,
        CancellationToken cancellationToken = default,
        Func<string, CancellationToken, Task>? onValidatedSuccessfulExit = null)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        ArgumentNullException.ThrowIfNull(inspect);
        if (inspectionInterval < TimeSpan.FromMilliseconds(100) ||
            inspectionInterval > TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(inspectionInterval),
                "Safety checks must run at least once per second.");
        cancellationToken.ThrowIfCancellationRequested();

        VideoExecutionPermit initial = await InspectFailClosedAsync(inspect, cancellationToken);
        if (!initial.Allowed)
            return new(false, false, initial.ReasonCode, null);

        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Func<string, CancellationToken, Task>? guardedPublication = null;
        if (onValidatedSuccessfulExit is not null)
        {
            guardedPublication = async (workspace, publicationToken) =>
            {
                VideoExecutionPermit permit = await InspectFailClosedAsync(
                    inspect, publicationToken);
                if (!permit.Allowed)
                    throw new InvalidOperationException(
                        "Video publication authorization was revoked.");
                await onValidatedSuccessfulExit(workspace, publicationToken);
            };
        }
        Task<VideoToolExecutionResult> execution = supervisor.RunAsync(
            executablePath, expectedSha256, scratchRoot, buildArguments,
            budget, linked.Token, afterSuccessfulExit: guardedPublication);

        try
        {
            while (true)
            {
                // A completed process is not a published derivative: check
                // cancellation, photo priority and the lease again before
                // returning a positive execution outcome.
                if (execution.IsCompleted)
                {
                    VideoToolExecutionResult result = await execution;
                    VideoExecutionPermit final = await InspectFailClosedAsync(
                        inspect, cancellationToken);
                    if (!final.Allowed)
                        return new(true, false, final.ReasonCode, null);
                    return new(true, result.ExitCode == 0,
                        result.ExitCode == 0 ? "completed" : "tool-exit-nonzero",
                        result.ExitCode);
                }

                Task delay = Task.Delay(inspectionInterval, cancellationToken);
                if (await Task.WhenAny(execution, delay) == execution)
                    continue;

                cancellationToken.ThrowIfCancellationRequested();
                VideoExecutionPermit next = await InspectFailClosedAsync(
                    inspect, cancellationToken);
                if (!next.Allowed)
                {
                    linked.Cancel();
                    await WaitForSafeTerminationAsync(execution, linked.Token);
                    return new(true, false, next.ReasonCode, null);
                }
            }
        }
        finally
        {
            // Also kill the subprocess if external cancellation or a failed
            // safety observation bypasses the normal revoked-permit path.
            linked.Cancel();
            if (!execution.IsCompleted)
                await WaitForSafeTerminationAsync(execution, linked.Token);
        }
    }

    private static async Task<VideoExecutionPermit> InspectFailClosedAsync(
        Func<CancellationToken, Task<VideoExecutionPermit>> inspect,
        CancellationToken token)
    {
        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ProbeDeadline);
        try
        {
            VideoExecutionPermit? permit = await inspect(deadline.Token)
                .WaitAsync(ProbeDeadline, token);
            return permit ?? throw new InvalidOperationException(
                "Video safety observation returned no decision.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Intentionally avoid exposing arbitrary database exception text
            // or paths from a caller-provided probe.
            throw new InvalidOperationException(
                "Video execution safety observation failed (fail closed).");
        }
    }

    private static async Task WaitForSafeTerminationAsync(
        Task<VideoToolExecutionResult> task, CancellationToken expectedCancellation)
    {
        try
        {
            await task.WaitAsync(ShutdownGrace);
        }
        catch (OperationCanceledException) when (expectedCancellation.IsCancellationRequested)
        {
            // Child observed cancellation and its supervisor finished cleanup.
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                "Video child shutdown could not be verified; preserve scratch for controlled cleanup.");
        }
    }
}
