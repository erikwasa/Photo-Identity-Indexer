using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoIdentity.Core.Geometry;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.SFace;
using PhotoIdentity.Recognition.Onnx.YuNet;
using PhotoIdentity.Transfer.Bundles;

namespace PhotoIdentity.Worker;

public sealed record PortableRecognitionConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public PortableRecognitionConfiguration(double confidenceThreshold = 0.9)
    {
        if (!double.IsFinite(confidenceThreshold) || confidenceThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidenceThreshold),
                "The confidence threshold must be between zero and one.");
        }

        ConfidenceThreshold = confidenceThreshold;
    }

    public double ConfidenceThreshold { get; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static PortableRecognitionConfiguration FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        PortableRecognitionConfigurationData? data;
        try
        {
            data = JsonSerializer.Deserialize<PortableRecognitionConfigurationData>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new PortableBundleValidationException("Portable recognition configuration is invalid JSON.", exception);
        }

        return new PortableRecognitionConfiguration(data?.ConfidenceThreshold ?? 0.9);
    }

    private sealed record PortableRecognitionConfigurationData(double? ConfidenceThreshold);
}

/// <summary>
/// Runs the production OpenCV, YuNet and SFace pipeline for a verified portable job.
/// This processor has no database dependency.
/// </summary>
public sealed class PortableRecognitionProcessor : IPortableBundleProcessor
{
    private const string DetectorManifestFile = "yunet-2023mar-fp32.json";
    private const string EmbedderManifestFile = "sface-2021dec-fp32.json";
    private const string CropInputDetectorIdValue = "portable-aligned-face-crop-v1";
    private const string CropInputPrefix = "inputs/faces/face-";
    private const string CropInputSuffix = ".png";

    private static readonly ModelId CropInputDetectorId = new(CropInputDetectorIdValue);
    private static readonly Sha256Digest CropInputDetectorHash = Digest(Encoding.UTF8.GetBytes(CropInputDetectorIdValue));
    private static readonly NormalizedBoundingBox CropInputBoundingBox = new(0, 0, 1, 1);
    private static readonly NormalizedFaceLandmarks CropInputLandmarks = new(
        LeftEye: new NormalizedPoint(73.5318 / 112.0, 51.5014 / 112.0),
        RightEye: new NormalizedPoint(38.2946 / 112.0, 51.6963 / 112.0),
        Nose: new NormalizedPoint(56.0252 / 112.0, 71.7366 / 112.0),
        MouthLeft: new NormalizedPoint(70.7299 / 112.0, 92.2041 / 112.0),
        MouthRight: new NormalizedPoint(41.5493 / 112.0, 92.3655 / 112.0));

    private readonly IImageDecoder _decoder;
    private readonly OpenCvPngEncoder _encoder;
    private readonly IFaceAligner _aligner;
    private readonly Func<double, IFaceDetector> _detectorFactory;
    private readonly Func<IFaceEmbedder> _embedderFactory;
    private readonly TimeProvider _timeProvider;

