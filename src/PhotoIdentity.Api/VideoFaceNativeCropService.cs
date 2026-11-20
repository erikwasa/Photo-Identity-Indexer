using PhotoIdentity.Cli;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Source.OneDriveSync;

namespace PhotoIdentity.Api;

/// <summary>
/// Dedicated owner-marked root directly beneath OS temp. No fallback to the
/// generic video cache, source root or unowned scratch. Uncleaned leftovers
/// deny a later request rather than silently deleting an unknown directory.
/// </summary>
public static class VideoFaceNativeCropScratchGuard
{
    public const string MarkerName = ".photoidentity-native-face-crop-root";
    public const string MarkerContents = "wi0155-native-face-crop-read-v1";
    public const string Prefix = "photoidentity-native-face-review-";

    public static string Validate(string? scratchRoot)
    {
        if (string.IsNullOrWhiteSpace(scratchRoot) ||
            !Path.IsPathFullyQualified(scratchRoot) ||
            !Directory.Exists(scratchRoot) ||
            (OperatingSystem.IsWindows() &&
             scratchRoot.StartsWith(@"\\", StringComparison.Ordinal)))
            throw new InvalidOperationException("A dedicated owned local native crop scratch is required.");

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scratchRoot));
        string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        string relative = Path.GetRelativePath(temp, root);
        if (!relative.StartsWith(Prefix, StringComparison.Ordinal) ||
            relative.Length <= Prefix.Length ||
            relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative) ||
            relative.Contains(Path.DirectorySeparatorChar) ||
            relative.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidOperationException("Native face crop scratch must be directly beneath OS temp.");

        VideoDerivativeCacheGuard.AssertNoLinks(root);
        string marker = Path.Combine(root, MarkerName);
        if (!File.Exists(marker) ||
            (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(marker).Length != MarkerContents.Length ||
            File.ReadAllText(marker) != MarkerContents ||
            Directory.EnumerateFileSystemEntries(root).Any(entry => entry != marker))
            throw new InvalidOperationException("Native face crop scratch ownership or emptiness check failed.");
        return root;
    }
}

public sealed class VideoFaceNativeCropResponse(
    VideoFaceNativeCropJpeg crop, VideoFaceNativeCropSourceLease source) : IAsyncDisposable
{
    public VideoFaceNativeCropJpeg Crop { get; } = crop;

    public async ValueTask DisposeAsync()
    {
        try { Array.Clear(Crop.Bytes); }
        finally { await source.DisposeAsync(); }
    }
}

/// <summary>
/// Explicit, default-off on-demand native crop reader. No registered worker,
/// new database row, face inference, persistent crop cache, source hydration,
/// identity choice or video/person association. One bounded request at a time.
/// </summary>
public sealed class VideoFaceNativeCropService(
    PostgresVideoFaceNativeCropRepository repository,
    PostgresCatalogueDatabase database)
{
    private readonly SemaphoreSlim _singleCrop = new(1, 1);
    private static VideoToolExecutionBudget FrameBudget => new(
        MaximumWallTime: TimeSpan.FromSeconds(60),
        MaximumTemporaryBytes: 16L * 1024 * 1024,
        MaximumTemporaryEntries: 12,
        InspectionInterval: TimeSpan.FromMilliseconds(200),
        WindowsJobLimits: new WindowsVideoJobLimits(
            MaximumJobMemoryMiB: 768, MaximumProcesses: 1,
            MaximumCpuPercent: 20));

    public async Task<VideoFaceNativeCropResponse?> OpenAsync(
        AssetRevisionId revision, Guid observationId, IConfiguration settings,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!OperatingSystem.IsWindows() || revision.IsEmpty ||
            observationId == Guid.Empty)
            return null;

        string? executable = settings["PhotoIdentity:VideoFaceNativeCrop:FfmpegPath"];
        string? expectedHash = settings["PhotoIdentity:VideoFaceNativeCrop:FfmpegSha256"];
        string? scratchRoot = settings["PhotoIdentity:VideoFaceNativeCrop:ScratchRoot"];
        if (string.IsNullOrWhiteSpace(executable) ||
            !Path.IsPathFullyQualified(executable) ||
            executable.StartsWith(@"\\", StringComparison.Ordinal) ||
            expectedHash is not { Length: 64 } ||
            !expectedHash.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return null;

        // An explicitly owner-marked empty scratch is required before any
        // source is resolved. Do not create scratch silently on request.
        if (!await _singleCrop.WaitAsync(TimeSpan.FromSeconds(2), token))
            return null;
        try
        {
            string root = VideoFaceNativeCropScratchGuard.Validate(scratchRoot);
            VideoFaceNativeCropSourceLease? lease = await repository.OpenAsync(
                revision, observationId, expectedHash, token);
            if (lease is null) return null;

            try
            {
                VideoFaceNativeCropSource entry = lease.Source;
                // Fail early rather than recalling online-only video or
                // allocating an unbounded temporary file.
                VideoScratchCapacityGuard.EnsureCapacity(
                    root, checked(entry.SourceBytes + FrameBudget.MaximumTemporaryBytes));
                var priority = new PostgresVideoPhotoPriorityProbe(database);
                async Task<VideoExecutionPermit> InspectAsync(CancellationToken ct)
                {
                    VideoPhotoPrioritySnapshot current = await priority.GetAsync(ct);
                    return current.PhotoWorkActive
                        ? VideoExecutionPermit.Deny("photo-priority")
                        : VideoExecutionPermit.Ready;
                }
                if (!(await InspectAsync(token)).Allowed)
                    throw new InvalidOperationException(
                        "Photo processing takes priority over private native crop review.");

                OneDriveVideoAssetSource source = new(
                    SourceId.From(entry.SourceId), entry.RootLocator);
                OneDriveVideoLocalStager stager = new(source);
                await using VideoStagedLocalCopy staged = await stager.StageAsync(
                    new SourceAssetReference(source.SourceId, entry.SourceKey),
                    entry.SourceBytes, entry.SourceSha256, root,
                    async ct => (await InspectAsync(ct)).Allowed, token);

                ImageFrame? nativeFrame = null;
                VideoGuardedToolExecutionResult execution =
                    await new VideoGuardedToolExecutor(new VideoToolProcessSupervisor()).RunAsync(
                        executable, expectedHash, root,
                        workspace => VideoFaceNativeFrameProfile.BuildArguments(
                            staged.FilePath, workspace,
                            new VideoFrameSample(entry.TimestampMs, null),
                            entry.DurationMilliseconds),
                        FrameBudget, InspectAsync,
                        TimeSpan.FromMilliseconds(250), token,
                        onValidatedSuccessfulExit: async (workspace, ct) =>
                        {
                            nativeFrame = await VideoFaceNativeFrameProfile.ReadValidatedFrameAsync(
                                Path.Combine(workspace, VideoFaceNativeFrameProfile.OutputName), ct);
                        });

                if (!execution.Completed || nativeFrame is null)
                    throw new InvalidDataException(
                        "The native video face review frame could not be verified.");

                VideoFaceNativeCropJpeg crop = VideoFaceNativeCropRenderer.Render(
                    nativeFrame, entry.FrameWidth, entry.FrameHeight,
                    entry.X, entry.Y, entry.Width, entry.Height, token);
                if (!(await InspectAsync(token)).Allowed)
                    throw new InvalidOperationException(
                        "Photo processing resumed before private native crop response.");
                // The source and asset locks remain held until the JPEG bytes
                // have finished their HTTP response or the client aborts.
                return new VideoFaceNativeCropResponse(crop, lease);
            }
            catch
            {
                await lease.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _singleCrop.Release();
        }
    }
}
