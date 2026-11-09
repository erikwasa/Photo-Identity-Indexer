using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.YuNet;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Explicit, offline re-detection of only operator-selected review frames.
/// Reconstructs the same post-NMS, post-top32 face ordinals as the existing
/// private catalogue report. Does NOT rerun embeddings, query PostgreSQL,
/// assign people, or modify media. All coordinates remain in a new local
/// private file; stdout contains anonymous counts only.
/// </summary>
public static class VideoFaceReviewGeometryRunner
{
    private sealed record ReviewFrame(string Clip, int FrameNumber);
    private sealed record ReviewSelection(int SchemaVersion, List<ReviewFrame>? Frames);
    private sealed record ReviewGeometryFrame(string Clip, int FrameNumber,
        IReadOnlyList<VideoFaceReviewBox> Boxes);
    private sealed record ReviewGeometry(int SchemaVersion,
        IReadOnlyList<ReviewGeometryFrame> Frames);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            await ExecuteAsync(args);
            return 0;
        }
        catch (Exception)
        {
            // Deliberately do not reveal private paths, aliases, frames,
            // model errors, identity evidence or exception details.
            Console.Error.WriteLine(
                "error: private review geometry failed (input, model or detector mismatch); no private frame data was logged.");
            return 1;
        }
    }

    private static async Task ExecuteAsync(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) ||
                args[i] is not ("--review-geometry-selection" or "--catalogue-cases" or
                    "--private-catalogue-report" or "--review-geometry-output" or
                    "--root" or "--model-dir") ||
                !values.TryAdd(args[i], args[i + 1]))
            {
                throw new ArgumentException("Invalid private geometry options.");
            }
        }

        string root = Path.GetFullPath(values.GetValueOrDefault("--root") ??
            Directory.GetCurrentDirectory());
        string modelDir = Path.GetFullPath(values.GetValueOrDefault("--model-dir") ??
            Path.Combine(root, "models", "files"));
        string casesPath = Required(values, "--catalogue-cases");
        string reportPath = RegularPrivate(Required(values, "--private-catalogue-report"),
            64L * 1024 * 1024, root);
        string selectionPath = RegularPrivate(Required(values, "--review-geometry-selection"),
            16 * 1024, root);
        string output = Path.GetFullPath(Required(values, "--review-geometry-output"));

        VideoFaceCatalogueCases cases = await VideoFaceCatalogueCasesLoader.LoadAsync(
            casesPath, root);
        byte[] reportBytes = await File.ReadAllBytesAsync(reportPath);
        VideoFaceCatalogueEvaluationReport report =
            JsonSerializer.Deserialize<VideoFaceCatalogueEvaluationReport>(reportBytes,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            ?? throw new ArgumentException("Invalid private report.");

        ReviewSelection requested = JsonSerializer.Deserialize<ReviewSelection>(
            await File.ReadAllBytesAsync(selectionPath),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            ?? throw new ArgumentException("Invalid private selection.");
        if (requested.SchemaVersion != 1 || requested.Frames is not { Count: >= 1 and <= 128 } ||
            report.SchemaVersion != 1 || report.Clips.Count != cases.Clips!.Count)
        {
            throw new ArgumentException("Unsupported private review scope.");
        }

        string outputDir = Path.GetDirectoryName(output)!;
        DirectoryInfo folder = new(outputDir);
        if (!folder.Exists || (folder.Attributes & FileAttributes.ReparsePoint) != 0 ||
            File.Exists(output) || Path.GetExtension(output) != ".json" ||
            VideoFaceCatalogueCasesLoader.IsWithin(root, output) ||
            VideoFaceCatalogueCasesLoader.IsWithin(
                Path.GetDirectoryName(Path.GetFullPath(casesPath))!, output) ||
            cases.Clips.Any(clip =>
                VideoFaceCatalogueCasesLoader.IsWithin(
                    Path.GetDirectoryName(Path.GetFullPath(clip.Manifest))!, output)))
        {
            throw new ArgumentException("Geometry output requires a new private JSON file.");
        }

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (ReviewFrame item in requested.Frames)
        {
            if (item is null || item.Clip is null ||
                !System.Text.RegularExpressions.Regex.IsMatch(item.Clip, @"^V(?!00)[0-9]{2}$") ||
                item.FrameNumber < 1 || item.FrameNumber > 180 ||
                !unique.Add(item.Clip + ":" + item.FrameNumber) ||
                !cases.Clips.Any(clip => clip.Alias == item.Clip))
            {
                throw new ArgumentException("Invalid private review frame selection.");
            }
        }

        ModelManifestLoader modelLoader = new();
        ModelManifest manifest = await modelLoader.LoadAsync(
            Path.Combine(root, "models", "manifests", "yunet-2023mar-fp32.json"),
            CancellationToken.None);
        string modelFile = Path.Combine(modelDir, manifest.FileName);
        await new ModelFileVerifier().VerifyOrThrowAsync(modelFile, manifest);

        OpenCvImageDecoder decoder = new();
        Dictionary<string, (VideoFaceFrameSet Set, string Folder,
            VideoFaceCatalogueClipReview Review, ImageSize? Expected)> approved = new(
                StringComparer.Ordinal);
        foreach (VideoFaceCatalogueCase clip in cases.Clips)
        {
            VideoFaceCatalogueClipReview result = report.Clips.Single(r => r.Alias == clip.Alias);
            VideoFaceFrameSet set = await VideoFaceFrameSetLoader.LoadAsync(
                clip.Manifest, coverage: true);
            if (result.Review.Diagnostics is null ||
                result.Review.Aggregate.FramesExamined != set.Frames!.Count ||
                result.Review.Diagnostics.FrameDetections.Count != set.Frames.Count ||
                result.Review.Diagnostics.DetectorProfile?.PipelineMode != "multi-scale" ||
                result.Review.Diagnostics.DetectorProfile?.TileSize is not (640 or 1024) ||
                result.Review.Diagnostics.RequestedDetectorConfidenceThreshold is not (0.5 or 0.7 or 0.9) ||
                result.Review.DetectorSha256 != manifest.Sha256)
            {
                throw new ArgumentException("Private report cannot be reproduced.");
            }
            string dir = Path.GetDirectoryName(Path.GetFullPath(clip.Manifest))!;
            approved.Add(clip.Alias, (set, dir, result,
                VideoFaceExtractionFidelity.ExpectedNativeDimensions(dir)));
        }

        // A separate detector per experimental configuration, with the same
        // model revision, threshold, tile size, overlap and NMS as the report.
        var detectors = new Dictionary<string, YuNetFaceDetector>(StringComparer.Ordinal);
        List<ReviewGeometryFrame> outputFrames = [];
        try
        {
            foreach (ReviewFrame selectedFrame in requested.Frames)
            {
                var clip = approved[selectedFrame.Clip];
                VideoFaceFrame frame = clip.Set.Frames![selectedFrame.FrameNumber - 1];
                string path = VideoFaceFrameSetLoader.ResolveApprovedFrame(
                    clip.Folder, frame.File);
                FileInfo imageFile = new(path);
                if (imageFile.Length is < 1 or > VideoFaceFrameSetLoader.MaximumFrameBytes ||
                    (imageFile.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                {
                    throw new ArgumentException("Selected private frame is not approved.");
                }

                VideoFaceSensitivityDiagnostics diagnostics = clip.Review.Diagnostics!;
                VideoFaceFrameDetectionDiagnostic expected =
                    diagnostics.FrameDetections[selectedFrame.FrameNumber - 1];
                if (expected.FrameNumber != selectedFrame.FrameNumber ||
                    expected.TimestampMs != frame.TimestampMs)
                {
                    throw new ArgumentException("Review frame ordinal or timestamp mismatch.");
                }

                int tile = diagnostics.DetectorProfile!.TileSize!.Value;
                double threshold = diagnostics.RequestedDetectorConfidenceThreshold;
                string key = tile + ":" + threshold.ToString("R",
                    System.Globalization.CultureInfo.InvariantCulture);
                if (!detectors.TryGetValue(key, out YuNetFaceDetector? detector))
                {
                    detector = new YuNetFaceDetector(manifest, modelFile,
                        new VideoFaceDetectorSelection(
                            YuNetDetectorPipelineMode.MultiScale, tile).CreateOptions(threshold));
                    detectors.Add(key, detector);
                }

                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
                ImageFrame decoded = await decoder.DecodeAsync(stream,
                    new DecodeOptions(new ImageSize(3840, 3840)), CancellationToken.None);
                VideoFaceExtractionFidelity.VerifyDecodedSize(decoded.Size, clip.Expected);
                IReadOnlyList<PhotoIdentity.Core.Recognition.DetectedFaceCandidate> raw =
                    await detector.DetectAsync(decoded, CancellationToken.None);
                IReadOnlyList<VideoFaceReviewBox> boxes =
                    VideoFaceReviewGeometryConsistency.Reconstruct(raw, expected);
                outputFrames.Add(new ReviewGeometryFrame(
                    selectedFrame.Clip, selectedFrame.FrameNumber, boxes));
            }
        }
        finally
        {
            foreach (YuNetFaceDetector detector in detectors.Values)
            {
                detector.Dispose();
            }
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new ReviewGeometry(1, outputFrames),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });
        await using (FileStream stream = new(output, FileMode.CreateNew,
            FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(payload);
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            reviewedFrames = outputFrames.Count,
            reconstructedCandidates = outputFrames.Sum(f => f.Boxes.Count),
            privateGeometryWritten = true,
        }));
    }

    private static string Required(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) &&
        Path.IsPathFullyQualified(value)
            ? value : throw new ArgumentException("Missing private review option.");

    private static string RegularPrivate(string path, long maximumBytes, string root)
    {
        string full = Path.GetFullPath(path);
        FileInfo file = new(full);
        const FileAttributes blocked = FileAttributes.ReparsePoint | FileAttributes.Offline;
        if (!file.Exists || file.Length is < 1 || file.Length > maximumBytes ||
            (file.Attributes & blocked) != 0 ||
            VideoFaceCatalogueCasesLoader.IsWithin(root, full))
        {
            throw new ArgumentException("Expected a bounded private input outside Git.");
        }
        return full;
    }
}