    public PortableRecognitionProcessor(
        IImageDecoder decoder,
        OpenCvPngEncoder encoder,
        IFaceAligner aligner,
        Func<double, IFaceDetector> detectorFactory,
        Func<IFaceEmbedder> embedderFactory,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(aligner);
        ArgumentNullException.ThrowIfNull(detectorFactory);
        ArgumentNullException.ThrowIfNull(embedderFactory);

        _decoder = decoder;
        _encoder = encoder;
        _aligner = aligner;
        _detectorFactory = detectorFactory;
        _embedderFactory = embedderFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static async Task<PortableRecognitionProcessor> CreateAsync(
        string repositoryRoot,
        string? modelDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        string root = Path.GetFullPath(repositoryRoot);
        string manifests = Path.Combine(root, "models", "manifests");
        string models = modelDirectory is null
            ? Path.Combine(root, "models", "files")
            : Path.GetFullPath(modelDirectory);

        ModelManifestLoader loader = new();
        ModelManifest detectorManifest = await loader.LoadAsync(
            Path.Combine(manifests, DetectorManifestFile),
            cancellationToken);
        ModelManifest embedderManifest = await loader.LoadAsync(
            Path.Combine(manifests, EmbedderManifestFile),
            cancellationToken);
        string detectorPath = RequireModelFile(models, detectorManifest);
        string embedderPath = RequireModelFile(models, embedderManifest);

        return new PortableRecognitionProcessor(
            new OpenCvImageDecoder(),
            new OpenCvPngEncoder(),
            new OpenCvFaceAligner(),
            confidenceThreshold => new YuNetFaceDetector(
                detectorManifest,
                detectorPath,
                new YuNetDetectorOptions { ConfidenceThreshold = confidenceThreshold }),
            () => new SFaceFaceEmbedder(embedderManifest, embedderPath));
    }

    public async Task<PortableProcessingOutput> ProcessAsync(
        ExtractedPortableJob job,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        PortableRecognitionConfiguration configuration = PortableRecognitionConfiguration.FromJson(
            job.Manifest.ConfigurationJson);
        Directory.CreateDirectory(Path.GetFullPath(outputDirectory));

        IFaceEmbedder embedder = _embedderFactory();
        try
        {
            AlignmentProtocolId protocol = embedder.Descriptor.AlignmentProtocol
                ?? throw new PortableBundleValidationException(
                    "The embedding model does not declare an alignment protocol.");

            return job.Manifest.Profile switch
            {
                PortableBundleProfile.FullImage or PortableBundleProfile.ReducedImage =>
                    await ProcessImageAsync(job, outputDirectory, configuration, embedder, protocol, cancellationToken),
                PortableBundleProfile.FaceCrops =>
                    await ProcessFaceCropsAsync(job, outputDirectory, embedder, protocol, cancellationToken),
                _ => throw new PortableBundleValidationException(
                    $"Portable profile '{job.Manifest.Profile}' is not supported by the production processor."),
            };
        }
        finally
        {
            if (embedder is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private async Task<PortableProcessingOutput> ProcessImageAsync(
        ExtractedPortableJob job,
        string outputDirectory,
        PortableRecognitionConfiguration configuration,
        IFaceEmbedder embedder,
        AlignmentProtocolId protocol,
        CancellationToken cancellationToken)
    {
        string role = job.Manifest.Profile == PortableBundleProfile.FullImage
            ? PortableBundleRoles.SourceImage
            : PortableBundleRoles.ReducedImage;
        PortableBundleFile input = job.Manifest.Files.Single(file => file.Role == role);
        ImageFrame image = await DecodeAsync(job.ResolveFile(input), cancellationToken);
        IFaceDetector detector = _detectorFactory(configuration.ConfidenceThreshold);
        try
        {
            IReadOnlyList<DetectedFaceCandidate> detections = (await detector.DetectAsync(image, cancellationToken))
                .OrderByDescending(face => face.Confidence)
                .ThenBy(face => face.BoundingBox.Y)
                .ThenBy(face => face.BoundingBox.X)
                .ToArray();
            List<PortableProcessedFace> faces = [];
            for (int index = 0; index < detections.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DetectedFaceCandidate detection = detections[index];
                AlignedFace aligned = await _aligner.AlignAsync(
                    image,
                    detection,
                    protocol,
                    cancellationToken);
                EmbeddingVector embedding = await embedder.EmbedAsync(aligned, cancellationToken);
                string cropPath = Path.Combine(outputDirectory, $"face-{index + 1:000}.png");
                await EncodeAsync(aligned.Image, cropPath, cancellationToken);
                faces.Add(new PortableProcessedFace(
                    index,
                    detection.Confidence,
                    detection.BoundingBox,
                    detection.Landmarks,
                    cropPath,
                    aligned.Image.Size.Width,
                    aligned.Image.Size.Height,
                    embedding.ToArray()));
            }

            return new PortableProcessingOutput(
                detector.Descriptor.Id,
                detector.Descriptor.ModelHash,
                embedder.Descriptor.Id,
                embedder.Descriptor.ModelHash,
                protocol,
                faces,
                _timeProvider.GetUtcNow());
        }
        finally
        {
            if (detector is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private async Task<PortableProcessingOutput> ProcessFaceCropsAsync(
        ExtractedPortableJob job,
        string outputDirectory,
        IFaceEmbedder embedder,
        AlignmentProtocolId protocol,
        CancellationToken cancellationToken)
    {
        FaceCropInput[] inputs = job.Manifest.Files
            .Where(file => file.Role == PortableBundleRoles.FaceCrop)
            .Select(file => new FaceCropInput(ParseCropOrdinal(file.Path), file))
            .OrderBy(input => input.Ordinal)
            .ToArray();
        if (inputs.Select(input => input.Ordinal).Distinct().Count() != inputs.Length)
        {
            throw new PortableBundleValidationException("Face-crop inputs contain duplicate canonical ordinals.");
        }

        List<PortableProcessedFace> faces = [];
        foreach (FaceCropInput input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImageFrame image = await DecodeAsync(job.ResolveFile(input.File), cancellationToken);
            if (image.Size != embedder.Descriptor.InputSize)
            {
                throw new PortableBundleValidationException(
                    $"Face-crop input '{input.File.Path}' must be {embedder.Descriptor.InputSize.Width}x" +
                    $"{embedder.Descriptor.InputSize.Height}, but is {image.Size.Width}x{image.Size.Height}.");
            }

            AlignedFace aligned = new(image, protocol);
            EmbeddingVector embedding = await embedder.EmbedAsync(aligned, cancellationToken);
            string cropPath = Path.Combine(outputDirectory, $"face-{input.Ordinal + 1:000}.png");
            await EncodeAsync(image, cropPath, cancellationToken);
            faces.Add(new PortableProcessedFace(
                input.Ordinal,
                1,
                CropInputBoundingBox,
                CropInputLandmarks,
                cropPath,
                image.Size.Width,
                image.Size.Height,
                embedding.ToArray()));
        }

        return new PortableProcessingOutput(
            CropInputDetectorId,
            CropInputDetectorHash,
            embedder.Descriptor.Id,
            embedder.Descriptor.ModelHash,
            protocol,
            faces,
            _timeProvider.GetUtcNow());
    }

    private static int ParseCropOrdinal(string path)
    {
        if (!path.StartsWith(CropInputPrefix, StringComparison.Ordinal) ||
            !path.EndsWith(CropInputSuffix, StringComparison.Ordinal))
        {
            throw new PortableBundleValidationException(
                $"Face-crop input path '{path}' must match 'inputs/faces/face-NNN.png'.");
        }

        string numberText = path[CropInputPrefix.Length..^CropInputSuffix.Length];
        if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out int faceNumber) ||
            faceNumber <= 0 ||
            !string.Equals(path, $"{CropInputPrefix}{faceNumber:000}{CropInputSuffix}", StringComparison.Ordinal))
        {
            throw new PortableBundleValidationException(
                $"Face-crop input path '{path}' does not contain a canonical positive face number.");
        }

        return checked(faceNumber - 1);
    }

    private async Task<ImageFrame> DecodeAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        return await _decoder.DecodeAsync(stream, new DecodeOptions(), cancellationToken);
    }

    private async Task EncodeAsync(
        ImageFrame image,
        string path,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             useAsync: true))
            {
                await _encoder.EncodeAsync(image, stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
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

    private static Sha256Digest Digest(ReadOnlySpan<byte> bytes) =>
        new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    private sealed record FaceCropInput(int Ordinal, PortableBundleFile File);
}
