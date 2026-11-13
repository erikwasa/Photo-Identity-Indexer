using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.YuNet;

namespace PhotoIdentity.VideoFaceFeasibility;

/// <summary>
/// Private detector evidence for one approved, pre-extracted frame window.
/// This report has no references, embeddings, person labels, catalogue ranks or
/// identity assignments. Its shape is compatible with the offline cue deriver.
/// </summary>
public sealed record VideoFaceDetectorOnlyAggregate(
    int FramesExamined,
    int FramesWithFaces,
    int FacesDetected,
    int RawCandidatesDetected,
    int CandidatesDiscarded,
    double ElapsedMilliseconds);

public sealed record VideoFaceDetectorOnlyDiagnostics(
    IReadOnlyList<VideoFaceFrameDetectionDiagnostic> FrameDetections);

public sealed record VideoFaceDetectorOnlyReport(
    int SchemaVersion,
    VideoFaceDetectorOnlyAggregate Aggregate,
    VideoFaceDetectorOnlyDiagnostics Diagnostics);

/// <summary>
/// Runs YuNet only on explicitly selected, already-local frame images.
/// Does not open the source video, SFace, PostgreSQL or reference images.
/// </summary>
public static class VideoFaceDetectorOnlyRunner
{
    private sealed record Options(
        string Manifest, string PrivateReport, string Root, string ModelDirectory,
        double DetectorConfidence, VideoFaceDetectorSelection Selection,
        VideoFaceOverflowPolicy OverflowPolicy);

    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private const long RecallOnOpen = 0x00040000;
    private const long RecallOnDataAccess = 0x00400000;
    private const long UnsafeAttributes =
        (long)(FileAttributes.Offline | FileAttributes.ReparsePoint) |
        RecallOnOpen | RecallOnDataAccess;

