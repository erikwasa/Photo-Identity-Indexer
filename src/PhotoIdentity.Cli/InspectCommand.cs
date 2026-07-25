using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.SFace;
using PhotoIdentity.Recognition.Onnx.YuNet;

namespace PhotoIdentity.Cli;

internal static class InspectCommandRunner
{
    private const string DetectorManifestFile = "yunet-2023mar-fp32.json";
    private const string EmbedderManifestFile = "sface-2021dec-fp32.json";

    public static async Task<int> RunAsync(
        InspectCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string inputPath = Path.GetFullPath(options.InputPath);
        if (!File.Exists(inputPath))
        {
            error.WriteLine("error: input file does not exist");
            return 2;
        }

        string outputDirectory = Path.GetFullPath(options.OutputDirectory);

        try
        {
            ValidateOutputLocation(inputPath, outputDirectory);
            string repositoryRoot = RepositoryRootLocator.Resolve(options.RepositoryRoot);
            string manifestDirectory = Path.Combine(repositoryRoot, "models", "manifests");
            string modelDirectory = options.ModelDirectory is null
                ? Path.Combine(repositoryRoot, "models", "files")
                : Path.GetFullPath(options.ModelDirectory);

            ModelManifestLoader loader = new();
            ModelManifest detectorManifest = await loader.LoadAsync(
                Path.Combine(manifestDirectory, DetectorManifestFile),
                cancellationToken);
            ModelManifest embedderManifest = await loader.LoadAsync(
                Path.Combine(manifestDirectory, EmbedderManifestFile),
                cancellationToken);

            string detectorPath = RequireModelFile(modelDirectory, detectorManifest);
            string embedderPath = RequireModelFile(modelDirectory, embedderManifest);

            using YuNetFaceDetector detector = new(
                detectorManifest,
                detectorPath,
                new YuNetDetectorOptions
                {
                    ConfidenceThreshold = options.ConfidenceThreshold,
                });
            using SFaceFaceEmbedder embedder = new(embedderManifest, embedderPath);

            InspectPipeline pipeline = new(
                new OpenCvImageDecoder(),
                new OpenCvPngEncoder(),
                new OpenCvFaceCropper(),
                new OpenCvFaceAligner(),
                new YuNetInspectDetector(detector, InspectModelReport.FromManifest(detectorManifest)),
                new SFaceInspectEmbedder(embedder, InspectModelReport.FromManifest(embedderManifest)));

            InspectRunSummary summary = await pipeline.RunAsync(
                inputPath,
                outputDirectory,
                options.Overwrite,
                options.PaddingRatio,
                cancellationToken);

            output.WriteLine($"inspected: {summary.Width}x{summary.Height} {summary.PixelFormat}");
            output.WriteLine($"faces: {summary.FaceCount}");
            output.WriteLine($"output: {summary.OutputDirectory}");
            output.WriteLine($"manifest: {summary.ManifestPath}");
            output.WriteLine($"elapsed-ms: {summary.ElapsedMilliseconds:0.###}");
            output.WriteLine($"input-unchanged: {summary.InputUnchanged.ToString().ToLowerInvariant()}");

            if (options.Verbose)
            {
                output.WriteLine($"input: {inputPath}");
                output.WriteLine($"detector: {detector.Descriptor.Id}");
                output.WriteLine($"embedder: {embedder.Descriptor.Id}");
            }

            return summary.InputUnchanged ? 0 : 1;
        }
        catch (InspectUsageException exception)
        {
            error.WriteLine($"error: {exception.Message}");
            return 2;
        }
        catch (ImageDecodingException exception)
            when (exception.Failure == ImageDecodingFailure.UnsupportedFormat)
        {
            error.WriteLine($"unsupported-format: {exception.Message}");
            return 3;
        }
        catch (ImageDecodingException exception)
            when (exception.Failure == ImageDecodingFailure.CorruptMedia)
        {
            error.WriteLine($"corrupt-media: {exception.Message}");
            return 4;
        }
        catch (Exception exception)
            when (exception is ModelManifestException or DirectoryNotFoundException or FileNotFoundException)
        {
            error.WriteLine($"model-unavailable: {exception.Message}");
            return 5;
        }
        catch (Exception exception)
            when (exception is YuNetOutputException or SFaceOutputException or OnnxRuntimeException)
        {
            error.WriteLine($"inference-failed: {exception.Message}");
            return 6;
        }
    }

