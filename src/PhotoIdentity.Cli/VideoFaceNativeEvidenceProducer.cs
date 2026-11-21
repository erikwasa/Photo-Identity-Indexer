using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.SFace;
using PhotoIdentity.Recognition.Onnx.YuNet;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Cli;

/// <summary>
/// Operator-authorized inputs only. Invoked only by the explicit selected
/// video faces one-shot CLI after a time-limited FaceEvidence lease is claimed.
/// Never registered as a hosted worker or available through a public API.
/// </summary>
public sealed record VideoFaceNativeProducerOptions(
    string ScratchRoot, string FfmpegPath, string FfmpegSha256,
    string ManifestDirectory, string ModelDirectory)
{
    public void Validate()
    {
        foreach (string path in new[] {
            ScratchRoot, FfmpegPath, ManifestDirectory, ModelDirectory
        })
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !Path.IsPathFullyQualified(path) ||
                (OperatingSystem.IsWindows() &&
                 path.StartsWith(@"\\", StringComparison.Ordinal)))
                throw new ArgumentException(
                    "Native video face processing requires local absolute inputs.");
        }
        if (!VideoDerivativeArtifactPath.IsHash(FfmpegSha256))
            throw new ArgumentException("Approved FFmpeg digest is required.");
    }
}

/// <summary>
/// Unregistered single-lease native video face *producer*. Stages only
/// source-verified current local video bytes; invokes digest-pinned FFmpeg
/// with Windows Job Object limits, and runs approved YuNet/SFace in memory.
///
/// Deliberately returns ONLY an ephemeral embedding proposal. It cannot
/// publish evidence, complete jobs, create identity assignments, or
/// independently authorize a new derivative kind. The selected
/// VideoFaceEvidenceWorkerAdapter monitors the lease through the *scoring
/// and database commit* stage before invoking PostgresVideoFaceMatchPublisher.
/// </summary>
public sealed class VideoFaceNativeEvidenceProducer(
    PostgresCatalogueDatabase database, VideoFaceNativeProducerOptions options)
    : IVideoFaceEmbeddingProducer
{
    private static VideoToolExecutionBudget FrameBudget => new(
        MaximumWallTime: TimeSpan.FromSeconds(60),
        MaximumTemporaryBytes: 16L * 1024 * 1024,
        MaximumTemporaryEntries: 12,
        InspectionInterval: TimeSpan.FromMilliseconds(200),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 768, MaximumProcesses: 1,
            MaximumCpuPercent: 20));

    public async Task<VideoFaceEmbeddingPublication?> ProduceAsync(
        VideoProcessingJobLease lease, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!OperatingSystem.IsWindows() ||
            !VideoFaceEvidenceLeaseIdentity.Matches(
                lease.DerivativeKind, lease.PolicyVersion,
                lease.PresetVersion, lease.RequestedFrames) ||
            lease.ToolSha256 != options.FfmpegSha256 ||
            lease.RequestedFrames is < 1 or > VideoFaceNativeFrameAnalyzer.MaximumFramesPerAttempt ||
            lease.SourceBytes is < 1 or > 1024L * 1024 * 1024 ||
            lease.TemporaryBytes < lease.SourceBytes + FrameBudget.MaximumTemporaryBytes)
            throw new InvalidOperationException(
                "No exact approved FaceEvidence lease and bounded source budget.");

        string root = VideoFrameEvidenceScratchGuard.Validate(options.ScratchRoot);
        VideoScratchCapacityGuard.EnsureCapacity(root, lease.TemporaryBytes);
        var queue = new PostgresVideoProcessingJobRepository(database);
        var safety = new PostgresVideoLeaseSafetyProbe(database);
        using CancellationTokenSource work = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task monitor = MonitorLeaseAsync(queue, safety, lease, work);
        try
        {
            await RequirePermit(work.Token);
            VideoRevisionInspection? inspection =
                await new PostgresVideoEvidenceRepository(database).GetInspectionAsync(
                    lease.RevisionId, work.Token);
            if (inspection is null || inspection.InspectionStatus != "supported" ||
                inspection.ColorTransfer != "bt709" ||
                inspection.DurationSeconds is null ||
                inspection.DurationSeconds.Value is < 0.2m or > 3600m ||
                inspection.CodedWidth is null ||
                inspection.CodedWidth.Value is < 1 or > VideoFaceNativeFrameProfile.MaximumEdge ||
                inspection.CodedHeight is null ||
                inspection.CodedHeight.Value is < 1 or > VideoFaceNativeFrameProfile.MaximumEdge)
                throw new InvalidOperationException(
                    "Native face processing requires inspected bounded Rec.709 source geometry.");

            decimal duration = inspection.DurationSeconds.Value;

            // Resolve SHA evidence from the registered catalogue, never
            // from an operator-supplied source filename or manifest.
            VideoLeasedLocalSource? source =
                await new PostgresVideoLocalSourceLocator(database).ResolveAsync(
                    lease, work.Token);
            if (source is null)
                throw new InvalidOperationException("The current video source is not authorized.");

            await using VideoStagedLocalCopy staged =
                await new VideoLeasedInputPreparer(database).PrepareAsync(
                    lease, root, work.Token);
            await RequirePermit(work.Token);

            ModelManifestLoader loader = new();
            ModelManifest yunet = await loader.LoadAsync(
                Path.Combine(options.ManifestDirectory, "yunet-2023mar-fp32.json"),
                work.Token);
            ModelManifest sface = await loader.LoadAsync(
                Path.Combine(options.ManifestDirectory, "sface-2021dec-fp32.json"),
                work.Token);
            VideoFaceNativeFrameAnalyzer.ValidateModels(
                yunet.ToDescriptor(), sface.ToDescriptor());
            string yunetFile = Path.Combine(options.ModelDirectory, yunet.FileName);
            string sfaceFile = Path.Combine(options.ModelDirectory, sface.FileName);
            ModelFileVerifier verifier = new();
            await verifier.VerifyOrThrowAsync(yunetFile, yunet);
            await verifier.VerifyOrThrowAsync(sfaceFile, sface);
            await RequirePermit(work.Token);

            using YuNetFaceDetector detector = new(yunet, yunetFile);
            using SFaceFaceEmbedder embedder = new(sface, sfaceFile);
            var analyzer = new VideoFaceNativeFrameAnalyzer(
                detector, new OpenCvFaceAligner(), embedder);
            VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
                new VideoFrameSamplingRequest(duration,
                    MaximumFrames: lease.RequestedFrames,
                    MaximumReservedDecodeMilliseconds: 12000,
                    ReservedDecodeMillisecondsPerFrame: 3000));
            if (plan.Samples.Count != lease.RequestedFrames)
                throw new InvalidOperationException("Face sampling plan is not bounded.");

            List<VideoFaceEmbeddingObservation> observations = [];
            var supervisor = new VideoToolProcessSupervisor();
            for (int i = 0; i < plan.Samples.Count; i++)
            {
                work.Token.ThrowIfCancellationRequested();
                await RequirePermit(work.Token);
                ImageFrame? frame = null;
                VideoFrameSample sample = plan.Samples[i];
                VideoToolExecutionResult result;
                try
                {
                    result = await supervisor.RunAsync(
                        options.FfmpegPath, options.FfmpegSha256, root,
                        workspace => VideoFaceNativeFrameProfile.BuildArguments(
                            staged.FilePath, workspace, sample,
                            plan.DurationMilliseconds),
                        FrameBudget, work.Token,
                        afterSuccessfulExit: async (workspace, ct) =>
                        {
                            try
                            {
                                frame = await VideoFaceNativeFrameProfile.ReadValidatedFrameAsync(
                                    Path.Combine(workspace, VideoFaceNativeFrameProfile.OutputName), ct);
                            }
                            catch (InvalidDataException error)
                            {
                                throw VideoFaceFailureEvidence.Mark(error, "native-frame-validation");
                            }
                        });
                }
                catch (InvalidDataException error)
                {
                    // Respect any more specific checkpoint added in the callback.
                    throw VideoFaceFailureEvidence.Mark(error, "native-frame-extraction");
                }
                if (result.ExitCode != 0 || frame is null)
                    throw VideoFaceFailureEvidence.Mark(
                        new InvalidDataException("Native video face frame extraction was incomplete."),
                        "native-frame-extraction",
                        result.ExitCode);

                await RequirePermit(work.Token);
                using CancellationTokenSource boundedInference =
                    CancellationTokenSource.CreateLinkedTokenSource(work.Token);
                boundedInference.CancelAfter(TimeSpan.FromSeconds(90));
                IReadOnlyList<VideoFaceEmbeddingObservation> faces =
                    await analyzer.AnalyzeAsync(frame, i,
                        sample.TimestampMilliseconds, boundedInference.Token);
                observations.AddRange(faces);
            }

            await RequirePermit(work.Token);
            // Even a complete, all-empty sampling pass must produce a
            // source-attested receipt under the live lease. Zero observations
            // never imply that the entire video contains no people.
            // The eventual guarded adapter must recheck this source/lease
            // through score+commit; this producer itself writes NOTHING.
            return new VideoFaceEmbeddingPublication(
                lease.RevisionId, source.Sha256,
                "wi0155-native-face-evidence-v1",
                yunet.ModelId, yunet.Sha256,
                sface.ModelId, sface.Sha256,
                "sface-five-point-v1", observations);
        }
        finally
        {
            work.Cancel();
            await monitor;
        }

        async Task RequirePermit(CancellationToken ct)
        {
            VideoExecutionPermit permit = await safety.CheckAsync(lease, ct);
            if (!permit.Allowed)
                throw new OperationCanceledException(
                    "Native video face source/lease authorization was revoked.", ct);
        }
    }

    private static async Task MonitorLeaseAsync(
        PostgresVideoProcessingJobRepository queue,
        PostgresVideoLeaseSafetyProbe safety,
        VideoProcessingJobLease lease, CancellationTokenSource work)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(work.Token))
            {
                bool allowed;
                try
                {
                    allowed = await queue.TryHeartbeatAsync(
                        lease.Id, lease.LeaseToken, 0, work.Token) &&
                        (await safety.CheckAsync(lease, work.Token)).Allowed;
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
            // Normal owner-controlled cancellation on exit.
        }
    }
}
