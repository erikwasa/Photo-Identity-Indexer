using System.Text.Json;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Source-safe, *structural* proof that explicit adaptive extraction windows
/// account for the requested whole clip. This is NOT evidence that the detector
/// found all people, that the original video bytes were hashed, or that frames
/// were decoded correctly. Nothing is hydrated, inferred, deleted or assigned.
/// </summary>
public sealed record VideoFaceCoverageWindowEvidence(
    Guid RunId, string SourceDescriptorSha256,
    long ClipDurationMs, long WindowStartMs, long WindowEndMs,
    int CoarseFrameCount, IReadOnlyList<long> RequestedFrameTimestampsMs,
    bool RefinementBudgetReached, long ExtractedFrameBytes);

public sealed record VideoFaceCoverageWindowSummary(
    long WindowStartMs, long WindowEndMs, int CoarseFrames,
    int RequestedFrames, long ExtractedFrameBytes, bool RefinementBudgetReached);

public sealed record VideoFaceWholeClipCoverageReport(
    int SchemaVersion, string VerificationScope,
    string SourceDescriptorSha256, bool SourceContentSha256Verified,
    long ClipDurationMs, int ExpectedWindows, int VerifiedWindows,
    bool WholeClipCoverageComplete, IReadOnlyList<long> MissingWindowStartsMs,
    long? NextMissingWindowStartMs, long CoveredWindowDurationMs,
    long MaximumRequestedFrameGapMs, int CoarseFrames, int RefinedFrames,
    int TotalRequestedFrames, long ExtractedFrameBytes, int WindowsAtRefinementBudget,
    IReadOnlyList<VideoFaceCoverageWindowSummary> Windows);

public static class VideoFaceWholeClipCoverage
{
    public const int MaximumWindows = 6;
    private const long UnsafeAttributes = 0x00040000 | 0x00400000 |
        (long)(FileAttributes.Offline | FileAttributes.ReparsePoint);
    private const int MaximumMetadataBytes = 4096;
    private const int MaximumManifestBytes = 24 * 1024;

