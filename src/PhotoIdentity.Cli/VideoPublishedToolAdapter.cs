using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Unregistered integration adapter proving the staged-input -> bounded
/// subprocess -> current-lease validation -> private artifact commit path.
/// No approved production FFmpeg argument profile or video enable mechanism
/// is provided by this type. Downstream WI-0189 must review encoding,
/// media validation and output size/quality before any runtime registration.
/// </summary>
public sealed class VideoPublishedToolAdapter(
    PostgresCatalogueDatabase database, string executablePath, string scratchRoot,
    VideoToolExecutionBudget budget,
    Func<string, string, IReadOnlyList<string>> approvedArguments,
    Func<string, CancellationToken, Task>? validateBeforePublish = null,
    VideoProxyRetentionLimits? publicationQuota = null)
    : IVideoPublishedDerivativeAdapter
{
    public async Task<VideoDerivativeAttemptReport> ProcessAsync(
        VideoProcessingJobLease lease, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.DerivativeKind != "PlayableProxy")
            return new(false, false);

        bool committed = false;
        VideoGuardedToolExecutionResult result =
            await new VideoUnpublishedToolAttempt(database).RunAsync(
                lease, executablePath, scratchRoot, budget, approvedArguments,
                token: token,
                onValidatedSuccessfulExit: async (workspace, publicationToken) =>
                {
                    // The child has fully exited. The staged original has
                    // not been touched and the private tool workspace exists
                    // until this callback completes; the guarded executor
                    // just rechecked photo priority and lease permission.
                    // PostgreSQL publication re-locks source, control and
                    // job before committing the receipt and succeeded state.
                    string output = Path.Combine(workspace, "output.mp4");
                    if (validateBeforePublish is not null)
                    {
                        try { await validateBeforePublish(output, publicationToken); }
                        catch (Exception error)
                        {
                            VideoAttemptFailureDiagnostics.Mark(error, "output-validation");
                            throw;
                        }
                    }
                    try
                    {
                        committed = await new VideoDerivativeFilePublisher(database)
                            .PublishAsync(lease, scratchRoot, output,
                                publicationToken, publicationQuota);
                    }
                    catch (Exception error)
                    {
                        VideoAttemptFailureDiagnostics.Mark(error, "publication");
                        throw;
                    }
                });

        // Even if a post-publication sampling tick observes a new photo job,
        // a committed receipt remains durable. The coordinator independently
        // verifies the receipt rather than trusting this in-memory flag.
        if (committed)
            return new(true, false);

        // A nonzero FFmpeg exit and a guarded interruption previously
        // collapsed into the same generic retry. Both are first-party,
        // fixed labels; never return ffmpeg stderr or a media filename.
        VideoAttemptFailureDiagnostic failure = result switch
        {
            { Started: false } =>
                new("tool-execution", "execution-not-started"),
            { ExitCode: not null and not 0 } =>
                new("tool-execution", "tool-exit-nonzero",
                    ToolExitCode: result.ExitCode),
            { Completed: false } =>
                new("tool-execution", "execution-interrupted"),
            _ => new("publication", "not-committed")
        };
        return new(false, result.Started, failure);
    }
}
