using System.Security.Cryptography;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Recognition.Onnx.Semantic;

namespace PhotoIdentity.Cli;

/// <summary>
/// Immutable, operator-supplied local tool/model inputs for the separately
/// reviewed FrameEvidence worker. This is NOT a CLI, hosted service, or
/// automatic activation. Ollama is permanently loopback, even if the operator
/// runs a separately authorized bridge to managed Azure infrastructure.
/// </summary>
public sealed record VideoRegisteredProxyFrameProducerOptions(
    string CacheRoot, string ScratchRoot,
    string FfmpegPath, string FfmpegSha256,
    string ClipModelPath, string ClipVocabularyPath, string ClipMergesPath,
    string OllamaModel = OllamaVisionCaptionClient.DefaultModel)
{
    public void Validate()
    {
        foreach (string path in new[] {
            CacheRoot, ScratchRoot, FfmpegPath, ClipModelPath,
            ClipVocabularyPath, ClipMergesPath
        })
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !Path.IsPathFullyQualified(path) ||
                (OperatingSystem.IsWindows() &&
                 path.StartsWith(@"\\", StringComparison.Ordinal)))
                throw new ArgumentException(
                    "Frame evidence tools and storage must have local absolute paths.");
        }

        if (!VideoDerivativeArtifactPath.IsHash(FfmpegSha256) ||
            string.IsNullOrWhiteSpace(OllamaModel) ||
            OllamaModel.Length > 100 || OllamaModel.Any(char.IsControl))
            throw new ArgumentException("Frame evidence model/tool identity is invalid.");
    }
}

/// <summary>
/// A dedicated, operator-created, marker-owned EMPTY workspace beneath the
/// current OS temporary directory. Distinct from the synthetic pilot marker
/// and from the private video-proxy cache or any media source.
/// </summary>
public static class VideoFrameEvidenceScratchGuard
{
    public const string MarkerName = ".photoidentity-frame-evidence-root";
    public const string MarkerContents = "wi0190-registered-proxy-frames-v1";

    public static string Validate(string scratchRoot)
    {
        if (string.IsNullOrWhiteSpace(scratchRoot) ||
            !Path.IsPathFullyQualified(scratchRoot) ||
            !Directory.Exists(scratchRoot) ||
            (OperatingSystem.IsWindows() &&
             scratchRoot.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new InvalidOperationException("An owned local frame scratch is required.");

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratchRoot));
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        string relative = Path.GetRelativePath(temp, root);
        if (relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative) ||
            !relative.StartsWith("photoidentity-frame-evidence-", StringComparison.Ordinal) ||
            relative.Contains(Path.DirectorySeparatorChar) ||
            relative.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidOperationException(
                "Frame scratch must be a dedicated directory directly below OS temp.");

        VideoDerivativeCacheGuard.AssertNoLinks(root);
        string marker = Path.Combine(root, MarkerName);
        if (!File.Exists(marker) ||
            (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(marker).Length != MarkerContents.Length ||
            File.ReadAllText(marker) != MarkerContents ||
            Directory.EnumerateFileSystemEntries(root).Any(path => path != marker))
            throw new InvalidOperationException(
                "Frame scratch ownership or empty-workspace invariant failed.");
        return root;
    }
}

/// <summary>
/// Checkpoint 6, opt-in-unregistered concrete producer for an *already claimed*
/// FrameEvidence lease. Uses a current, SHA-verified, locally published
/// playable proxy only; never resolves/opens the source original or hydrates
/// online-only media. The adapter owns lease heartbeats and atomic publication.
/// </summary>
public sealed class VideoRegisteredProxyFrameEvidenceProducer(
    PostgresCatalogueDatabase database,
    VideoRegisteredProxyFrameProducerOptions options) : IVideoFrameEvidenceProducer
{
    private static VideoToolExecutionBudget FrameBudget => new(
        MaximumWallTime: TimeSpan.FromSeconds(60),
        MaximumTemporaryBytes: 8L * 1024 * 1024,
        MaximumTemporaryEntries: 16,
        InspectionInterval: TimeSpan.FromMilliseconds(200),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 768, MaximumProcesses: 1,
            MaximumCpuPercent: 15));

