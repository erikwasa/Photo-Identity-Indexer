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
    public const long MaximumTimestampMs = 600_000;
    public const long MaximumFrameBytes = 8 * 1024 * 1024;
    private const long MaximumManifestBytes = 16 * 1024;
    private static readonly JsonSerializerOptions InputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static async Task<VideoFaceFrameSet> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
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
        Validate(set);
        return set;
    }

    public static void Validate(VideoFaceFrameSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.SchemaVersion != 1 || set.Frames is not { Count: >= 1 and <= MaximumFrames })
        {
            throw new ArgumentException("Expected schemaVersion 1 and between 1 and 12 selected frames.");
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

    public VideoFaceExperiment(
        IImageDecoder decoder,
        IFaceDetector detector,
        IFaceAligner aligner,
        IFaceEmbedder embedder)
    {
        _decoder = decoder;
        _detector = detector;
        _aligner = aligner;
        _embedder = embedder;
    }

    public async Task<VideoFaceAggregate> RunAsync(
        VideoFaceFrameSet set,
        string manifestDirectory,
        CancellationToken cancellationToken = default)
    {
        VideoFaceFrameSetLoader.Validate(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);
        // Preflight all selected paths before invoking a model.
        string[] files = set.Frames!.Select(frame =>
            VideoFaceFrameSetLoader.ResolveApprovedFrame(manifestDirectory, frame.File)).ToArray();

        long started = Stopwatch.GetTimestamp();
        int framesWithFaces = 0;
        int totalFaces = 0;
        int totalEmbeddings = 0;
        int adjacentPairs = 0;
        List<EmbeddingVector>? previous = null;
        long previousTimestamp = -1;

        for (int index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = files[index];
            byte[] input = await File.ReadAllBytesAsync(file, cancellationToken);
            if (input.Length is < 1 or > VideoFaceFrameSetLoader.MaximumFrameBytes)
            {
                throw new InvalidOperationException("Frame contents changed after preflight.");
            }

            byte[] before = SHA256.HashData(input);
            using MemoryStream stream = new(input, writable: false);
            ImageFrame image = await _decoder.DecodeAsync(
                stream,
                new DecodeOptions(new ImageSize(1600, 1600)),
                cancellationToken);
            IReadOnlyList<DetectedFaceCandidate> detections =
                await _detector.DetectAsync(image, cancellationToken);
            totalFaces += detections.Count;
            if (detections.Count > 0)
            {
                framesWithFaces++;
            }

            List<EmbeddingVector> current = [];
            foreach (DetectedFaceCandidate face in detections)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var protocol = _embedder.Descriptor.AlignmentProtocol
                    ?? throw new InvalidOperationException("The embedding model has no alignment protocol.");
                AlignedFace aligned = await _aligner.AlignAsync(
                    image, face, protocol, cancellationToken);
                EmbeddingVector embedding = await _embedder.EmbedAsync(
                    aligned, cancellationToken);
                current.Add(embedding);
                totalEmbeddings++;
            }

            long timestamp = set.Frames[index].TimestampMs;
            if (previous is not null &&
                timestamp - previousTimestamp <= MaximumAdjacentGapMs)
            {
                adjacentPairs += CountPossibleRepeatPairs(previous, current);
            }

            // Verify the selected file was not modified during the model run.
            byte[] after = await File.ReadAllBytesAsync(file, cancellationToken);
            if (!SHA256.HashData(after).AsSpan().SequenceEqual(before))
            {
                throw new InvalidOperationException("A frame was modified during the experiment.");
            }

            previous = current;
            previousTimestamp = timestamp;
        }

        return new VideoFaceAggregate(
            1,
            files.Length,
            framesWithFaces,
            totalFaces,
            totalEmbeddings,
            adjacentPairs,
            Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3));
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
