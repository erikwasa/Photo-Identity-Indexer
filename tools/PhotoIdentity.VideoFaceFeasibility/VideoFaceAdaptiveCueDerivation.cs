using System.Text.Json;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Diagnostic evidence from an ALREADY examined, approved coarse frame.
/// No people, vectors, coordinates, identity scores or labels are used.
/// </summary>
public sealed record VideoFaceObservedCoarseFrame(
    int FrameNumber,
    long AbsoluteTimestampMs,
    int FacesDetected,
    double? LowestConfidence,
    double? SmallestFaceWidthPixels,
    int CandidatesDiscarded);

/// <summary>
/// Derives optional, strictly exploratory refinement cues from a completed
/// offline detector-only summary. Never removes coarse frames or assigns IDs.
/// A count transition is NOT proof of a real entrance or a distinct person.
/// </summary>
public static class VideoFaceAdaptiveCueDeriver
{
    // Exploratory review signals, not a validated face-quality filter.
    public const double SmallFaceWidthPixels = 64;
    public const double LowDetectionConfidence = 0.70;

    public static IReadOnlyList<VideoFaceSamplingCue> Derive(
        VideoFaceSamplingPlan coarsePlan,
        IReadOnlyList<VideoFaceObservedCoarseFrame> observed)
    {
        ArgumentNullException.ThrowIfNull(coarsePlan);
        ArgumentNullException.ThrowIfNull(observed);

        VideoFaceSamplingPlan expected = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(
            coarsePlan.ClipDurationMs, coarsePlan.WindowStartMs);
        if (coarsePlan.WindowEndMs != expected.WindowEndMs ||
            coarsePlan.CoarseFrameCount != expected.CoarseFrameCount ||
            !coarsePlan.FrameTimestampsMs.SequenceEqual(expected.FrameTimestampsMs) ||
            observed.Count != expected.CoarseFrameCount)
        {
            throw new ArgumentException("Only a complete, unrefined coarse window is valid.");
        }

        var anchors = new SortedDictionary<long, VideoFaceSamplingCueKind>();
        void Add(long timestamp, VideoFaceSamplingCueKind kind)
        {
            if (!anchors.TryGetValue(timestamp, out var prior) || kind > prior)
                anchors[timestamp] = kind;
        }

        for (int i = 0; i < observed.Count; i++)
        {
            VideoFaceObservedCoarseFrame frame = observed[i];
            if (frame is null || frame.FrameNumber != i + 1 ||
                frame.AbsoluteTimestampMs != expected.FrameTimestampsMs[i] ||
                frame.FacesDetected is < 0 or > 32 ||
                frame.CandidatesDiscarded < 0 ||
                (frame.LowestConfidence is double confidence &&
                    (!double.IsFinite(confidence) || confidence is < 0 or > 1)) ||
                (frame.SmallestFaceWidthPixels is double width &&
                    (!double.IsFinite(width) || width <= 0)))
            {
                throw new ArgumentException("Coarse detector observations must match the entire plan.");
            }

            if (frame.FacesDetected == 0 &&
                (frame.LowestConfidence is not null || frame.SmallestFaceWidthPixels is not null))
            {
                throw new ArgumentException("An empty frame cannot contain face-quality measurements.");
            }

            if (i == 0 && frame.FacesDetected > 0)
            {
                // A face already present at the beginning may deserve more
                // neighboring samples; this is not evidence of an entrance.
                Add(frame.AbsoluteTimestampMs, VideoFaceSamplingCueKind.PossibleEntranceOrExit);
            }
            if (i > 0 && observed[i - 1].FacesDetected != frame.FacesDetected)
            {
                // Densify BOTH sides of a possible appearance/disappearance,
                // including the last frame where the detector saw no face.
                Add(observed[i - 1].AbsoluteTimestampMs,
                    VideoFaceSamplingCueKind.PossibleEntranceOrExit);
                Add(frame.AbsoluteTimestampMs,
                    VideoFaceSamplingCueKind.PossibleEntranceOrExit);
            }

            if (frame.FacesDetected > 0)
            {
                if (frame.SmallestFaceWidthPixels is double size &&
                    size < SmallFaceWidthPixels)
                {
                    Add(frame.AbsoluteTimestampMs,
                        VideoFaceSamplingCueKind.PossibleNewOrPoorlyObservedFace);
                }
                else if (frame.CandidatesDiscarded > 0 ||
                    frame.LowestConfidence is double score &&
                    score < LowDetectionConfidence)
                {
                    Add(frame.AbsoluteTimestampMs,
                        VideoFaceSamplingCueKind.UncertainFace);
                }
            }
            else if (frame.CandidatesDiscarded != 0)
            {
                throw new ArgumentException("An empty frame cannot discard detections.");
            }
        }

        if (anchors.Count > VideoFaceAdaptiveSamplingPlanner.MaximumCuesPerWindow)
            throw new InvalidOperationException("Derived cue count exceeded the bounded plan.");

        return anchors.Select(a => new VideoFaceSamplingCue(a.Key, a.Value)).ToArray();
    }
}