    public async Task<VideoFrameEvidenceProduction> ProduceAsync(
        VideoProcessingJobLease lease, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "Frame processing requires Windows Job Object containment.");
        if (lease.DerivativeKind != "FrameEvidence" ||
            lease.PolicyVersion != VideoFrameSamplingPlanner.PolicyVersion ||
            lease.PresetVersion != VideoFrameExtractionProfile.Version ||
            !VideoDerivativeArtifactPath.IsHash(lease.ToolSha256) ||
            lease.ToolSha256 != options.FfmpegSha256 ||
            lease.RequestedFrames is < 1 or > 4)
            throw new InvalidOperationException(
                "Frame processing lease is not an exact approved producer identity.");

        string root = VideoFrameEvidenceScratchGuard.Validate(options.ScratchRoot);
        string cache = await VideoDerivativeCacheGuard.ValidateAsync(
            database, options.CacheRoot, token);
        if (VideoDerivativeCacheGuard.Overlaps(root, cache))
            throw new InvalidOperationException(
                "Frame scratch cannot overlap the private derivative cache.");
        VideoScratchCapacityGuard.EnsureCapacity(root,
            PostgresVideoFrameProxyPilotSource.MaximumPilotProxyBytes +
            FrameBudget.MaximumTemporaryBytes);

