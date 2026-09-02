using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Catalogue;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Recognition.Onnx.CenterFace;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.SFace;
using PhotoIdentity.Recognition.Onnx.YuNet;

namespace PhotoIdentity.Worker;

/// <summary>
/// Runs the production local decode, detect, align and embed path for one immutable revision.
/// </summary>
public sealed class LocalInspectionJobHandler : IProcessingJobHandler, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IAssetRevisionLookupRepository _assetRepository;
    private readonly IFaceInspectionRepository _faceRepository;
    private readonly LocalBatchConfiguration _configuration;
    private readonly IImageDecoder _decoder;
    private readonly OpenCvPngEncoder _encoder;
    private readonly IFaceDetector _detector;
    private readonly IFaceAligner _aligner;
    private readonly IFaceEmbedder _embedder;
    private readonly TimeProvider _timeProvider;
    private readonly ArchiveThroughputMetrics? _metrics;
    private readonly IDisposable? _sessionLifetime;
    private bool _disposed;

    public LocalInspectionJobHandler(
        IAssetRevisionLookupRepository assetRepository,
        IFaceInspectionRepository faceRepository,
        LocalBatchConfiguration configuration,
        IImageDecoder decoder,
        OpenCvPngEncoder encoder,
        IFaceDetector detector,
        IFaceAligner aligner,
        IFaceEmbedder embedder,
        TimeProvider? timeProvider = null,
        ArchiveThroughputMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(assetRepository);
        ArgumentNullException.ThrowIfNull(faceRepository);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(aligner);
        ArgumentNullException.ThrowIfNull(embedder);

        _assetRepository = assetRepository;
        _faceRepository = faceRepository;
        _configuration = configuration;
        _decoder = decoder;
        _encoder = encoder;
        _detector = detector;
        _aligner = aligner;
        _embedder = embedder;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _metrics = metrics;
        _sessionLifetime = metrics?.Measure(ArchiveThroughputMetricNames.AnalysisSessionLifetime);
    }

    public static async Task<LocalInspectionJobHandler> CreateAsync(
        IAssetRevisionLookupRepository assetRepository,
        IFaceInspectionRepository faceRepository,
        LocalBatchConfiguration configuration,
        CancellationToken cancellationToken = default,
        ArchiveThroughputMetrics? metrics = null)
    {
        using IDisposable? initialization = metrics?.Measure(
            ArchiveThroughputMetricNames.AnalysisSessionInitialization);
        string manifestDirectory = Path.Combine(configuration.RepositoryRoot, "models", "manifests");
        ModelManifestLoader loader = new();
        IReadOnlyList<ModelManifest> manifests = await loader.LoadDirectoryAsync(
            manifestDirectory,
            cancellationToken);
        ModelManifest detectorManifest = RequireManifest(
            manifests,
            configuration.DetectorModelId,
            "faceDetection");
        ModelManifest embedderManifest = RequireManifest(
            manifests,
            configuration.EmbedderModelId,
            "faceEmbedding");
        string detectorPath = RequireModelFile(configuration.ModelDirectory, detectorManifest);
        string embedderPath = RequireModelFile(configuration.ModelDirectory, embedderManifest);

        IFaceDetector detector = CreateDetector(detectorManifest, detectorPath, configuration);
        try
        {
            SFaceFaceEmbedder embedder = new(embedderManifest, embedderPath);
            LocalInspectionJobHandler handler = new(
                assetRepository,
                faceRepository,
                configuration,
                new OpenCvImageDecoder(),
                new OpenCvPngEncoder(),
                detector,
                new OpenCvFaceAligner(),
                embedder,
                metrics: metrics);
            metrics?.RecordCounter(ArchiveThroughputMetricNames.ModelSessionInitializations);
            return handler;
        }
        catch
        {
            if (detector is IDisposable disposableDetector)
            {
                disposableDetector.Dispose();
            }

            throw;
        }
    }

    public async Task ProcessAsync(
        ProcessingJobContext context,
        IProcessingCheckpointWriter checkpointWriter,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checkpointWriter);

        try
        {
            _metrics?.RecordCounter(ArchiveThroughputMetricNames.AnalysisAttempts);
            AssetRevisionLookup asset = await _assetRepository.GetRevisionAsync(
                context.AssetRevisionId,
                cancellationToken)
                ?? throw Permanent($"Asset revision {context.AssetRevisionId} was not found.");
            if (!string.Equals(asset.SourceKind, "local-folder", StringComparison.Ordinal))
            {
                throw Permanent($"Asset revision {context.AssetRevisionId} is not owned by a local-folder source.");
            }

            string inputPath = ResolveSourcePath(asset.RootLocator, asset.SourceKey);
            if (!File.Exists(inputPath))
            {
                throw Permanent($"The catalogued source file is unavailable: {inputPath}");
            }

            Sha256Digest sourceHash = await ComputeHashAsync(
                inputPath,
                context.AssetRevisionId.ToString(),
                cancellationToken);
            if (sourceHash != asset.ContentHash)
            {
                throw Permanent(
                    $"The source content changed after cataloguing for revision {asset.RevisionId}; rescan before resuming.");
            }

            ImageFrame image;
            using (IDisposable? decodeTiming = _metrics?.Measure(ArchiveThroughputMetricNames.ImageDecode))
            {
                await using FileStream stream = new(
                    inputPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    useAsync: true);
                image = await _decoder.DecodeAsync(stream, new DecodeOptions(), cancellationToken);
            }

            IReadOnlyList<DetectedFaceCandidate> faces;
            using (IDisposable? detectionTiming = _metrics?.Measure(ArchiveThroughputMetricNames.FaceDetection))
            {
                faces = (await _detector.DetectAsync(image, cancellationToken))
                    .OrderByDescending(face => face.Confidence)
                    .ThenBy(face => face.BoundingBox.Y)
                    .ThenBy(face => face.BoundingBox.X)
                    .ToArray();
            }
            _metrics?.RecordCounter(ArchiveThroughputMetricNames.FacesDetected, faces.Count);
            InspectionCheckpoint checkpoint = ParseCheckpoint(context.CheckpointJson);
            if (checkpoint.CompletedFaceCount > faces.Count)
            {
                throw Permanent("The saved checkpoint contains more faces than the current deterministic detection result.");
            }

            AlignmentProtocolId protocol = _embedder.Descriptor.AlignmentProtocol
                ?? throw Permanent("The embedding model does not declare an alignment protocol.");
            string assetOutputDirectory = Path.Combine(
                _configuration.OutputRoot,
                "runs",
                context.RunId.ToString(),
                "assets",
                context.AssetRevisionId.ToString());

            for (int index = checkpoint.CompletedFaceCount; index < faces.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DetectedFaceCandidate face = faces[index];
                AlignedFace aligned;
                using (IDisposable? alignmentTiming = _metrics?.Measure(ArchiveThroughputMetricNames.FaceAlignment))
                {
                    aligned = await _aligner.AlignAsync(image, face, protocol, cancellationToken);
                }

                EmbeddingVector embedding;
                using (IDisposable? embeddingTiming = _metrics?.Measure(ArchiveThroughputMetricNames.FaceEmbedding))
                {
                    embedding = await _embedder.EmbedAsync(aligned, cancellationToken);
                }

                using (IDisposable? persistenceTiming = _metrics?.Measure(ArchiveThroughputMetricNames.FacePersistence))
                {
                    byte[] alignedPng = await EncodePngAsync(aligned.Image, cancellationToken);
                    Sha256Digest cropHash = Digest(alignedPng);
                    string relativeStoragePath = Path.Combine(
                            "runs",
                            context.RunId.ToString(),
                            "assets",
                            context.AssetRevisionId.ToString(),
                            "faces",
                            $"face-{index + 1:000}",
                            "aligned.png")
                        .Replace('\\', '/');
                    string storagePath = Path.Combine(
                        _configuration.OutputRoot,
                        relativeStoragePath.Replace('/', Path.DirectorySeparatorChar));
                    await WriteAtomicallyAsync(storagePath, alignedPng, cancellationToken);

                    DateTimeOffset observedAt = _timeProvider.GetUtcNow();
                    FaceOccurrenceId occurrenceId = FaceOccurrenceId.New();
                    FaceCropId cropId = FaceCropId.New();
                    await _faceRepository.SaveInspectionAsync(
                        new FaceInspectionWrite(
                            occurrenceId,
                            context.AssetRevisionId,
                            index,
                            observedAt,
                            _detector.Descriptor.Id,
                            _detector.Descriptor.ModelHash,
                            face.Confidence,
                            face.BoundingBox,
                            face.Landmarks,
                            cropId,
                            protocol,
                            cropHash,
                            relativeStoragePath,
                            aligned.Image.Size.Width,
                            aligned.Image.Size.Height,
                            _embedder.Descriptor.Id,
                            _embedder.Descriptor.ModelHash,
                            embedding),
                        cancellationToken);

                    await checkpointWriter.WriteAsync(
                        JsonSerializer.Serialize(
                            new InspectionCheckpoint(1, index + 1, faces.Count),
                            JsonOptions),
                        cancellationToken);
                }
            }

            using (IDisposable? resultTiming = _metrics?.Measure(ArchiveThroughputMetricNames.AnalysisResultPersistence))
            {
                Directory.CreateDirectory(assetOutputDirectory);
                byte[] resultJson = JsonSerializer.SerializeToUtf8Bytes(
                    new InspectionResult(
                        1,
                        context.AssetRevisionId.ToString(),
                        sourceHash.ToString(),
                        faces.Count,
                        _detector.Descriptor.Id.ToString(),
                        _detector.Descriptor.ModelHash.ToString(),
                        _embedder.Descriptor.Id.ToString(),
                        _embedder.Descriptor.ModelHash.ToString(),
                        context.IdempotencyKey),
                    JsonOptions);
                await WriteAtomicallyAsync(
                    Path.Combine(assetOutputDirectory, "result.json"),
                    resultJson,
                    cancellationToken);

                if (faces.Count == 0 || checkpoint.CompletedFaceCount == faces.Count)
                {
                    await checkpointWriter.WriteAsync(
                        JsonSerializer.Serialize(new InspectionCheckpoint(1, faces.Count, faces.Count), JsonOptions),
                        cancellationToken);
                }
            }
        }
        catch (ProcessingJobFailureException)
        {
            throw;
        }
        catch (IOException exception) when (exception is not FileNotFoundException and not DirectoryNotFoundException)
        {
            throw new ProcessingJobFailureException(
                ProcessingFailureKind.Transient,
                exception.Message,
                exception);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw Permanent(exception.Message, exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_detector is IDisposable detector)
        {
            detector.Dispose();
        }

        if (_embedder is IDisposable embedder)
        {
            embedder.Dispose();
        }

        _sessionLifetime?.Dispose();
        _disposed = true;
    }

    private async Task<byte[]> EncodePngAsync(ImageFrame image, CancellationToken cancellationToken)
    {
        await using MemoryStream stream = new();
        await _encoder.EncodeAsync(image, stream, cancellationToken);
        return stream.ToArray();
    }

    private static InspectionCheckpoint ParseCheckpoint(string? json)
    {
        if (json is null)
        {
            return new InspectionCheckpoint(1, 0, 0);
        }

        InspectionCheckpoint? checkpoint = JsonSerializer.Deserialize<InspectionCheckpoint>(json, JsonOptions);
        if (checkpoint is null || checkpoint.SchemaVersion != 1 || checkpoint.CompletedFaceCount < 0)
        {
            throw Permanent("The processing checkpoint is invalid or unsupported.");
        }

        return checkpoint;
    }

    private static string ResolveSourcePath(string rootLocator, string sourceKey)
    {
        string root = Path.GetFullPath(rootLocator);
        string platformKey = sourceKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.Combine(root, platformKey));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(prefix, comparison))
        {
            throw Permanent("The catalogued source key resolves outside its configured local root.");
        }

        return resolved;
    }

    private async Task<Sha256Digest> ComputeHashAsync(
        string path,
        string subjectKey,
        CancellationToken cancellationToken)
    {
        using IDisposable? timing = _metrics?.Measure(ArchiveThroughputMetricNames.AnalysisSourceHash);
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        long bytes = stream.Length;
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        _metrics?.RecordHashRead(
            ArchiveThroughputMetricNames.AnalysisHashKind,
            subjectKey,
            bytes);
        return new Sha256Digest(Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static Sha256Digest Digest(ReadOnlySpan<byte> bytes) =>
        new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    private static async Task WriteAtomicallyAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(path))
        {
            byte[] existing = await File.ReadAllBytesAsync(path, cancellationToken);
            if (existing.AsSpan().SequenceEqual(content))
            {
                return;
            }
        }

        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
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

    private static IFaceDetector CreateDetector(
        ModelManifest manifest,
        string modelPath,
        LocalBatchConfiguration configuration)
    {
        return manifest.ModelId switch
        {
            LocalBatchConfiguration.DefaultDetectorModelId => new YuNetFaceDetector(
                manifest,
                modelPath,
                new YuNetDetectorOptions
                {
                    ConfidenceThreshold = configuration.ConfidenceThreshold,
                    PipelineMode = configuration.DetectorPipeline == LocalBatchConfiguration.MultiScaleDetectorPipeline
                        ? YuNetDetectorPipelineMode.MultiScale
                        : YuNetDetectorPipelineMode.SinglePass,
                    TileSize = configuration.TileSize,
                    TileOverlap = configuration.TileOverlap,
                    MergeNmsThreshold = configuration.MergeNmsThreshold,
                }),
            "centerface-2019-fp32" => CreateCenterFaceDetector(manifest, modelPath, configuration),
            _ => throw new ModelManifestException(
                $"No local detector adapter is registered for model '{manifest.ModelId}'."),
        };
    }

    private static IFaceDetector CreateCenterFaceDetector(
        ModelManifest manifest,
        string modelPath,
        LocalBatchConfiguration configuration)
    {
        if (configuration.DetectorPipeline != LocalBatchConfiguration.SinglePassDetectorPipeline)
        {
            throw new ModelManifestException(
                "CenterFace currently supports only the 'single-pass' detector pipeline. " +
                "Its bounded dynamic input-shape policy is defined by the model manifest.");
        }

        return new CenterFaceFaceDetector(
            manifest,
            modelPath,
            new CenterFaceDetectorOptions
            {
                ConfidenceThreshold = configuration.ConfidenceThreshold,
            });
    }

    private static ModelManifest RequireManifest(
        IReadOnlyList<ModelManifest> manifests,
        string modelId,
        string role)
    {
        ModelManifest[] matches = manifests
            .Where(value => string.Equals(value.ModelId, modelId, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new ModelManifestException(
                matches.Length == 0
                    ? $"Model manifest '{modelId}' was not found."
                    : $"Model manifest ID '{modelId}' is duplicated.");
        }

        ModelManifest manifest = matches[0];
        if (!string.Equals(manifest.Role, role, StringComparison.Ordinal))
        {
            throw new ModelManifestException(
                $"Model '{modelId}' has role '{manifest.Role}', but role '{role}' is required.");
        }

        return manifest;
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

    private static ProcessingJobFailureException Permanent(string message, Exception? inner = null) =>
        new(ProcessingFailureKind.Permanent, message, inner);

    private sealed record InspectionCheckpoint(
        int SchemaVersion,
        int CompletedFaceCount,
        int FaceCount);

    private sealed record InspectionResult(
        int SchemaVersion,
        string AssetRevisionId,
        string SourceSha256,
        int FaceCount,
        string DetectorModelId,
        string DetectorModelHash,
        string EmbedderModelId,
        string EmbedderModelHash,
        string IdempotencyKey);
}