/// <summary>
/// Explicit private-only adapter: a one-clip diagnostics JSON, the matching
/// completed adaptive coarse extraction and a CREATE-NEW cue JSON output.
/// It does not open the original video, call models, or consult identity ranks.
/// </summary>
public static class VideoFaceAdaptiveCueCommand
{
    private const int MaximumDiagnosticsBytes = 32 * 1024 * 1024;
    private const int MaximumMetadataBytes = 4 * 1024;
    private const long RecallFlags = 0x00040000 | 0x00400000;
    private const long UnsafeFlags =
        RecallFlags | (long)FileAttributes.Offline | (long)FileAttributes.ReparsePoint;

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            string? manifest = null, report = null, output = null, root = null;
            bool commandSeen = false;
            for (int i = 0; i < args.Length; i++)
            {
                string option = args[i];
                if (option == "--derive-adaptive-cues")
                {
                    if (commandSeen) throw new ArgumentException("Repeated command.");
                    commandSeen = true;
                    continue;
                }
                if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                    throw new ArgumentException("Missing argument value.");

                string value = args[i];
                switch (option)
                {
                    case "--manifest" when manifest is null:
                        manifest = value;
                        break;
                    case "--private-diagnostics" when report is null:
                        report = value;
                        break;
                    case "--private-cues-output" when output is null:
                        output = value;
                        break;
                    case "--root" when root is null:
                        root = value;
                        break;
                    default:
                        throw new ArgumentException("Unknown or repeated argument.");
                }
            }
            if (!commandSeen || manifest is null || report is null || output is null)
                throw new ArgumentException("Explicit private diagnostic inputs and output required.");

