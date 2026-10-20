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
    Func<string, string, IReadOnlyList<string>> approvedArguments)
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
                    committed = await new VideoDerivativeFilePublisher(database)
                        .PublishAsync(lease, scratchRoot,
                            Path.Combine(workspace, "output.mp4"), publicationToken);
                });

        // Even if a post-publication sampling tick observes a new photo job,
        // a committed receipt remains durable. The coordinator independently
        // verifies the receipt rather than trusting this in-memory flag.
        return new(committed, !committed && result.Started);
    }
}