    private static string RequireModelFile(string modelDirectory, ModelManifest manifest)
    {
        string path = Path.Combine(modelDirectory, manifest.FileName);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                $"Installed model '{manifest.ModelId}' was not found at '{path}'. " +
                $"Run ./models/install-models.ps1 -Id {manifest.ModelId}.",
                path);
    }

    private static void ValidateOutputLocation(string inputPath, string outputDirectory)
    {
        string relative = Path.GetRelativePath(outputDirectory, inputPath);
        bool inputIsInsideOutput = !Path.IsPathRooted(relative) &&
            relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);

        if (inputIsInsideOutput)
        {
            throw new InspectUsageException(
                "The output directory cannot contain the source image because --overwrite could delete it.");
        }
    }
}

internal sealed record InspectCommandOptions(
    string InputPath,
    string OutputDirectory,
    string? RepositoryRoot,
    string? ModelDirectory,
    double ConfidenceThreshold,
    double PaddingRatio,
    bool Overwrite,
    bool Verbose)
{
    public static InspectCommandOptions Parse(string[] args)
    {
        string? input = null;
        string? output = null;
        string? repositoryRoot = null;
        string? modelDirectory = null;
        double? confidence = null;
        double? padding = null;
        bool overwrite = false;
        bool verbose = false;

        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (option == "--overwrite")
            {
                overwrite = true;
                continue;
            }

            if (option == "--verbose")
            {
                verbose = true;
                continue;
            }

            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                input = Single(input, option, "input image");
                continue;
            }

            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--input":
                    input = Single(input, value, option);
                    break;
                case "--output":
                case "--output-dir":
                    output = Single(output, value, option);
                    break;
                case "--root":
                    repositoryRoot = Single(repositoryRoot, value, option);
                    break;
                case "--model-dir":
                    modelDirectory = Single(modelDirectory, value, option);
                    break;
                case "--confidence":
                    confidence = UnitInterval(confidence, value, option);
                    break;
                case "--padding":
                    padding = NonNegative(padding, value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (input is null)
        {
            throw new ArgumentException("An input image path is required.");
        }

        output ??= Path.Combine(".artifacts", "inspect", SafeFileStem(input));

        return new InspectCommandOptions(
            input,
            output,
            repositoryRoot,
            modelDirectory,
            confidence ?? 0.9,
            padding ?? 0.25,
            overwrite,
            verbose);
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        return value;
    }

    private static double UnitInterval(double? current, string value, string option)
    {
        double parsed = Number(current, value, option);
        return parsed is >= 0 and <= 1
            ? parsed
            : throw new ArgumentException($"Option '{option}' must be between 0 and 1.");
    }

    private static double NonNegative(double? current, string value, string option)
    {
        double parsed = Number(current, value, option);
        return parsed >= 0
            ? parsed
            : throw new ArgumentException($"Option '{option}' must be non-negative.");
    }

    private static double Number(double? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
               double.IsFinite(parsed)
            ? parsed
            : throw new ArgumentException($"Option '{option}' requires a finite number.");
    }

    private static string SafeFileStem(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string safe = new(stem.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "image" : safe;
    }
}

internal sealed class InspectPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IImageDecoder _decoder;
    private readonly OpenCvPngEncoder _encoder;
    private readonly OpenCvFaceCropper _cropper;
    private readonly IFaceAligner _aligner;
    private readonly IInspectDetector _detector;
    private readonly IInspectEmbedder _embedder;

    public InspectPipeline(
        IImageDecoder decoder,
        OpenCvPngEncoder encoder,
        OpenCvFaceCropper cropper,
        IFaceAligner aligner,
        IInspectDetector detector,
        IInspectEmbedder embedder)
    {
        _decoder = decoder;
        _encoder = encoder;
        _cropper = cropper;
        _aligner = aligner;
        _detector = detector;
        _embedder = embedder;
    }

    public async Task<InspectRunSummary> RunAsync(
        string inputPath,
        string outputDirectory,
        bool overwrite,
        double paddingRatio,
        CancellationToken cancellationToken)
    {
        PrepareOutputDirectory(outputDirectory, overwrite);
        byte[] sourceHashBefore = await ComputeFileHashAsync(inputPath, cancellationToken);
        long totalStarted = Stopwatch.GetTimestamp();

        ImageFrame image;
        TimeSpan decodeDuration;
        await using (FileStream source = new(
                         inputPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 64 * 1024,
                         useAsync: true))
        {
            long decodeStarted = Stopwatch.GetTimestamp();
            image = await _decoder.DecodeAsync(source, new DecodeOptions(), cancellationToken);
            decodeDuration = Stopwatch.GetElapsedTime(decodeStarted);
        }

        byte[] normalisedPng = await EncodePngAsync(image, cancellationToken);
        string normalisedPath = Path.Combine(outputDirectory, "normalised.png");
        await File.WriteAllBytesAsync(normalisedPath, normalisedPng, cancellationToken);

        InspectDetectionStage detection = await _detector.DetectAsync(image, cancellationToken);
        IReadOnlyList<DetectedFaceCandidate> faces = detection.Faces
            .OrderByDescending(face => face.Confidence)
            .ThenBy(face => face.BoundingBox.Y)
            .ThenBy(face => face.BoundingBox.X)
            .ToArray();

        List<InspectFaceManifest> faceManifests = [];
        List<InspectFaceTiming> faceTimings = [];
        string facesDirectory = Path.Combine(outputDirectory, "faces");
        Directory.CreateDirectory(facesDirectory);

        AlignmentProtocolId protocol = _embedder.Descriptor.AlignmentProtocol ??
            throw new InvalidOperationException("The embedding model does not declare an alignment protocol.");

        for (int index = 0; index < faces.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int faceNumber = index + 1;
            DetectedFaceCandidate face = faces[index];
            string relativeDirectory = $"faces/face-{faceNumber:000}";
            string faceDirectory = Path.Combine(facesDirectory, $"face-{faceNumber:000}");
            Directory.CreateDirectory(faceDirectory);

            long cropStarted = Stopwatch.GetTimestamp();
            PaddedFaceCrop crop = _cropper.CreatePaddedCrop(
                image,
                face,
                new FaceCropOptions { PaddingRatio = paddingRatio },
                cancellationToken);
            TimeSpan cropDuration = Stopwatch.GetElapsedTime(cropStarted);

            long alignmentStarted = Stopwatch.GetTimestamp();
            AlignedFace aligned = await _aligner.AlignAsync(
                image,
                face,
                protocol,
                cancellationToken);
            TimeSpan alignmentDuration = Stopwatch.GetElapsedTime(alignmentStarted);

            InspectEmbeddingStage embedding = await _embedder.EmbedAsync(aligned, cancellationToken);

            byte[] cropPng = await EncodePngAsync(crop.Image, cancellationToken);
            byte[] alignedPng = await EncodePngAsync(aligned.Image, cancellationToken);
            string cropPath = Path.Combine(faceDirectory, "crop.png");
            string alignedPath = Path.Combine(faceDirectory, "aligned.png");
            await File.WriteAllBytesAsync(cropPath, cropPng, cancellationToken);
            await File.WriteAllBytesAsync(alignedPath, alignedPng, cancellationToken);

            InspectEmbeddingFile embeddingFile = new(
                SchemaVersion: 1,
                ModelId: embedding.Descriptor.Id.Value,
                ModelHash: embedding.Descriptor.ModelHash.Value,
                Dimensions: embedding.Embedding.Dimensions,
                L2Norm: embedding.Embedding.L2Norm,
                Values: embedding.Embedding.ToArray());
            byte[] embeddingJson = JsonSerializer.SerializeToUtf8Bytes(embeddingFile, JsonOptions);
            string embeddingPath = Path.Combine(faceDirectory, "embedding.json");
            await File.WriteAllBytesAsync(embeddingPath, embeddingJson, cancellationToken);

            faceManifests.Add(new InspectFaceManifest(
                Index: faceNumber,
                Confidence: face.Confidence,
                BoundingBox: InspectBoundingBox.From(face.BoundingBox),
                Landmarks: InspectLandmarks.From(face.Landmarks),
                Crop: new InspectCropOutput(
                    File: $"{relativeDirectory}/crop.png",
                    SourceBounds: InspectBoundingBox.From(crop.SourceBounds),
                    ContentHash: crop.ContentHash.Value,
                    PngSha256: Hash(cropPng)),
                Aligned: new InspectAlignedOutput(
                    File: $"{relativeDirectory}/aligned.png",
                    Protocol: aligned.Protocol.Value,
                    Width: aligned.Image.Size.Width,
                    Height: aligned.Image.Size.Height,
                    PngSha256: Hash(alignedPng)),
                Embedding: new InspectEmbeddingOutput(
                    File: $"{relativeDirectory}/embedding.json",
                    Dimensions: embedding.Embedding.Dimensions,
                    L2Norm: embedding.Embedding.L2Norm,
                    Sha256: Hash(embeddingJson))));

            faceTimings.Add(new InspectFaceTiming(
                Index: faceNumber,
                CropMilliseconds: Milliseconds(cropDuration),
                AlignmentMilliseconds: Milliseconds(alignmentDuration),
                EmbeddingPreprocessingMilliseconds: Milliseconds(embedding.PreprocessingDuration),
                EmbeddingInferenceMilliseconds: Milliseconds(embedding.InferenceDuration),
                EmbeddingPostprocessingMilliseconds: Milliseconds(embedding.PostprocessingDuration)));
        }

        byte[] annotatedSvg = Encoding.UTF8.GetBytes(
            SvgAnnotationWriter.Create(image.Size, normalisedPng, faces));
        string annotatedPath = Path.Combine(outputDirectory, "annotated.svg");
        await File.WriteAllBytesAsync(annotatedPath, annotatedSvg, cancellationToken);

        byte[] sourceHashAfter = await ComputeFileHashAsync(inputPath, cancellationToken);
        bool inputUnchanged = sourceHashBefore.AsSpan().SequenceEqual(sourceHashAfter);
        TimeSpan totalDuration = Stopwatch.GetElapsedTime(totalStarted);

        InspectTimingReport timingReport = new(
            SchemaVersion: 1,
            DecodeMilliseconds: Milliseconds(decodeDuration),
            DetectionPreprocessingMilliseconds: Milliseconds(detection.PreprocessingDuration),
            DetectionInferenceMilliseconds: Milliseconds(detection.InferenceDuration),
            DetectionPostprocessingMilliseconds: Milliseconds(detection.PostprocessingDuration),
            Faces: faceTimings,
            TotalMilliseconds: Milliseconds(totalDuration));
        byte[] timingJson = JsonSerializer.SerializeToUtf8Bytes(timingReport, JsonOptions);
        string timingPath = Path.Combine(outputDirectory, "timings.json");
        await File.WriteAllBytesAsync(timingPath, timingJson, cancellationToken);

        InspectManifest manifest = new(
            SchemaVersion: 1,
            Source: new InspectSourceManifest(
                FileName: Path.GetFileName(inputPath),
                Sha256: Convert.ToHexString(sourceHashBefore).ToLowerInvariant(),
                Width: image.Size.Width,
                Height: image.Size.Height,
                PixelFormat: image.Format.ToString(),
                InputUnchanged: inputUnchanged),
            Detector: _detector.Model,
            Embedder: _embedder.Model,
            FaceCount: faces.Count,
            Faces: faceManifests,
            Outputs: new InspectRunOutputs(
                NormalisedImage: "normalised.png",
                NormalisedImageSha256: Hash(normalisedPng),
                AnnotatedImage: "annotated.svg",
                AnnotatedImageSha256: Hash(annotatedSvg),
                Timings: "timings.json",
                TimingsSha256: Hash(timingJson)));
        byte[] manifestJson = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        string manifestPath = Path.Combine(outputDirectory, "manifest.json");
        await File.WriteAllBytesAsync(manifestPath, manifestJson, cancellationToken);

        return new InspectRunSummary(
            image.Size.Width,
            image.Size.Height,
            image.Format.ToString(),
            faces.Count,
            outputDirectory,
            manifestPath,
            inputUnchanged,
            Milliseconds(totalDuration));
    }

    private async Task<byte[]> EncodePngAsync(
        ImageFrame image,
        CancellationToken cancellationToken)
    {
        await using MemoryStream stream = new();
        await _encoder.EncodeAsync(image, stream, cancellationToken);
        return stream.ToArray();
    }

    private static void PrepareOutputDirectory(string outputDirectory, bool overwrite)
    {
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            if (!overwrite)
            {
                throw new InspectUsageException(
                    $"Output directory '{outputDirectory}' is not empty. Use --overwrite to replace it.");
            }

            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);
    }

    private static async Task<byte[]> ComputeFileHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        using SHA256 sha256 = SHA256.Create();
        return await sha256.ComputeHashAsync(stream, cancellationToken);
    }

    private static string Hash(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static double Milliseconds(TimeSpan duration) =>
        Math.Round(duration.TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
}

internal interface IInspectDetector
{
    ModelDescriptor Descriptor { get; }
    InspectModelReport Model { get; }
    Task<InspectDetectionStage> DetectAsync(ImageFrame image, CancellationToken cancellationToken);
}

internal interface IInspectEmbedder
{
    ModelDescriptor Descriptor { get; }
    InspectModelReport Model { get; }
    Task<InspectEmbeddingStage> EmbedAsync(AlignedFace face, CancellationToken cancellationToken);
}

internal sealed class YuNetInspectDetector : IInspectDetector
{
    private readonly YuNetFaceDetector _detector;

    public YuNetInspectDetector(YuNetFaceDetector detector, InspectModelReport model)
    {
        _detector = detector;
        Model = model;
    }

    public ModelDescriptor Descriptor => _detector.Descriptor;
    public InspectModelReport Model { get; }

    public async Task<InspectDetectionStage> DetectAsync(
        ImageFrame image,
        CancellationToken cancellationToken)
    {
        YuNetDetectionResult result = await _detector.DetectWithMetricsAsync(image, cancellationToken);
        return new InspectDetectionStage(
            result.Descriptor,
            result.Faces,
            result.PreprocessingDuration,
            result.InferenceDuration,
            result.PostprocessingDuration);
    }
}

internal sealed class SFaceInspectEmbedder : IInspectEmbedder
{
    private readonly SFaceFaceEmbedder _embedder;

    public SFaceInspectEmbedder(SFaceFaceEmbedder embedder, InspectModelReport model)
    {
        _embedder = embedder;
        Model = model;
    }

    public ModelDescriptor Descriptor => _embedder.Descriptor;
    public InspectModelReport Model { get; }

    public async Task<InspectEmbeddingStage> EmbedAsync(
        AlignedFace face,
        CancellationToken cancellationToken)
    {
        SFaceEmbeddingResult result = await _embedder.EmbedWithMetricsAsync(face, cancellationToken);
        return new InspectEmbeddingStage(
            result.Descriptor,
            result.Embedding,
            result.PreprocessingDuration,
            result.InferenceDuration,
            result.PostprocessingDuration);
    }
}

internal sealed record InspectDetectionStage(
    ModelDescriptor Descriptor,
    IReadOnlyList<DetectedFaceCandidate> Faces,
    TimeSpan PreprocessingDuration,
    TimeSpan InferenceDuration,
    TimeSpan PostprocessingDuration);

internal sealed record InspectEmbeddingStage(
    ModelDescriptor Descriptor,
    EmbeddingVector Embedding,
    TimeSpan PreprocessingDuration,
    TimeSpan InferenceDuration,
    TimeSpan PostprocessingDuration);

internal sealed record InspectRunSummary(
    int Width,
    int Height,
    string PixelFormat,
    int FaceCount,
    string OutputDirectory,
    string ManifestPath,
    bool InputUnchanged,
    double ElapsedMilliseconds);

internal sealed record InspectModelReport(
    string ModelId,
    string Role,
    string Format,
    string FileName,
    string Sha256,
    long SizeBytes,
    string Runtime,
    string SourceVersion,
    InspectModelInput Input,
    InspectModelOutput Output,
    string? AlignmentProtocol)
{
    public static InspectModelReport FromManifest(ModelManifest manifest) => new(
        manifest.ModelId,
        manifest.Role,
        manifest.Format,
        manifest.FileName,
        manifest.Sha256,
        manifest.SizeBytes,
        manifest.Runtime,
        manifest.SourceVersion,
        new InspectModelInput(
            manifest.Input.Width,
            manifest.Input.Height,
            manifest.Input.ColourOrder,
            manifest.Input.DataType,
            manifest.Input.Normalisation.Scale,
            manifest.Input.Normalisation.Mean),
        new InspectModelOutput(
            manifest.Output.Kind,
            manifest.Output.Dimensions,
            manifest.Output.Normalisation,
            manifest.Output.DistanceMetric),
        manifest.AlignmentProtocol);
}

internal sealed record InspectModelInput(
    int Width,
    int Height,
    string ColourOrder,
    string DataType,
    double Scale,
    double[] Mean);

internal sealed record InspectModelOutput(
    string Kind,
    int? Dimensions,
    string Normalisation,
    string? DistanceMetric);

internal sealed record InspectManifest(
    int SchemaVersion,
    InspectSourceManifest Source,
    InspectModelReport Detector,
    InspectModelReport Embedder,
    int FaceCount,
    IReadOnlyList<InspectFaceManifest> Faces,
    InspectRunOutputs Outputs);

internal sealed record InspectSourceManifest(
    string FileName,
    string Sha256,
    int Width,
    int Height,
    string PixelFormat,
    bool InputUnchanged);

internal sealed record InspectFaceManifest(
    int Index,
    double Confidence,
    InspectBoundingBox BoundingBox,
    InspectLandmarks Landmarks,
    InspectCropOutput Crop,
    InspectAlignedOutput Aligned,
    InspectEmbeddingOutput Embedding);

internal sealed record InspectCropOutput(
    string File,
    InspectBoundingBox SourceBounds,
    string ContentHash,
    string PngSha256);

internal sealed record InspectAlignedOutput(
    string File,
    string Protocol,
    int Width,
    int Height,
    string PngSha256);

internal sealed record InspectEmbeddingOutput(
    string File,
    int Dimensions,
    double L2Norm,
    string Sha256);

internal sealed record InspectRunOutputs(
    string NormalisedImage,
    string NormalisedImageSha256,
    string AnnotatedImage,
    string AnnotatedImageSha256,
    string Timings,
    string TimingsSha256);

internal sealed record InspectEmbeddingFile(
    int SchemaVersion,
    string ModelId,
    string ModelHash,
    int Dimensions,
    double L2Norm,
    float[] Values);

internal sealed record InspectTimingReport(
    int SchemaVersion,
    double DecodeMilliseconds,
    double DetectionPreprocessingMilliseconds,
    double DetectionInferenceMilliseconds,
    double DetectionPostprocessingMilliseconds,
    IReadOnlyList<InspectFaceTiming> Faces,
    double TotalMilliseconds);

internal sealed record InspectFaceTiming(
    int Index,
    double CropMilliseconds,
    double AlignmentMilliseconds,
    double EmbeddingPreprocessingMilliseconds,
    double EmbeddingInferenceMilliseconds,
    double EmbeddingPostprocessingMilliseconds);

internal sealed record InspectPoint(double X, double Y)
{
    public static InspectPoint From(NormalizedPoint point) => new(point.X, point.Y);
    public static InspectPoint From(PixelPoint point) => new(point.X, point.Y);
}

internal sealed record InspectBoundingBox(double X, double Y, double Width, double Height)
{
    public static InspectBoundingBox From(NormalizedBoundingBox box) =>
        new(box.X, box.Y, box.Width, box.Height);

    public static InspectBoundingBox From(PixelBoundingBox box) =>
        new(box.X, box.Y, box.Width, box.Height);
}

internal sealed record InspectLandmarks(
    InspectPoint LeftEye,
    InspectPoint RightEye,
    InspectPoint Nose,
    InspectPoint MouthLeft,
    InspectPoint MouthRight)
{
    public static InspectLandmarks From(NormalizedFaceLandmarks landmarks) => new(
        InspectPoint.From(landmarks.LeftEye),
        InspectPoint.From(landmarks.RightEye),
        InspectPoint.From(landmarks.Nose),
        InspectPoint.From(landmarks.MouthLeft),
        InspectPoint.From(landmarks.MouthRight));
}

internal static class SvgAnnotationWriter
{
    public static string Create(
        ImageSize size,
        ReadOnlySpan<byte> normalisedPng,
        IReadOnlyList<DetectedFaceCandidate> faces)
    {
        StringBuilder builder = new();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"")
            .Append(size.Width)
            .Append("\" height=\"")
            .Append(size.Height)
            .Append("\" viewBox=\"0 0 ")
            .Append(size.Width)
            .Append(' ')
            .Append(size.Height)
            .AppendLine("\">");
        builder.Append("  <image width=\"100%\" height=\"100%\" href=\"data:image/png;base64,")
            .Append(Convert.ToBase64String(normalisedPng))
            .AppendLine("\" />");
        builder.AppendLine("  <g font-family=\"Segoe UI,Arial,sans-serif\" font-size=\"14\" font-weight=\"600\">");

        for (int index = 0; index < faces.Count; index++)
        {
            DetectedFaceCandidate face = faces[index];
            PixelBoundingBox box = face.BoundingBox.ToPixels(size);
            PixelFaceLandmarks landmarks = face.Landmarks.ToPixels(size);
            double labelY = Math.Max(16, box.Y - 5);

            builder.Append("    <rect x=\"").Append(F(box.X))
                .Append("\" y=\"").Append(F(box.Y))
                .Append("\" width=\"").Append(F(box.Width))
                .Append("\" height=\"").Append(F(box.Height))
                .AppendLine("\" fill=\"none\" stroke=\"#00ff66\" stroke-width=\"3\" />");
            builder.Append("    <text x=\"").Append(F(box.X))
                .Append("\" y=\"").Append(F(labelY))
                .Append("\" fill=\"#00ff66\" stroke=\"#000\" stroke-width=\"3\" paint-order=\"stroke\">")
                .Append("face ").Append(index + 1).Append(' ')
                .Append(face.Confidence.ToString("0.000", CultureInfo.InvariantCulture))
                .AppendLine("</text>");

            AppendPoint(builder, landmarks.LeftEye, "#00d8ff", "LE");
            AppendPoint(builder, landmarks.RightEye, "#00d8ff", "RE");
            AppendPoint(builder, landmarks.Nose, "#ffea00", "N");
            AppendPoint(builder, landmarks.MouthLeft, "#ff4fd8", "ML");
            AppendPoint(builder, landmarks.MouthRight, "#ff4fd8", "MR");
        }

        builder.AppendLine("  </g>");
        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static void AppendPoint(
        StringBuilder builder,
        PixelPoint point,
        string colour,
        string label)
    {
        builder.Append("    <circle cx=\"").Append(F(point.X))
            .Append("\" cy=\"").Append(F(point.Y))
            .Append("\" r=\"4\" fill=\"").Append(colour)
            .AppendLine("\" stroke=\"#000\" stroke-width=\"1\" />");
        builder.Append("    <text x=\"").Append(F(point.X + 6))
            .Append("\" y=\"").Append(F(point.Y - 6))
            .Append("\" fill=\"").Append(colour)
            .Append("\" stroke=\"#000\" stroke-width=\"3\" paint-order=\"stroke\">")
            .Append(label)
            .AppendLine("</text>");
    }

    private static string F(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

internal static class RepositoryRootLocator
{
    public static string Resolve(string? explicitRoot)
    {
        if (explicitRoot is not null)
        {
            string root = Path.GetFullPath(explicitRoot);
            return IsRepositoryRoot(root)
                ? root
                : throw new DirectoryNotFoundException(
                    $"Repository root '{root}' does not contain PhotoIdentity.slnx and model manifests.");
        }

        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (IsRepositoryRoot(directory.FullName))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root. Run from the repository or supply --root PATH.");
    }

    private static bool IsRepositoryRoot(string path) =>
        File.Exists(Path.Combine(path, "PhotoIdentity.slnx")) &&
        Directory.Exists(Path.Combine(path, "models", "manifests"));
}

internal sealed class InspectUsageException : Exception
{
    public InspectUsageException(string message)
        : base(message)
    {
    }
}