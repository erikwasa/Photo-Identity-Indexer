using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;

namespace PhotoIdentity.Cli;

/// <summary>
/// Shared-Archive WI-0188/WI-0189 SDR playable-proxy adapter:
/// revision-fenced staged video -> hash-pinned Windows-constrained FFmpeg ->
/// independent FFprobe output validation -> lease-fenced private receipt.
/// Only a separately approved, persisted fixed preset can be selected.
/// No caller-supplied source path, free-form FFmpeg flags or new video
/// scheduler is introduced; the existing photo-first Archive owns activation.
/// </summary>
public sealed class VideoExperimentalProxyAdapter(
    PostgresCatalogueDatabase database, string ffmpegPath,
    string ffprobePath, string ffprobeSha256,
    string privateCacheRoot, VideoToolExecutionBudget budget,
    VideoProxyRetentionLimits? publicationQuota = null,
    VideoProxyEncodingProfile? encodingProfile = null)
    : IVideoPublishedDerivativeAdapter
{
    public async Task<VideoDerivativeAttemptReport> ProcessAsync(
        VideoProcessingJobLease lease, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lease);
        VideoProxyEncodingProfile selected =
            encodingProfile ?? VideoProxyEncodingProfiles.Standard;
        // Only trusted, reviewed static selections are admissible. A
        // forged object with arbitrary fields must never reach FFmpeg.
        if (!Equals(selected, VideoProxyEncodingProfiles.Resolve(selected.Key)))
            throw new ArgumentException("An approved video profile is required.");
        // Never process a queued job using a different user's current
        // profile. Exact preset/version must match the leased identity.
        if (lease.DerivativeKind != "PlayableProxy" ||
            lease.PresetVersion != selected.Version ||
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
            ffprobePath, ffprobeSha256, privateCacheRoot,
            selected.MaximumDimension);
        var worker = new VideoPublishedToolAdapter(
            database, ffmpegPath, privateCacheRoot, budget,
            (staged, workspace) => selected.BuildArguments(staged, workspace),
            async (output, checkToken) =>
            {
                var liveSafety = new PostgresVideoLeaseSafetyProbe(database);
                _ = await validator.ValidateAsync(output,
                    inspectToken => liveSafety.CheckAsync(lease, inspectToken),
                    checkToken);
            }, publicationQuota);
        return await worker.ProcessAsync(lease, token);
    }
}