            var cues = await DeriveFromPrivateFilesAsync(
                manifest, report, root ?? Directory.GetCurrentDirectory());
            await WriteCuesAsync(output, root ?? Directory.GetCurrentDirectory(),
                Path.GetDirectoryName(Path.GetFullPath(manifest))!, cues);
            // Never print private timestamps, paths, confidence, people or ranks.
            Console.WriteLine("private-adaptive-cues-written: true");
            Console.WriteLine($"cue-count: {cues.Count}");
            return 0;
        }
        catch
        {
            Console.Error.WriteLine(
                "error: private coarse diagnostics were invalid, unsafe or inconsistent.");
            return 1;
        }
    }

    public static async Task<IReadOnlyList<VideoFaceSamplingCue>> DeriveFromPrivateFilesAsync(
        string manifestPath, string reportPath, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        string manifest = RequireLocalFile(manifestPath, VideoFaceFrameSetLoader.MaximumCoverageFrames * 128);
        string report = RequireLocalFile(reportPath, MaximumDiagnosticsBytes);
        if (IsWithin(repositoryRoot, manifest) || IsWithin(repositoryRoot, report))
            throw new ArgumentException("Biometric diagnostics and frame manifests must stay outside the repository.");
        string folder = Path.GetDirectoryName(manifest)!;
        string metadataPath = RequireLocalFile(
            Path.Combine(folder, "extraction-metadata.json"), MaximumMetadataBytes);
        string markerPath = RequireLocalFile(
            Path.Combine(folder, "wi0155-run.json"), MaximumMetadataBytes);

        using JsonDocument marker = JsonDocument.Parse(await File.ReadAllBytesAsync(markerPath));
        JsonElement ownership = marker.RootElement;
        if (ownership.GetProperty("schemaVersion").GetInt32() != 1 ||
            ownership.GetProperty("kind").GetString() != "PhotoIdentity.WI0155.CoverageWorkspace" ||
            ownership.GetProperty("state").GetString() != "extracted")
            throw new ArgumentException("Coarse source must be a completed owned workspace.");

        using JsonDocument metadata = JsonDocument.Parse(
            await File.ReadAllBytesAsync(metadataPath));
        JsonElement provenance = metadata.RootElement;
        if (provenance.GetProperty("schemaVersion").GetInt32() != 1 ||
            provenance.GetProperty("samplingMode").GetString() != "Adaptive")
            throw new ArgumentException("Diagnostics need an adaptive coarse extraction.");

        long duration = provenance.GetProperty("samplingClipDurationMs").GetInt64();
        long start = provenance.GetProperty("samplingWindowStartMs").GetInt64();
        long end = provenance.GetProperty("samplingWindowEndMs").GetInt64();
        VideoFaceSamplingPlan coarse = VideoFaceAdaptiveSamplingPlanner.PlanCoverage(duration, start);
        if (end != coarse.WindowEndMs ||
            provenance.GetProperty("samplingCoarseFrameCount").GetInt32() !=
                coarse.CoarseFrameCount ||
            provenance.GetProperty("samplingRefinementBudgetReached").GetBoolean() ||
            provenance.GetProperty("samplingHasMoreWindows").GetBoolean() != coarse.HasMoreWindows)
            throw new ArgumentException("Extraction provenance does not match the coarse plan.");

        VideoFaceFrameSet frames = await VideoFaceFrameSetLoader.LoadAsync(
            manifest, coverage: true);
        if (frames.Frames!.Count != coarse.CoarseFrameCount)
            throw new ArgumentException("Only complete coarse-only frame runs may produce cues.");

        // Require the exact window-relative manifest timestamps from the
        // trusted planner. Fixed/partial/refined/other-window manifests fail.
        for (int i = 0; i < frames.Frames.Count; i++)
        {
            if (frames.Frames[i].TimestampMs != coarse.FrameTimestampsMs[i] - start)
                throw new ArgumentException("Frame manifest does not match the coarse window.");
            // A complete coarse run must still own its actual local frame
            // inputs. Do not follow online-only, offline or linked files.
            string frameFile = VideoFaceFrameSetLoader.ResolveApprovedFrame(
                folder, frames.Frames[i].File);
            _ = RequireLocalFile(frameFile, VideoFaceFrameSetLoader.MaximumFrameBytes);
        }

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(report));
        JsonElement json = document.RootElement;
        if (json.GetProperty("schemaVersion").GetInt32() != 1 ||
            json.GetProperty("aggregate").GetProperty("framesExamined").GetInt32() !=
                frames.Frames.Count)
            throw new ArgumentException("Incomplete private diagnostic report.");

        // Read frame detections ONLY. Ranked aliases and person evidence in the
        // same private report are never used for adaptive sampling decisions.
        JsonElement detections = json.GetProperty("diagnostics")
            .GetProperty("frameDetections");
        if (detections.ValueKind != JsonValueKind.Array ||
            detections.GetArrayLength() != frames.Frames.Count)
            throw new ArgumentException("Every coarse frame needs a detector observation.");

        var observations = new List<VideoFaceObservedCoarseFrame>(frames.Frames.Count);
        foreach (JsonElement detection in detections.EnumerateArray())
        {
            long relativeTimestamp = detection.GetProperty("timestampMs").GetInt64();
            int count = detection.GetProperty("facesDetected").GetInt32();
            int discarded = OptionalInt(detection, "candidatesDiscarded") ?? 0;
            observations.Add(new VideoFaceObservedCoarseFrame(
                detection.GetProperty("frameNumber").GetInt32(),
                checked(start + relativeTimestamp),
                count,
                OptionalDouble(detection, "lowestConfidence"),
                OptionalDouble(detection, "smallestFaceWidthPixels"),
                discarded));
        }

        return VideoFaceAdaptiveCueDeriver.Derive(coarse, observations);
    }

    private static int? OptionalInt(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value) &&
           value.ValueKind != JsonValueKind.Null ? value.GetInt32() : null;

    private static double? OptionalDouble(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value) &&
           value.ValueKind != JsonValueKind.Null ? value.GetDouble() : null;

    public static async Task WriteCuesAsync(
        string outputPath, string repositoryRoot, string inputFolder,
        IReadOnlyList<VideoFaceSamplingCue> cues)
    {
        ArgumentNullException.ThrowIfNull(cues);
        if (cues.Count > VideoFaceAdaptiveSamplingPlanner.MaximumCuesPerWindow ||
            cues.Any(c => c is null || !Enum.IsDefined(c.Kind) || c.ObservedFrameTimestampMs < 0))
            throw new ArgumentException("Invalid private sampling cue output.");
        if (!Path.IsPathFullyQualified(outputPath) ||
            !string.Equals(Path.GetExtension(outputPath), ".json",
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only a new absolute local JSON output is supported.");

        string target = Path.GetFullPath(outputPath);
        string parent = Path.GetDirectoryName(target)!;
        AssertLocalDirectory(parent);
        if (IsWithin(repositoryRoot, target) ||
            IsWithin(inputFolder, target) ||
            File.Exists(target) || Directory.Exists(target))
            throw new ArgumentException("Private output must be a new file outside the repository and frame workspace.");

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            cues = cues.Select(c => new
            {
                observedFrameTimestampMs = c.ObservedFrameTimestampMs,
                kind = c.Kind.ToString(),
            }),
        });
        if (bytes.Length > 16 * 1024)
            throw new ArgumentException("The new private cue JSON exceeds the input budget.");

        bool created = false;
        try
        {
            await using FileStream stream = new(
                target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true);
            created = true;
            await stream.WriteAsync(bytes);
        }
        catch
        {
            if (created)
                File.Delete(target);
            throw;
        }
    }

    private static string RequireLocalFile(string path, long maxBytes)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("An absolute, private local path is required.");
        string full = Path.GetFullPath(path);
        AssertLocalDirectory(Path.GetDirectoryName(full)!);
        FileInfo info = new(full);
        if (!info.Exists || info.Length is < 1 ||
            info.Length > maxBytes ||
            ((long)info.Attributes & UnsafeFlags) != 0)
            throw new ArgumentException("Private input was unavailable, offline, linked or oversized.");
        return full;
    }

    private static void AssertLocalDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        string? current = full;
        while (current is not null)
        {
            DirectoryInfo info = new(current);
            if (!info.Exists || ((long)info.Attributes & UnsafeFlags) != 0)
                throw new ArgumentException("Private directory is unavailable, offline or linked.");
            current = info.Parent?.FullName;
        }
    }

    private static bool IsWithin(string directory, string path)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(directory), path);
        return relative == "." ||
            (!Path.IsPathRooted(relative) && relative != ".." &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