    /// <summary>
    /// Offline deterministic accounting, no file access. Missing windows
    /// produce an explicit incomplete receipt; inconsistent or duplicate
    /// windows fail closed. Input order cannot change the result.
    /// </summary>
    public static VideoFaceWholeClipCoverageReport Evaluate(
        IReadOnlyList<VideoFaceCoverageWindowEvidence> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (windows.Count is < 1 or > MaximumWindows || windows.Any(w => w is null))
            throw new ArgumentException("Require one to six verified window records.");

        VideoFaceCoverageWindowEvidence first = windows[0];
        long duration = first.ClipDurationMs;
        string source = first.SourceDescriptorSha256;
        if (duration is < 200 or > VideoFaceAdaptiveSamplingPlanner.MaximumClipDurationMs ||
            !ValidDigest(source))
            throw new ArgumentException("Invalid clip duration or source descriptor.");

        int expectedWindows = checked((int)(
            (duration + VideoFaceAdaptiveSamplingPlanner.MaximumWindowLengthMs - 1) /
            VideoFaceAdaptiveSamplingPlanner.MaximumWindowLengthMs));
        var byStart = new SortedDictionary<long, VideoFaceCoverageWindowEvidence>();
        var runIds = new HashSet<Guid>();
        foreach (VideoFaceCoverageWindowEvidence window in windows)
        {
            if (window.ClipDurationMs != duration ||
                !string.Equals(window.SourceDescriptorSha256, source,
                    StringComparison.Ordinal) ||
                window.RunId == Guid.Empty || !runIds.Add(window.RunId) ||
                window.WindowStartMs < 0 ||
                window.WindowStartMs % VideoFaceAdaptiveSamplingPlanner.MaximumWindowLengthMs != 0 ||
                !byStart.TryAdd(window.WindowStartMs, window))
                throw new ArgumentException("Duplicate, unrelated or unaligned coverage windows.");

            VideoFaceSamplingPlan expected = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(
                duration, window.WindowStartMs);
            if (window.WindowEndMs != expected.WindowEndMs ||
                window.CoarseFrameCount != expected.CoarseFrameCount ||
                window.RequestedFrameTimestampsMs is null ||
                window.RequestedFrameTimestampsMs.Count is < 1 or >
                    VideoFaceAdaptiveSamplingPlanner.MaximumFramesPerWindow ||
                window.RequestedFrameTimestampsMs.Count < expected.CoarseFrameCount ||
                window.ExtractedFrameBytes < window.RequestedFrameTimestampsMs.Count * 3L ||
                window.RefinementBudgetReached &&
                    window.RequestedFrameTimestampsMs.Count !=
                        VideoFaceAdaptiveSamplingPlanner.MaximumFramesPerWindow)
                throw new ArgumentException("Coverage frames and budgets do not match the plan.");

            long preceding = expected.WindowStartMs - 1;
            var requested = new HashSet<long>();
            foreach (long time in window.RequestedFrameTimestampsMs)
            {
                if (time <= preceding || time < window.WindowStartMs ||
                    time >= window.WindowEndMs)
                    throw new ArgumentException("Requested frames must be strictly ordered and inside their window.");
                preceding = time;
                requested.Add(time);
            }

            if (expected.FrameTimestampsMs.Any(time => !requested.Contains(time)))
                throw new ArgumentException("A required coarse frame is missing.");
        }

        var missing = new List<long>();
        for (int index = 0; index < expectedWindows; index++)
        {
            long start = index * VideoFaceAdaptiveSamplingPlanner.MaximumWindowLengthMs;
            if (!byStart.ContainsKey(start)) missing.Add(start);
        }

        long covered = 0;
        long totalBytes = 0;
        int coarseFrames = 0;
        int allFrames = 0;
        int saturated = 0;
        long maximumGap = 0;
        long lastRequested = 0;
        foreach (VideoFaceCoverageWindowEvidence window in byStart.Values)
        {
            covered = checked(covered + window.WindowEndMs - window.WindowStartMs);
            totalBytes = checked(totalBytes + window.ExtractedFrameBytes);
            coarseFrames = checked(coarseFrames + window.CoarseFrameCount);
            allFrames = checked(allFrames + window.RequestedFrameTimestampsMs.Count);
            if (window.RefinementBudgetReached) saturated++;
            foreach (long t in window.RequestedFrameTimestampsMs)
            {
                maximumGap = Math.Max(maximumGap, t - lastRequested);
                lastRequested = t;
            }
        }
        maximumGap = Math.Max(maximumGap, duration - lastRequested);

        return new VideoFaceWholeClipCoverageReport(
            1, "owned-extracted-frame-manifests-only", source,
            SourceContentSha256Verified: false,
            duration, expectedWindows, windows.Count,
            WholeClipCoverageComplete: missing.Count == 0,
            missing, missing.Count == 0 ? null : missing[0],
            covered, maximumGap, coarseFrames, allFrames - coarseFrames,
            allFrames, totalBytes, saturated,
            byStart.Values.Select(window => new VideoFaceCoverageWindowSummary(
                window.WindowStartMs, window.WindowEndMs, window.CoarseFrameCount,
                window.RequestedFrameTimestampsMs.Count, window.ExtractedFrameBytes,
                window.RefinementBudgetReached)).ToArray());
    }

