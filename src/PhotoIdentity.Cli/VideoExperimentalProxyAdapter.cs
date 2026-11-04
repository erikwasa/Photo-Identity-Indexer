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
    public string? RequiredDerivativeKind => "PlayableProxy";

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
        if (!VideoProxyEncodingProfiles.IsEligible(selected, evidence))
            return new(false, false);
        bool fullRange = VideoProxyEncodingProfiles.IsRec709FullRange(selected.Key);

        // This pilot permits only the exact declared FFmpeg executable hash
        // embedded in the DB lease. The separate FFprobe executable is also
        // checked against an independently operator-approved digest.
        var validator = new VideoProxyOutputValidator(
            ffprobePath, ffprobeSha256, privateCacheRoot,
            selected.MaximumDimension, requireLimitedRange: fullRange);
        var worker = new VideoPublishedToolAdapter(
            database, ffmpegPath, privateCacheRoot, budget,
            (staged, workspace) => selected.BuildArguments(staged, workspace),
            async (output, checkToken) =>
            {
                var liveSafety = new PostgresVideoLeaseSafetyProbe(database);
                VideoProxyInspection validated = await validator.ValidateAsync(
                    output, inspectToken => liveSafety.CheckAsync(lease, inspectToken),
                    checkToken);
                if (fullRange && (evidence is null ||
                    !VideoRec709RealSourcePilotCommandRunner.MatchesExpectedOutput(
                        evidence, validated)))
                    throw new InvalidDataException(
                        "Full-range proxy duration, audio or orientation does not match the registered source.");
            }, publicationQuota,
            validateStagedInput: fullRange
                ? ValidateFullRangeStagedSourceAsync
                : null);
        return await worker.ProcessAsync(lease, token);

        async Task ValidateFullRangeStagedSourceAsync(
            string stagedFile,
            Func<CancellationToken, Task<VideoExecutionPermit>> inspect,
            CancellationToken ct)
        {
            bool proven = false;
            var sourceProbeBudget = new VideoToolExecutionBudget(
                MaximumWallTime: TimeSpan.FromSeconds(20),
                MaximumTemporaryBytes: 128 * 1024,
                MaximumTemporaryEntries: 8,
                InspectionInterval: TimeSpan.FromMilliseconds(100),
                WindowsJobLimits: new WindowsVideoJobLimits(
                    MaximumJobMemoryMiB: 512, MaximumProcesses: 1,
                    MaximumCpuPercent: 25,
                    MaximumObservedReadBytes: 512L * 1024 * 1024,
                    MaximumObservedWriteBytes: 32L * 1024 * 1024));

            // Run the same allowlisted FFprobe argument set and actual
            // full-range colour/geometry/rotation/audio proof accepted by
            // the private real-original evaluation. Probe only the
            // lease-staged SHA-verified bytes, never the OneDrive original.
            VideoGuardedToolExecutionResult result =
                await new VideoGuardedToolExecutor(
                    new VideoToolProcessSupervisor()).RunAsync(
                    ffprobePath, ffprobeSha256, privateCacheRoot,
                    workspace => VideoRec709RealSourcePilotCommandRunner
                        .BuildInputProbeArgs(stagedFile, workspace),
                    sourceProbeBudget, inspect,
                    inspectionInterval: TimeSpan.FromMilliseconds(200),
                    cancellationToken: ct,
                    onValidatedSuccessfulExit: async (workspace, checkToken) =>
                    {
                        string json = Path.Combine(workspace, "probe.json");
                        if (!File.Exists(json) ||
                            (File.GetAttributes(json) &
                                FileAttributes.ReparsePoint) != 0 ||
                            new FileInfo(json).Length is < 20 or > 65536)
                            throw new VideoFullRangeInputRejectionException(
                                "source-probe-data-invalid");
                        byte[] bytes = await File.ReadAllBytesAsync(json, checkToken);
                        VideoRec709FullRangeEvaluationProfile.ValidateInputProbeJson(
                            bytes, evidence, lease.SourceBytes);
                        proven = true;
                    });
            if (!result.Started || !result.Completed ||
                result.ExitCode != 0 || !proven)
                throw new VideoFullRangeInputRejectionException(
                    "source-probe-execution-incomplete");
        }
    }
}
