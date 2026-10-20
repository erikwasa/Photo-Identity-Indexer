using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Explicitly unregistered candidate for WI-0188/WI-0189 integration:
/// selected/current SDR-only revision -> hash-pinned FFmpeg constrained by
/// Windows Job Object -> independently constrained FFprobe -> lease-fenced
/// private receipt. No API, operator activation, hosted service or scheduler.
/// No source path is accepted from the caller; it comes from the existing
/// revision-fenced staging boundary.
/// </summary>
public sealed class VideoExperimentalProxyAdapter(
    PostgresCatalogueDatabase database, string ffmpegPath,
    string ffprobePath, string ffprobeSha256,
    string privateCacheRoot, VideoToolExecutionBudget budget)
    : IVideoPublishedDerivativeAdapter
{
    public async Task<VideoDerivativeAttemptReport> ProcessAsync(
        VideoProcessingJobLease lease, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (lease.DerivativeKind != "PlayableProxy" ||
            lease.PresetVersion != VideoProxyCandidateProfile.Version ||
            !OperatingSystem.IsWindows() ||
            budget.WindowsJobLimits?.MaximumProcesses != 1 ||
            budget.MaximumTemporaryBytes > VideoProxyCandidateProfile.MaximumOutputBytes)
            return new(false, false);

        VideoRevisionInspection? evidence =
            await new PostgresVideoEvidenceRepository(database)
                .GetInspectionAsync(lease.RevisionId, token);
        if (!VideoProxyCandidateProfile.Eligible(evidence))
            return new(false, false);

        // This pilot permits only the exact declared FFmpeg executable hash
        // embedded in the DB lease. The separate FFprobe executable is also
        // checked against an independently operator-approved digest.
        var validator = new VideoProxyOutputValidator(
            ffprobePath, ffprobeSha256, privateCacheRoot);
        var worker = new VideoPublishedToolAdapter(
            database, ffmpegPath, privateCacheRoot, budget,
            VideoProxyCandidateProfile.BuildArguments,
            async (output, checkToken) =>
            {
                _ = await validator.ValidateAsync(output, checkToken);
            });
        return await worker.ProcessAsync(lease, token);
    }
}