        // Unique app-created folder. The direct scratch root must have been
        // empty and marker-owned; no user file can be removed by this method.
        string workspace = Path.Combine(root,
            "wi0190-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var safety = new PostgresVideoLeaseSafetyProbe(database);
            async Task RequirePermit(CancellationToken ct)
            {
                VideoExecutionPermit permit = await safety.CheckAsync(lease, ct);
                if (!permit.Allowed)
                    throw new OperationCanceledException(
                        "The current frame processing permit was revoked.", ct);
            }

            await RequirePermit(token);
            var proxy = new PostgresVideoFrameProxyPilotSource(database);
            Guid? proxyJobId = await proxy.FindCurrentProxyJobAsync(
                lease.RevisionId.Value, token);
            if (proxyJobId is null)
                throw new InvalidOperationException(
                    "No eligible locally published playable proxy is current.");

            string staged = Path.Combine(workspace, "registered-proxy.mp4");
            VideoFrameProxySnapshot? snapshot = await proxy.CopyCurrentProxyAsync(
                proxyJobId.Value, options.CacheRoot, staged, token);
            if (snapshot is null || snapshot.RevisionId != lease.RevisionId.Value ||
                snapshot.ProxyBytes > lease.TemporaryBytes - FrameBudget.MaximumTemporaryBytes)
                throw new InvalidOperationException(
                    "Proxy snapshot violates revision or leased scratch budget.");

            VideoFrameSamplingPlan plan = VideoFrameSamplingPlanner.Plan(
                new VideoFrameSamplingRequest(snapshot.DurationSeconds,
                    MaximumFrames: lease.RequestedFrames));
            if (plan.Samples.Count != lease.RequestedFrames)
                throw new InvalidDataException(
                    "The media duration cannot satisfy the leased frame count.");

            await RequirePermit(token);
            await using FileStream model = new(options.ClipModelPath,
                FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            string clipSha = Convert.ToHexString(
                await SHA256.HashDataAsync(model, token)).ToLowerInvariant();
            using ClipEmbeddingEncoder encoder = new(
                options.ClipModelPath, options.ClipVocabularyPath,
                options.ClipMergesPath);
            using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(200) };
            var captions = new OllamaVisionCaptionClient(http,
                OllamaVisionCaptionClient.DefaultBaseUri, options.OllamaModel);
            LocalVisionModelDescriptor descriptor =
                await captions.GetInstalledModelAsync(token);

            VideoFrameEvidenceProvenance provenance = new(
                lease.RevisionId, plan.PolicyVersion,
                VideoFrameExtractionProfile.Version,
                ClipEmbeddingEncoder.ModelId, clipSha,
                ClipZeroShotTagger.ImagePreprocessingVersion)
            {
                CaptionModelId = descriptor.Name,
                CaptionModelSha256 = descriptor.Digest,
                CaptionPromptVersion = OllamaVisionCaptionClient.VideoFramePromptVersion
            };

            var supervisor = new VideoToolProcessSupervisor();
            List<VideoFrameEvidenceItem> items = [];
            foreach (VideoFrameSample frame in plan.Samples)
            {
                token.ThrowIfCancellationRequested();
                await RequirePermit(token);
                if (!await proxy.StillCurrentAsync(
                        proxyJobId.Value, snapshot.RevisionId, token))
                    throw new InvalidOperationException(
                        "The registered proxy is no longer current.");

                ImageFrame? image = null;
                byte[]? jpeg = null;
                VideoToolExecutionResult decoded = await supervisor.RunAsync(
                    options.FfmpegPath, options.FfmpegSha256, root,
                    ffWorkspace => VideoFrameExtractionProfile.BuildArguments(
                        staged, ffWorkspace, frame, plan.DurationMilliseconds),
                    FrameBudget, token,
                    afterSuccessfulExit: async (ffWorkspace, frameToken) =>
                    {
                        string output = Path.Combine(ffWorkspace,
                            VideoFrameExtractionProfile.OutputName);
                        image = await VideoFrameExtractionProfile
                            .ReadValidatedFrameAsync(output, frameToken);
                        jpeg = await File.ReadAllBytesAsync(output, frameToken);
                    });
                if (decoded.ExitCode != 0 || image is null || jpeg is null ||
                    jpeg.Length > VideoFrameExtractionProfile.MaximumJpegBytes)
                    throw new InvalidDataException(
                        "Bounded frame extraction failed validation.");

                // Pixel disclosure via the loopback Ollama endpoint is a
                // separately authorized operator decision. No direct Azure
                // or third-party endpoint is configurable here.
                await RequirePermit(token);
                if (!await proxy.StillCurrentAsync(
                        proxyJobId.Value, snapshot.RevisionId, token))
                    throw new InvalidOperationException(
                        "The registered proxy was revoked before captioning.");

                VideoFrameEvidenceItem item =
                    await VideoFrameModelInferenceBudget.RunAsync(async inferenceToken =>
                    {
                        var embedding = encoder.EncodeImage(image, inferenceToken);
                        LocalVisionCaptionResult caption = await captions.CaptionAsync(
                            jpeg, inferenceToken, sampledVideoFrame: true);
                        return new VideoFrameEvidenceItem(
                            frame.TimestampMilliseconds, frame.SceneIndex,
                            caption.Content, embedding.Vector.ToArray());
                    }, token);
                items.Add(item);
            }

            await RequirePermit(token);
            if (!await proxy.StillCurrentAsync(
                    proxyJobId.Value, snapshot.RevisionId, token))
                throw new InvalidOperationException(
                    "The registered proxy was revoked before publication.");
            // A model replacement during a long run must not be mislabeled
            // with the initially observed Ollama digest.
            LocalVisionModelDescriptor finishedModel =
                await captions.GetInstalledModelAsync(token);
            if (finishedModel.Name != descriptor.Name ||
                finishedModel.Digest != descriptor.Digest)
                throw new InvalidOperationException(
                    "Caption model changed during frame evidence production.");
            // Do not publish to PostgreSQL here: the lease-monitored adapter
            // and v38 transactional repository are the ONLY commit boundary.
            return new(plan, new VideoFrameEvidenceBatch(provenance, items));
        }
        finally
        {
            // Only this freshly created, random-named child may be removed.
            // A future abnormal-exit janitor is a separate work item.
            if (Directory.Exists(workspace))
            {
                VideoDerivativeCacheGuard.AssertNoLinks(workspace);
                Directory.Delete(workspace, recursive: true);
            }
        }
    }
}