    /// <summary>
    /// Preflight every frame in an existing, completed, operator-owned, private
    /// adaptive workspace. No original video, pixel decoding, network access
    /// or reference/identity catalogue access.
    /// </summary>
    public static async Task<VideoFaceCoverageWindowEvidence> LoadWindowAsync(
        string manifestPath, string repositoryRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        string manifest = RequireLocalFile(manifestPath, MaximumManifestBytes);
        if (!string.Equals(Path.GetFileName(manifest), "frames.json", StringComparison.Ordinal))
            throw new ArgumentException("Only an owned frames.json manifest may be audited.");
        string folder = Path.GetDirectoryName(manifest)!;
        if (IsWithin(repositoryRoot, folder))
            throw new ArgumentException("Biometric frames must stay outside the repository.");

        string markerPath = RequireLocalFile(
            Path.Combine(folder, "wi0155-run.json"), MaximumMetadataBytes);
        string metadataPath = RequireLocalFile(
            Path.Combine(folder, "extraction-metadata.json"), MaximumMetadataBytes);
        using JsonDocument marker = JsonDocument.Parse(
            await File.ReadAllBytesAsync(markerPath, cancellationToken));
        JsonElement ownership = marker.RootElement;
        if (ownership.GetProperty("schemaVersion").GetInt32() != 1 ||
            ownership.GetProperty("kind").GetString() != "PhotoIdentity.WI0155.CoverageWorkspace" ||
            ownership.GetProperty("state").GetString() != "extracted" ||
            !Guid.TryParse(ownership.GetProperty("runId").GetString(), out Guid runId) ||
            runId == Guid.Empty)
            throw new ArgumentException("The workspace is incomplete or unowned.");

        using JsonDocument metadata = JsonDocument.Parse(
            await File.ReadAllBytesAsync(metadataPath, cancellationToken));
        JsonElement evidence = metadata.RootElement;
        if (evidence.GetProperty("schemaVersion").GetInt32() != 1 ||
            evidence.GetProperty("samplingMode").GetString() != "Adaptive")
            throw new ArgumentException("Whole-clip accounting requires adaptive extraction.");

        long duration = evidence.GetProperty("samplingClipDurationMs").GetInt64();
        long start = evidence.GetProperty("samplingWindowStartMs").GetInt64();
        VideoFaceSamplingPlan planned = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(
            duration, start);
        if (start % VideoFaceAdaptiveSamplingPlanner.MaximumWindowLengthMs != 0 ||
            evidence.GetProperty("samplingWindowEndMs").GetInt64() != planned.WindowEndMs ||
            evidence.GetProperty("samplingCoarseFrameCount").GetInt32() != planned.CoarseFrameCount ||
            evidence.GetProperty("samplingHasMoreWindows").GetBoolean() != planned.HasMoreWindows)
            throw new ArgumentException("The window provenance does not match the planner.");

        string source = evidence.GetProperty("sourceDescriptorSha256").GetString() ?? "";
        if (!ValidDigest(source))
            throw new ArgumentException("Source descriptor is missing; older reports cannot prove cross-window coherence.");

        string encoding = evidence.GetProperty("frameEncoding").GetString() ?? "";
        string extension = encoding switch
        {
            "NativeLosslessPng" => "png",
            "LegacyJpeg" => "jpg",
            _ => throw new ArgumentException("Unsupported approved coverage frame encoding."),
        };
        bool capped = evidence.GetProperty("samplingRefinementBudgetReached").GetBoolean();
        long declaredBytes = evidence.GetProperty("extractedFrameBytes").GetInt64();
        int workspaceMiB = evidence.GetProperty("maximumWorkspaceMiB").GetInt32();
        if (workspaceMiB is < 8 or > 2048 ||
            declaredBytes < 1 || declaredBytes > workspaceMiB * 1_048_576L)
            throw new ArgumentException("Reported frame storage exceeds the owned workspace budget.");

        VideoFaceFrameSet frames = await VideoFaceFrameSetLoader.LoadAsync(
            manifest, coverage: true);
        if (frames.Frames is null || frames.Frames.Count < planned.CoarseFrameCount ||
            frames.Frames.Count > VideoFaceAdaptiveSamplingPlanner.MaximumFramesPerWindow)
            throw new ArgumentException("Invalid bounded frame count.");

        var requested = new List<long>(frames.Frames.Count);
        long actualBytes = 0;
        for (int index = 0; index < frames.Frames.Count; index++)
        {
            VideoFaceFrame frame = frames.Frames[index];
            string expectedName = $"frame-{index + 1:D3}.{extension}";
            if (!string.Equals(frame.File, expectedName, StringComparison.Ordinal))
                throw new ArgumentException("Unexpected frame name or extraction encoding.");
            string approved = VideoFaceFrameSetLoader.ResolveApprovedFrame(folder, frame.File);
            string file = RequireLocalFile(approved, VideoFaceFrameSetLoader.MaximumFrameBytes);
            actualBytes = checked(actualBytes + new FileInfo(file).Length);
            requested.Add(checked(start + frame.TimestampMs));
        }
        // A completed workspace must contain ONLY its owned manifest,
        // metadata, marker and declared frames. Unknown extra files
        // (including links and unlisted crops) invalidate completeness.
        var allowedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "frames.json", "wi0155-run.json", "extraction-metadata.json",
        };
        foreach (VideoFaceFrame frame in frames.Frames)
            allowedNames.Add(frame.File);
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            if (!allowedNames.Contains(Path.GetFileName(entry)))
                throw new ArgumentException("Unexpected file in completed coverage workspace.");
        }

        if (actualBytes != declaredBytes)
            throw new ArgumentException("The extracted-frame byte total does not match metadata.");

        VideoFaceCoverageWindowEvidence window = new(
            runId, source, duration, start, planned.WindowEndMs,
            planned.CoarseFrameCount, requested, capped, actualBytes);
        // Also validate the entire window alone so even a partial aggregation
        // cannot mistake a missing coarse frame for a complete extraction.
        _ = Evaluate([window]);
        return window;
    }

    public static async Task<VideoFaceWholeClipCoverageReport> AuditAsync(
        IReadOnlyList<string> manifests, string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        if (manifests.Count is < 1 or > MaximumWindows)
            throw new ArgumentException("A bounded list of one to six existing manifests is required.");
        var windows = new List<VideoFaceCoverageWindowEvidence>(manifests.Count);
        foreach (string path in manifests)
            windows.Add(await LoadWindowAsync(path, repositoryRoot, cancellationToken));
        return Evaluate(windows);
    }

    public static async Task WritePrivateReportAsync(
        string outputPath, string repositoryRoot, IReadOnlyList<string> manifests,
        VideoFaceWholeClipCoverageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!Path.IsPathFullyQualified(outputPath) ||
            !string.Equals(Path.GetExtension(outputPath), ".json",
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Output must be a new private absolute JSON file.");
        string output = Path.GetFullPath(outputPath);
        string folder = Path.GetDirectoryName(output)!;
        AssertLocalDirectory(folder);
        if (IsWithin(repositoryRoot, output) ||
            manifests.Any(m => IsWithin(Path.GetDirectoryName(Path.GetFullPath(m))!, output)) ||
            File.Exists(output) || Directory.Exists(output))
            throw new ArgumentException("Report must be create-new outside the repository and frame workspaces.");

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        bool created = false;
        try
        {
            await using var stream = new FileStream(output, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, useAsync: true);
            created = true;
            await stream.WriteAsync(bytes);
        }
        catch
        {
            if (created) File.Delete(output);
            throw;
        }
    }

    private static string RequireLocalFile(string path, long maximumBytes)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("A fully qualified local input is required.");
        string full = Path.GetFullPath(path);
        AssertLocalDirectory(Path.GetDirectoryName(full)!);
        FileInfo info = new(full);
        if (!info.Exists || info.Length is < 3 || info.Length > maximumBytes ||
            ((long)info.Attributes & UnsafeAttributes) != 0)
            throw new ArgumentException("Frame evidence is missing, unsafe or over budget.");
        return full;
    }

    private static void AssertLocalDirectory(string path)
    {
        for (DirectoryInfo? dir = new DirectoryInfo(Path.GetFullPath(path));
            dir is not null; dir = dir.Parent)
        {
            if (!dir.Exists || ((long)dir.Attributes & UnsafeAttributes) != 0)
                throw new ArgumentException("Private input directory is unavailable, linked or offline.");
        }
    }

    private static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative == "." ||
            (!Path.IsPathRooted(relative) && relative != ".." &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static bool ValidDigest(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}

/// <summary>
/// Explicit CLI for previously approved private extraction workspaces.
/// stdout is aggregate only. The full, source-bound receipt stays private.
/// </summary>
public static class VideoFaceWholeClipCoverageCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var manifests = new List<string>();
            string? output = null, root = null;
            bool command = false;
            for (int i = 0; i < args.Length; i++)
            {
                string option = args[i];
                if (option == "--audit-adaptive-coverage")
                {
                    if (command) throw new ArgumentException("Repeated command.");
                    command = true;
                    continue;
                }
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    throw new ArgumentException("Missing option value.");
                string value = args[i];
                switch (option)
                {
                    case "--manifest" when manifests.Count < VideoFaceWholeClipCoverage.MaximumWindows:
                        manifests.Add(value);
                        break;
                    case "--private-report" when output is null:
                        output = value;
                        break;
                    case "--root" when root is null:
                        root = value;
                        break;
                    default: throw new ArgumentException("Unknown or repeated coverage audit option.");
                }
            }
            if (!command || manifests.Count == 0 || output is null)
                throw new ArgumentException("Existing private window manifests and output are required.");
            string repo = root ?? Directory.GetCurrentDirectory();
            VideoFaceWholeClipCoverageReport report =
                await VideoFaceWholeClipCoverage.AuditAsync(manifests, repo);
            await VideoFaceWholeClipCoverage.WritePrivateReportAsync(
                output, repo, manifests, report);

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = report.SchemaVersion,
                sourceBoundByDescriptor = true,
                sourceContentHashVerified = report.SourceContentSha256Verified,
                wholeClipCoverageComplete = report.WholeClipCoverageComplete,
                expectedWindows = report.ExpectedWindows,
                verifiedWindows = report.VerifiedWindows,
                missingWindows = report.MissingWindowStartsMs.Count,
                totalRequestedFrames = report.TotalRequestedFrames,
                coarseFrames = report.CoarseFrames,
                refinementFrames = report.RefinedFrames,
                windowsAtRefinementBudget = report.WindowsAtRefinementBudget,
                extractedFrameBytes = report.ExtractedFrameBytes,
                maximumRequestedFrameGapMs = report.MaximumRequestedFrameGapMs,
                privateReceiptWritten = true,
            }));
            // Incomplete coverage is a *valid audited finding*, not a claim
            // of completion and not an error that discards verified evidence.
            return 0;
        }
        catch
        {
            Console.Error.WriteLine(
                "error: adaptive coverage evidence was unavailable, unsafe or inconsistent.");
            return 1;
        }
    }
}