    public static async Task<int> RunAsync(string[] args)
    {
        string stage = "input-selection";
        try
        {
            Options options = Parse(args);
            string manifest = RequireLocalFile(options.Manifest, 16 * 1024);
            string inputFolder = Path.GetDirectoryName(manifest)!;
            string report = Path.GetFullPath(options.PrivateReport);
            ValidateOutput(report, options.Root, inputFolder);

            VideoFaceFrameSet frames = await VideoFaceFrameSetLoader.LoadAsync(
                manifest, coverage: true);
            foreach (VideoFaceFrame frame in frames.Frames!)
            {
                string file = VideoFaceFrameSetLoader.ResolveApprovedFrame(inputFolder, frame.File);
                RequireLocalFile(file, VideoFaceFrameSetLoader.MaximumFrameBytes);
            }

            stage = "model-verification";
            ModelManifest detectorManifest = await new ModelManifestLoader().LoadAsync(
                Path.Combine(options.Root, "models", "manifests", "yunet-2023mar-fp32.json"),
                CancellationToken.None);
            string detectorPath = Path.Combine(
                options.ModelDirectory, detectorManifest.FileName);
            await new ModelFileVerifier().VerifyOrThrowAsync(
                detectorPath, detectorManifest);
            using YuNetFaceDetector detector = new(
                detectorManifest, detectorPath,
                options.Selection.CreateOptions(options.DetectorConfidence));

            stage = "clip-analysis";
            VideoFaceDetectorOnlyReport result = await EvaluateAsync(
                frames, inputFolder, new OpenCvImageDecoder(), detector,
                options.OverflowPolicy);

            stage = "report-persistence";
            await WritePrivateReportAsync(
                report, options.Root, inputFolder, result);

            // The private evidence (frames, source paths, bounding boxes,
            // confidence and timing) is never printed to the console.
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = result.SchemaVersion,
                detectorOnly = true,
                framesExamined = result.Aggregate.FramesExamined,
                framesWithFaces = result.Aggregate.FramesWithFaces,
                facesDetected = result.Aggregate.FacesDetected,
                rawCandidatesDetected = result.Aggregate.RawCandidatesDetected,
                candidatesDiscarded = result.Aggregate.CandidatesDiscarded,
                elapsedMilliseconds = result.Aggregate.ElapsedMilliseconds,
                privateDiagnosticsWritten = true,
            }, ReportOptions));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(VideoFaceFailureDiagnostics.Describe(ex, stage));
            return 1;
        }
    }

    /// <summary>
    /// Injected detector/decoder permit source-free tests without ONNX, DB or video.
    /// The full manifest is preflighted before the first image is decoded.
    /// </summary>
    public static async Task<VideoFaceDetectorOnlyReport> EvaluateAsync(
        VideoFaceFrameSet frames, string manifestDirectory,
        IImageDecoder decoder, IFaceDetector detector,
        VideoFaceOverflowPolicy overflowPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(detector);
        VideoFaceFrameSetLoader.Validate(frames, coverage: true);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestDirectory);

        string[] paths = frames.Frames!.Select(frame =>
            VideoFaceFrameSetLoader.ResolveApprovedFrame(manifestDirectory, frame.File)).ToArray();
        foreach (string path in paths)
            RequireLocalFile(path, VideoFaceFrameSetLoader.MaximumFrameBytes);

        ImageSize? expected = VideoFaceExtractionFidelity.ExpectedNativeDimensions(
            manifestDirectory);
        var observations = new List<VideoFaceFrameDetectionDiagnostic>(paths.Length);
        int framesWithFaces = 0;
        int candidates = 0;
        int discarded = 0;
        int rawCandidates = 0;
        long started = Stopwatch.GetTimestamp();

        for (int i = 0; i < paths.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = await VideoFaceExperiment.ReadApprovedStillAsync(
                paths[i], cancellationToken);
            byte[] sourceHash = SHA256.HashData(bytes);
            using MemoryStream stream = new(bytes, writable: false);
            ImageFrame image = await decoder.DecodeAsync(
                stream, new DecodeOptions(new ImageSize(3840, 3840)),
                cancellationToken);
            VideoFaceExtractionFidelity.VerifyDecodedSize(image.Size, expected);

            IReadOnlyList<DetectedFaceCandidate> raw =
                await detector.DetectAsync(image, cancellationToken);
            VideoFaceCandidateSelection selected =
                VideoFaceCandidateLimiter.Apply(raw, overflowPolicy);

            rawCandidates += selected.RawCandidateCount;
            discarded += selected.DiscardedCandidates;
            candidates += selected.Candidates.Count;
            if (selected.Candidates.Count > 0) framesWithFaces++;

            observations.Add(VideoFaceSensitivityDiagnostics.DescribeFrame(
                i + 1, frames.Frames[i].TimestampMs, selected.Candidates, image.Size)
                with
                {
                    RawCandidatesDetected = selected.RawCandidateCount,
                    CandidatesDiscarded = selected.DiscardedCandidates,
                });

            byte[] after = await File.ReadAllBytesAsync(paths[i], cancellationToken);
            if (!SHA256.HashData(after).AsSpan().SequenceEqual(sourceHash))
                throw new InvalidOperationException(
                    "An approved frame changed while detector diagnostics were running.");
        }

        return new VideoFaceDetectorOnlyReport(
            1,
            new VideoFaceDetectorOnlyAggregate(
                paths.Length, framesWithFaces, candidates, rawCandidates, discarded,
                Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3)),
            new VideoFaceDetectorOnlyDiagnostics(observations));
    }

    public static async Task WritePrivateReportAsync(
        string outputPath, string repositoryRoot, string inputFolder,
        VideoFaceDetectorOnlyReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateOutput(outputPath, repositoryRoot, inputFolder);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(report, ReportOptions);
        bool created = false;
        try
        {
            await using (FileStream stream = new(
                outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 16 * 1024, useAsync: true))
            {
                created = true;
                await stream.WriteAsync(content, cancellationToken);
            }
        }
        catch
        {
            if (created) File.Delete(outputPath);
            throw;
        }
    }

    private static Options Parse(string[] args)
    {
        string? manifest = null, report = null, root = null, models = null;
        string? confidence = null, pipeline = null, tileSize = null, overflow = null;
        bool selected = false;
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i];
            if (option == "--detector-only")
            {
                if (selected) throw new ArgumentException("Duplicate detector-only command.");
                selected = true;
                continue;
            }
            if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]))
                throw new ArgumentException("Missing or invalid detector-only option value.");
            string value = args[i];
            switch (option)
            {
                case "--manifest" when manifest is null: manifest = value; break;
                case "--private-report" when report is null: report = value; break;
                case "--root" when root is null: root = value; break;
                case "--model-dir" when models is null: models = value; break;
                case "--detector-confidence" when confidence is null: confidence = value; break;
                case "--detector-pipeline" when pipeline is null: pipeline = value; break;
                case "--tile-size" when tileSize is null: tileSize = value; break;
                case "--face-overflow-policy" when overflow is null: overflow = value; break;
                default: throw new ArgumentException("Unknown or duplicate detector-only option.");
            }
        }

        if (!selected || manifest is null || report is null ||
            !Path.IsPathFullyQualified(manifest) ||
            !Path.IsPathFullyQualified(report))
            throw new ArgumentException("Detector-only mode requires an absolute manifest and new report.");

        double threshold = confidence switch
        {
            null or "0.7" => 0.7,
            "0.5" => 0.5,
            "0.9" => 0.9,
            _ => throw new ArgumentException("Detector confidence must be 0.5, 0.7 or 0.9."),
        };
        VideoFaceDetectorSelection detection = VideoFaceDetectorSelection.Parse(
            pipeline, tileSize);
        VideoFaceOverflowPolicy overflowPolicy = overflow switch
        {
            null or "fail" => VideoFaceOverflowPolicy.Fail,
            "retain-top-32" => VideoFaceOverflowPolicy.RetainTop32,
            _ => throw new ArgumentException("Detector overflow policy must be fail or retain-top-32."),
        };
        if (overflowPolicy == VideoFaceOverflowPolicy.RetainTop32 &&
            detection.PipelineMode != YuNetDetectorPipelineMode.MultiScale)
            throw new ArgumentException("Retain-top-32 requires explicit multiscale detection.");

        string repositoryRoot = Path.GetFullPath(root ?? Directory.GetCurrentDirectory());
        string modelDirectory = Path.GetFullPath(models ??
            Path.Combine(repositoryRoot, "models", "files"));
        return new Options(manifest, report, repositoryRoot, modelDirectory, threshold,
            detection, overflowPolicy);
    }

    private static void ValidateOutput(
        string outputPath, string repositoryRoot, string inputFolder)
    {
        if (!Path.IsPathFullyQualified(outputPath) ||
            !Path.GetExtension(outputPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Private diagnostic report requires an absolute JSON path.");

        string path = Path.GetFullPath(outputPath);
        string parent = Path.GetDirectoryName(path)!;
        if (IsWithin(repositoryRoot, path) || IsWithin(inputFolder, path) ||
            File.Exists(path) || Directory.Exists(path) ||
            !Directory.Exists(parent) ||
            ((long)File.GetAttributes(parent) & UnsafeAttributes) != 0)
            throw new ArgumentException(
                "Private detector diagnostics must be create-new outside the repository and frames.");
    }

    private static string RequireLocalFile(string path, long maxBytes)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("An absolute private local input file is required.");
        string full = Path.GetFullPath(path);
        FileInfo info = new(full);
        if (!info.Exists || info.Length is < 1 || info.Length > maxBytes ||
            ((long)info.Attributes & UnsafeAttributes) != 0 ||
            ((long)File.GetAttributes(Path.GetDirectoryName(full)!) & UnsafeAttributes) != 0)
            throw new ArgumentException("Approved private detector input is missing, linked, online-only or oversized.");

        return full;
    }

    private static bool IsWithin(string parent, string path)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(parent), Path.GetFullPath(path));
        return relative == "." ||
            (!Path.IsPathRooted(relative) && relative != ".." &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
