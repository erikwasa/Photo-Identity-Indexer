using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// A deliberately offline contract: one operator-selected clip, at most twelve already-extracted
/// JPEG/PNG frames. No video, original, database or network access is implemented here.
/// </summary>
public sealed record VideoFaceFrame(long TimestampMs, string File);
public sealed record VideoFaceFrameSet(int SchemaVersion, List<VideoFaceFrame>? Frames);
public sealed record VideoFaceAggregate(
    int SchemaVersion,
    int FramesExamined,
    int FramesWithFaces,
    int FacesDetected,
    int EmbeddingsCalculated,
    int AdjacentPossibleRepeatPairs,
    double ElapsedMilliseconds);

public static class VideoFaceFrameSetLoader
{
    public const int MaximumFrames = 12;
    public const int MaximumCoverageFrames = 180;
    public const long MaximumTimestampMs = 600_000;
    public const int MaximumFrameBytes = 8 * 1024 * 1024;
    private const int MaximumManifestBytes = 16 * 1024;
    private static readonly JsonSerializerOptions InputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task<VideoFaceFrameSet> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken = default,
        bool coverage = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        string path = Path.GetFullPath(manifestPath);
        FileInfo manifestInfo = new(path);
        if (!manifestInfo.Exists || manifestInfo.Length is < 1 or > MaximumManifestBytes ||
            (manifestInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException("The private frame manifest must be a regular JSON file of at most 16 KiB.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length is < 1 or > MaximumManifestBytes)
        {
            throw new ArgumentException("The frame manifest exceeds its size limit.");
        }

        VideoFaceFrameSet set = JsonSerializer.Deserialize<VideoFaceFrameSet>(bytes, InputOptions)
            ?? throw new ArgumentException("The frame manifest is empty.");
        Validate(set, coverage);
        return set;
    }

    public static void Validate(VideoFaceFrameSet set, bool coverage = false)
    {
        ArgumentNullException.ThrowIfNull(set);
        int maximum = coverage ? MaximumCoverageFrames : MaximumFrames;
        if (set.SchemaVersion != 1 || set.Frames is null ||
            set.Frames.Count < 1 || set.Frames.Count > maximum)
        {
            throw new ArgumentException(
                $"Expected schemaVersion 1 and between 1 and {maximum} selected frames.");
        }

        long previous = -1;
        HashSet<string> usedFiles = new(StringComparer.OrdinalIgnoreCase);
        foreach (VideoFaceFrame? frame in set.Frames)
        {
            if (frame is null || frame.TimestampMs <= previous ||
                frame.TimestampMs > MaximumTimestampMs)
            {
                throw new ArgumentException("Frame timestamps must be strictly increasing, nonnegative and at most ten minutes.");
            }

            previous = frame.TimestampMs;
            string? name = frame.File;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100 ||
                !char.IsAsciiLetterOrDigit(name[0]) ||
                name.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                    character is not ('.' or '_' or '-')) ||
                !new[] { ".jpg", ".jpeg", ".png" }.Contains(
                    Path.GetExtension(name), StringComparer.OrdinalIgnoreCase) ||
                !usedFiles.Add(name))
            {
                throw new ArgumentException("Frame filenames must be unique, simple JPEG/PNG names without directory components.");
            }
        }
    }

    public static string ResolveApprovedFrame(string manifestDirectory, string filename)
    {
        // Validate the filename again at the file-open boundary, even for programmatic callers.
        Validate(new VideoFaceFrameSet(1, [new VideoFaceFrame(0, filename)]));
        string directory = Path.GetFullPath(manifestDirectory);
        if (!Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException("The manifest folder must be a regular local folder.");
        }

        string file = Path.Combine(directory, filename);
        FileInfo info = new(file);
        if (!info.Exists || info.Length is < 1 or > MaximumFrameBytes ||
            (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException("An approved frame is missing, linked or exceeds the 8 MiB limit.");
        }

        return file;
    }
}

/// <summary>
/// Runs the existing production model interfaces on pre-extracted stills only.
/// Embeddings remain in memory; no crops, paths, coordinates, vectors or identities are emitted.
/// An adjacent-repeat count is exploratory similarity evidence, never an identity assignment.
/// </summary>
public sealed class VideoFaceExperiment
{
    public const double ExploratorySimilarityThreshold = 0.80;
    public const long MaximumAdjacentGapMs = 5_000;
    private readonly IImageDecoder _decoder;
    private readonly IFaceDetector _detector;
    private readonly IFaceAligner _aligner;
    private readonly IFaceEmbedder _embedder;
    private readonly VideoFaceDetectorProfile? _detectorProfile;
    private readonly VideoFaceOverflowPolicy _overflowPolicy;

    public VideoFaceExperiment(
        IImageDecoder decoder,
        IFaceDetector detector,
        IFaceAligner aligner,
        IFaceEmbedder embedder,
        VideoFaceDetectorProfile? detectorProfile = null,
        VideoFaceOverflowPolicy overflowPolicy = VideoFaceOverflowPolicy.Fail)
    {
        if (!Enum.IsDefined(overflowPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(overflowPolicy));
        }
        _decoder = decoder;
        _detector = detector;
        _aligner = aligner;
        _embedder = embedder;
        _detectorProfile = detectorProfile;
        _overflowPolicy = overflowPolicy;
    }

    public async Task<VideoFaceAggregate> RunAsync(
        VideoFaceFrameSet set,
        string manifestDirectory,
        CancellationToken cancellationToken = default)
    {
        VideoFacePrivateReview result = await RunCoreAsync(
            set, manifestDirectory, matcher: null, diagnostics: false,
            coverage: false, requestedDetectorConfidence: 0.9,
            cancellationToken: cancellationToken);
        return result.Aggregate;
    }

    public async Task<VideoFacePrivateReview> RunWithReferencesAsync(
        VideoFaceFrameSet set,
        string manifestDirectory,
        VideoFaceReferenceSet referenceSet,
        string referenceDirectory,
        bool diagnostics = false,
        double requestedDetectorConfidence = 0.9,
        CancellationToken cancellationToken = default,
        bool coverage = false)
    {
        if (coverage && !diagnostics)
        {
            throw new ArgumentException("Coverage requires explicit private diagnostics.");
        }

        if (diagnostics)
        {
            if (requestedDetectorConfidence is not (0.5 or 0.7 or 0.9))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedDetectorConfidence),
                    "Diagnostics permit detector confidence 0.5, 0.7 or 0.9 only.");
            }
        }
        else if (requestedDetectorConfidence != 0.9)
        {
            throw new ArgumentException(
                "Non-default detector confidence requires explicitly enabled diagnostics.");
        }

        long fullRunStarted = Stopwatch.GetTimestamp();
        VideoFaceFrameSetLoader.Validate(set, coverage);
        VideoFaceReferenceSetLoader.Validate(referenceSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceDirectory);

        string[] selectedFrames = set.Frames!.Select(frame =>
            VideoFaceFrameSetLoader.ResolveApprovedFrame(manifestDirectory, frame.File)).ToArray();
        string[] referenceFiles = referenceSet.References!.Select(reference =>
            VideoFaceFrameSetLoader.ResolveApprovedFrame(referenceDirectory, reference.File)).ToArray();

        // A renamed copy of a selected frame is not an independent known-person reference.
        // Reject identical file contents before any model work, without revealing file names.
        HashSet<string> frameDigests = [];
        foreach (string path in selectedFrames)
        {
            byte[] bytes = await ReadApprovedStillAsync(path, cancellationToken);
            frameDigests.Add(Convert.ToHexString(SHA256.HashData(bytes)));
        }

        List<VideoFaceReferenceEmbedding> prepared = [];
        HashSet<string> referenceDigests = [];
        for (int i = 0; i < referenceFiles.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string referencePath = referenceFiles[i];
            byte[] bytes = await ReadApprovedStillAsync(referencePath, cancellationToken);
            if (frameDigests.Contains(Convert.ToHexString(SHA256.HashData(bytes))))
            {
                throw new ArgumentException("Reference images must be distinct from selected video frames.");
            }

            byte[] originalHash = SHA256.HashData(bytes);
            if (!referenceDigests.Add(Convert.ToHexString(originalHash)))
            {
                throw new ArgumentException("Approved references must be distinct photos.");
            }

            using MemoryStream stream = new(bytes, writable: false);
            ImageFrame image = await _decoder.DecodeAsync(
                stream, new DecodeOptions(new ImageSize(1600, 1600)), cancellationToken);
            IReadOnlyList<DetectedFaceCandidate> detected = await _detector.DetectAsync(
                image, cancellationToken);
            if (detected.Count != 1)
            {
                throw new ArgumentException(
                    "Each approved reference still must contain exactly one detected face.");
            }

            var protocol = _embedder.Descriptor.AlignmentProtocol
                ?? throw new InvalidOperationException("The embedding model has no alignment protocol.");
            AlignedFace aligned = await _aligner.AlignAsync(
                image, detected[0], protocol, cancellationToken);
            EmbeddingVector embedding = await _embedder.EmbedAsync(aligned, cancellationToken);
            byte[] after = await File.ReadAllBytesAsync(referencePath, cancellationToken);
            if (!SHA256.HashData(after).AsSpan().SequenceEqual(originalHash))
            {
                throw new InvalidOperationException("A reference photo changed during the experiment.");
            }

            prepared.Add(new VideoFaceReferenceEmbedding(
                referenceSet.References![i].Alias, embedding));
        }

        // Include time spent preparing the reference models in the overall measured runtime.
        // No original, catalogue, identity data or embeddings are written by the experiment.
        VideoFacePrivateReview review = await RunCoreAsync(
            set, manifestDirectory, new VideoFaceReferenceMatcher(prepared),
            diagnostics, coverage, requestedDetectorConfidence, cancellationToken);
        return review with
        {
            Aggregate = review.Aggregate with
            {
                ElapsedMilliseconds = Math.Round(
                    Stopwatch.GetElapsedTime(fullRunStarted).TotalMilliseconds, 3),
            },
        };
    }

    /// <summary>
    /// Catalogue vectors are loaded by the separate read-only adapter before inference.
    /// This method never opens the database, reads canonical IDs or writes observations.
    /// </summary>
    public async Task<VideoFacePrivateReview> RunWithPreparedMatcherAsync(
        VideoFaceFrameSet set,
        string manifestDirectory,
        IVideoFaceCandidateMatcher matcher,
        double detectorConfidence,
        CancellationToken cancellationToken = default,
        bool provisionalGroups = false,
        bool unassignedAudit = false)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        if (detectorConfidence is not (0.5 or 0.7 or 0.9))
        {
            throw new ArgumentOutOfRangeException(nameof(detectorConfidence));
        }

        if (unassignedAudit && !provisionalGroups)
        {
            throw new ArgumentException("Unassigned audit requires explicit provisional grouping.");
        }

        // Include up to 180 pre-extracted local frames, without requiring
        // heuristic groups/tracks to identify a person.
        return await RunCoreAsync(set, manifestDirectory, matcher,
            diagnostics: true, coverage: true,
            requestedDetectorConfidence: detectorConfidence,
            cancellationToken: cancellationToken,
            provisionalGroups: provisionalGroups,
            unassignedAudit: unassignedAudit);
    }

    private async Task<VideoFacePrivateReview> RunCoreAsync(
        VideoFaceFrameSet set,
        string manifestDirectory,
        IVideoFaceCandidateMatcher? matcher,
        bool diagnostics,
        bool coverage,
        double requestedDetectorConfidence,
        CancellationToken cancellationToken,
        bool provisionalGroups = false,
        bool unassignedAudit = false)
    {
        VideoFaceFrameSetLoader.Validate(set, coverage);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);
        if (!coverage && _overflowPolicy != VideoFaceOverflowPolicy.Fail)
        {
            throw new ArgumentException(
                "Bounded overflow retention requires explicit coverage diagnostics.");
        }
        // Preflight all selected paths before invoking a model.
        string[] files = set.Frames!.Select(frame =>
            VideoFaceFrameSetLoader.ResolveApprovedFrame(manifestDirectory, frame.File)).ToArray();
        // Validate sampler provenance before any inference; manually supplied
        // manifests without extraction metadata remain backwards compatible.
        var expectedNativeSize = coverage
            ? VideoFaceExtractionFidelity.ExpectedNativeDimensions(manifestDirectory)
            : null;

        long started = Stopwatch.GetTimestamp();
        int framesWithFaces = 0;
        int totalFaces = 0;
        int totalEmbeddings = 0;
        int adjacentPairs = 0;
        // Allocated only on an explicit operator-requested catalogue run.
        // No group vectors or cross-frame reference templates are serialized.
        VideoFaceProvisionalGrouper? provisional = provisionalGroups
            ? new VideoFaceProvisionalGrouper(
                captureUnassignedAudit: unassignedAudit,
                firstTimestampMs: set.Frames![0].TimestampMs,
                lastTimestampMs: set.Frames[^1].TimestampMs)
            : null;
        List<VideoFaceReferenceFinding> findings = [];
        List<VideoFaceFrameDetectionDiagnostic> frameDetections = [];
        List<VideoFaceRankedDiagnostic> rankedFaces = [];
        VideoFaceCoverageGrouper? coverageGrouper = coverage && matcher is VideoFaceReferenceMatcher
            ? new VideoFaceCoverageGrouper()
            : null;
        List<VideoFaceCoverageObservation> coverageSamples = [];
        List<VideoFaceSameFramePair> sameFramePairs = [];
        List<EmbeddingVector>? previous = null;
        long previousTimestamp = -1;

        string stage = "frame-read";
        try
        {
            for (int index = 0; index < files.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string file = files[index];
                stage = "frame-read";
                byte[] input = await ReadApprovedStillAsync(file, cancellationToken);

                byte[] before = SHA256.HashData(input);
                using MemoryStream stream = new(input, writable: false);
                stage = "frame-decode";
                ImageFrame image = await _decoder.DecodeAsync(
                    stream,
                    new DecodeOptions(new ImageSize(coverage ? 3840 : 1600, coverage ? 3840 : 1600)),
                    cancellationToken);
                stage = "frame-fidelity";
                VideoFaceExtractionFidelity.VerifyDecodedSize(image.Size, expectedNativeSize);
                stage = "face-detection";
                IReadOnlyList<DetectedFaceCandidate> rawDetections =
                    await _detector.DetectAsync(image, cancellationToken);
                stage = "face-count-limit";
                VideoFaceCandidateSelection selected = VideoFaceCandidateLimiter.Apply(
                    rawDetections, _overflowPolicy);
                IReadOnlyList<DetectedFaceCandidate> detections = selected.Candidates;

                totalFaces += detections.Count;
                if (detections.Count > 0)
                {
                    framesWithFaces++;
                }

                if (diagnostics)
                {
                    frameDetections.Add(VideoFaceSensitivityDiagnostics.DescribeFrame(
                        index + 1, set.Frames![index].TimestampMs, detections, image.Size)
                        with
                        {
                            RawCandidatesDetected = selected.RawCandidateCount,
                            CandidatesDiscarded = selected.DiscardedCandidates,
                        });
                }

                List<EmbeddingVector> current = [];
                stage = "face-alignment";
                foreach (DetectedFaceCandidate face in detections)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var protocol = _embedder.Descriptor.AlignmentProtocol
                        ?? throw new InvalidOperationException("The embedding model has no alignment protocol.");
                    AlignedFace aligned = await _aligner.AlignAsync(
                        image, face, protocol, cancellationToken);
                    stage = "face-embedding";
                    EmbeddingVector embedding = await _embedder.EmbedAsync(
                        aligned, cancellationToken);
                    current.Add(embedding);
                    totalEmbeddings++;
                }

                long timestamp = set.Frames![index].TimestampMs;
                if (coverageGrouper is not null && current.Count > 1)
                {
                    sameFramePairs.AddRange(VideoFaceCoverageSensitivity.DescribeSameFrame(
                        index + 1, detections, current));
                }

                stage = "candidate-ranking";
                if (matcher is not null)
                {
                    for (int faceIndex = 0; faceIndex < current.Count; faceIndex++)
                    {
                        if (provisional is not null)
                        {
                            provisional.Observe(index + 1, timestamp, faceIndex + 1,
                                current[faceIndex], detections[faceIndex], image.Size);
                        }
                        if (diagnostics && matcher is VideoFaceCataloguePersonMatcher catalogue)
                        {
                            // Full-archive ranking only once per face, preserving all
                            // top-three aliases for the private diagnostic report.
                            var result = catalogue.Evaluate(
                                index + 1, timestamp, faceIndex + 1, current[faceIndex]);
                            findings.Add(result.Finding);
                            rankedFaces.Add(result.Diagnostic);
                        }
                        else
                        {
                            findings.Add(matcher.FindCandidate(
                                index + 1, timestamp, faceIndex + 1, current[faceIndex]));
                            if (diagnostics)
                            {
                                rankedFaces.Add(matcher.Diagnose(
                                    index + 1, timestamp, faceIndex + 1, current[faceIndex]));
                            }
                        }
                        if (coverageGrouper is not null)
                        {
                            if (coverageSamples.Count >= 256)
                            {
                                throw new InvalidOperationException(
                                    "Private grouping sensitivity is limited to 256 face observations.");
                            }

                            coverageSamples.Add(new VideoFaceCoverageObservation(
                                index + 1, timestamp, faceIndex + 1, current[faceIndex]));
                            coverageGrouper.Observe(
                                index + 1, timestamp, faceIndex + 1, current[faceIndex]);
                        }
                    }
                }

                if (previous is not null &&
                    timestamp - previousTimestamp <= MaximumAdjacentGapMs)
                {
                    adjacentPairs += CountPossibleRepeatPairs(previous, current);
                }

                // Verify the selected file was not modified during the model run.
                stage = "frame-integrity";
                byte[] after = await File.ReadAllBytesAsync(file, cancellationToken);
                if (!SHA256.HashData(after).AsSpan().SequenceEqual(before))
                {
                    throw new InvalidOperationException("A frame was modified during the experiment.");
                }

                previous = current;
                previousTimestamp = timestamp;
            }

        }
        catch (Exception exception)
        {
            // Preserve the exception type and stack for existing tests, but
            // disclose only a fixed, non-identifying stage at the CLI boundary.
            VideoFaceFailureDiagnostics.Mark(exception, stage);
            throw;
        }

        VideoFaceAggregate aggregate = new(
            1,
            files.Length,
            framesWithFaces,
            totalFaces,
            totalEmbeddings,
            adjacentPairs,
            Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3));
        return new VideoFacePrivateReview(
            1,
            matcher?.ReferenceCount ?? 0,
            _detector.Descriptor.Id.Value,
            _detector.Descriptor.ModelHash.Value,
            _embedder.Descriptor.Id.Value,
            _embedder.Descriptor.ModelHash.Value,
            aggregate,
            findings)
        {
            Diagnostics = diagnostics
                ? new VideoFaceSensitivityDiagnostics(
                    requestedDetectorConfidence, frameDetections, rankedFaces)
                {
                    DetectorProfile = _detectorProfile,
                    ProvisionalGroups = provisional?.CreateReview(),
                    GroupSuggestions = provisional is not null &&
                        matcher is VideoFaceCataloguePersonMatcher catalogueMatcher
                        ? provisional.RankGroups(catalogueMatcher)
                        : null,
                    Coverage = coverageGrouper is null ? null : new VideoFaceCoverageReview(
                        coverageGrouper.Groups.Count,
                        coverageGrouper.Groups,
                        coverageGrouper.Assignments)
                    {
                        GroupingComparisons = VideoFaceCoverageSensitivity.Compare(
                            coverageSamples),
                        SameFramePairs = sameFramePairs,
                    },
                }
                : null,
        };
    }

    internal static async Task<byte[]> ReadApprovedStillAsync(
        string file,
        CancellationToken cancellationToken)
    {
        byte[] input = await File.ReadAllBytesAsync(file, cancellationToken);
        if (input.Length is < 1 or > VideoFaceFrameSetLoader.MaximumFrameBytes)
        {
            throw new InvalidOperationException("Selected still contents changed after preflight.");
        }

        string extension = Path.GetExtension(file);
        bool jpeg = input.Length >= 3 &&
            input[0] == 0xFF && input[1] == 0xD8 && input[2] == 0xFF;
        bool png = input.Length >= 8 &&
            input.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (!(jpeg && (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))) &&
            !(png && extension.Equals(".png", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Selected still signature does not match its JPEG/PNG extension.");
        }

        return input;
    }

    public static int CountPossibleRepeatPairs(
        IReadOnlyList<EmbeddingVector> preceding,
        IReadOnlyList<EmbeddingVector> following)
    {
        ArgumentNullException.ThrowIfNull(preceding);
        ArgumentNullException.ThrowIfNull(following);
        // Greedy, one-to-one pairs. Not tracking, not a calibrated identity threshold.
        HashSet<int> used = [];
        int count = 0;
        foreach (EmbeddingVector candidate in following)
        {
            int bestIndex = -1;
            double bestScore = ExploratorySimilarityThreshold;
            for (int index = 0; index < preceding.Count; index++)
            {
                if (used.Contains(index))
                {
                    continue;
                }

                double similarity = candidate.CosineSimilarity(preceding[index]);
                if (similarity >= bestScore)
                {
                    bestScore = similarity;
                    bestIndex = index;
                }
            }

            if (bestIndex >= 0)
            {
                used.Add(bestIndex);
                count++;
            }
        }

        return count;
    }
}
