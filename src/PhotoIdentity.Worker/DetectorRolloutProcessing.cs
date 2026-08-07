using System.Security.Cryptography;
using System.Text.Json;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Imaging;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Imaging.OpenCv;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Recognition.Onnx.CenterFace;
using PhotoIdentity.Recognition.Onnx.Models;
using PhotoIdentity.Recognition.Onnx.SFace;

namespace PhotoIdentity.Worker;

public sealed record DetectorRolloutConfiguration
{
    public const string DetectorModelId = "centerface-2019-fp32";
    public const string EmbedderModelId = "sface-2021dec-fp32";
    public const double ConfidenceThreshold = 0.5;
    public const string DetectorPipeline = "single-pass";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public DetectorRolloutConfiguration(
        string outputRoot,
        string repositoryRoot,
        string? modelDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        OutputRoot = Path.GetFullPath(outputRoot);
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        ModelDirectory = modelDirectory is null
            ? Path.Combine(RepositoryRoot, "models", "files")
            : Path.GetFullPath(modelDirectory);
    }

    public string OutputRoot { get; }
    public string RepositoryRoot { get; }
    public string ModelDirectory { get; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static DetectorRolloutConfiguration FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        DetectorRolloutConfigurationData? data = JsonSerializer.Deserialize<DetectorRolloutConfigurationData>(json, JsonOptions);
        return data is null
            ? throw new InvalidDataException("The detector-rollout configuration is empty.")
            : new DetectorRolloutConfiguration(data.OutputRoot, data.RepositoryRoot, data.ModelDirectory);
    }

    private sealed record DetectorRolloutConfigurationData(
        string OutputRoot,
        string RepositoryRoot,
        string? ModelDirectory);
}

public sealed record DetectorRolloutStartResult(
    ProcessingRunId RunId,
    ProcessingRunSummary ProcessingSummary,
    CatalogueDetectorRolloutSummary RolloutSummary);

public sealed record DetectorRolloutResumeResult(
    ProcessingRunSummary ProcessingSummary,
    CatalogueDetectorRolloutSummary RolloutSummary);

public sealed class DetectorRolloutCoordinator
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public DetectorRolloutCoordinator(SqliteCatalogueDatabase database, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DetectorRolloutStartResult> StartAsync(
        DetectorRolloutConfiguration configuration,
        IReadOnlyCollection<AssetRevisionId> revisionIds,
        ResumableBatchProcessorOptions? processorOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(revisionIds);
        AssetRevisionId[] revisions = revisionIds
            .Distinct()
            .OrderBy(value => value.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (revisions.Length == 0)
        {
            throw new ArgumentException("At least one immutable asset revision is required.", nameof(revisionIds));
        }

        await _database.InitializeAsync(cancellationToken);
        SqliteLocalBatchRepository assetRepository = new(_database);
        foreach (AssetRevisionId revisionId in revisions)
        {
            CatalogueProcessingAssetRevision revision = await assetRepository.GetAssetRevisionAsync(
                revisionId,
                cancellationToken)
                ?? throw new KeyNotFoundException($"Asset revision {revisionId} was not found.");
            if (!string.Equals(revision.SourceKind, "local-folder", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Asset revision {revisionId} is not backed by a local-folder source and cannot be used by local rollout.");
            }
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        ProcessingRunId runId = ProcessingRunId.New();
        CatalogueProcessingRun run = new(
            runId,
            ProcessingRunStatus.Pending,
            configuration.ToJson(),
            now);
        CatalogueProcessingJob[] jobs = revisions
            .Select(revisionId => new CatalogueProcessingJob(
                ProcessingJobId.New(),
                runId,
                revisionId,
                ProcessingJobStatus.Queued,
                attemptCount: 0,
                availableAtUtc: now,
                idempotencyKey: $"detector-rollout:{runId}:{revisionId}"))
            .ToArray();

        SqliteProcessingRepository processingRepository = new(_database);
        await processingRepository.CreateRunAsync(run, jobs, cancellationToken);
        using DetectorRolloutJobHandler handler = await DetectorRolloutJobHandler.CreateAsync(
            _database,
            configuration,
            _timeProvider,
            cancellationToken);
        await new SqliteDetectorRolloutRepository(_database).RegisterPipelineAsync(
            runId,
            handler.PipelineDefinition,
            now,
            cancellationToken);
        ResumableBatchProcessorResult result = await new ResumableBatchProcessor(
                processingRepository,
                handler,
                _timeProvider)
            .RunUntilIdleAsync(runId, processorOptions, cancellationToken);
        CatalogueDetectorRolloutSummary rolloutSummary = await new SqliteDetectorRolloutApplicationRepository(_database)
            .GetSummaryAsync(runId, cancellationToken);
        return new DetectorRolloutStartResult(runId, result.Summary, rolloutSummary);
    }

    public async Task<DetectorRolloutResumeResult> ResumeAsync(
        ProcessingRunId runId,
        ResumableBatchProcessorOptions? processorOptions = null,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        SqliteProcessingRepository processingRepository = new(_database);
        CatalogueProcessingRun run = await processingRepository.GetRunAsync(runId, cancellationToken)
            ?? throw new KeyNotFoundException($"Processing run {runId} was not found.");
        DetectorRolloutConfiguration configuration = DetectorRolloutConfiguration.FromJson(run.ConfigurationJson);
        using DetectorRolloutJobHandler handler = await DetectorRolloutJobHandler.CreateAsync(
            _database,
            configuration,
            _timeProvider,
            cancellationToken);
        await new SqliteDetectorRolloutRepository(_database).RegisterPipelineAsync(
            runId,
            handler.PipelineDefinition,
            _timeProvider.GetUtcNow(),
            cancellationToken);
        ResumableBatchProcessorResult result = await new ResumableBatchProcessor(
                processingRepository,
                handler,
                _timeProvider)
            .RunUntilIdleAsync(runId, processorOptions, cancellationToken);
        CatalogueDetectorRolloutSummary rolloutSummary = await new SqliteDetectorRolloutApplicationRepository(_database)
            .GetSummaryAsync(runId, cancellationToken);
        return new DetectorRolloutResumeResult(result.Summary, rolloutSummary);
    }
}

/// <summary>
/// Processes one explicitly-scoped immutable revision through the selected CenterFace rollout pipeline.
/// The complete plan and every candidate payload are durable before any unambiguous candidate is applied.
/// </summary>
public sealed class DetectorRolloutJobHandler : IProcessingJobHandler, IDisposable
{
    private const double DetectorNmsThreshold = 0.30;
    private const int DetectorTopK = 5000;

    private readonly SqliteLocalBatchRepository _assetRepository;
    private readonly SqliteDetectorRolloutRepository _rolloutRepository;
    private readonly SqliteDetectorRolloutReviewRepository _reviewRepository;
    private readonly SqliteDetectorRolloutApplicationRepository _applicationRepository;
    private readonly DetectorRolloutConfiguration _configuration;
    private readonly IImageDecoder _decoder;
    private readonly OpenCvPngEncoder _encoder;
    private readonly IFaceDetector _detector;
    private readonly IFaceAligner _aligner;
    private readonly IFaceEmbedder _embedder;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public DetectorRolloutJobHandler(
        SqliteCatalogueDatabase database,
        DetectorRolloutConfiguration configuration,
        IImageDecoder decoder,
        OpenCvPngEncoder encoder,
        IFaceDetector detector,
        IFaceAligner aligner,
        IFaceEmbedder embedder,
        DetectorPipelineDefinition pipelineDefinition,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(aligner);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(pipelineDefinition);
        _assetRepository = new SqliteLocalBatchRepository(database);
        _rolloutRepository = new SqliteDetectorRolloutRepository(database);
        _reviewRepository = new SqliteDetectorRolloutReviewRepository(database);
        _applicationRepository = new SqliteDetectorRolloutApplicationRepository(database);
        _configuration = configuration;
        _decoder = decoder;
        _encoder = encoder;
        _detector = detector;
        _aligner = aligner;
        _embedder = embedder;
        PipelineDefinition = pipelineDefinition;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DetectorPipelineDefinition PipelineDefinition { get; }

    public static async Task<DetectorRolloutJobHandler> CreateAsync(
        SqliteCatalogueDatabase database,
        DetectorRolloutConfiguration configuration,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        string manifestDirectory = Path.Combine(configuration.RepositoryRoot, "models", "manifests");
        ModelManifestLoader loader = new();
        IReadOnlyList<ModelManifest> manifests = await loader.LoadDirectoryAsync(manifestDirectory, cancellationToken);
        ModelManifest detectorManifest = RequireManifest(manifests, DetectorRolloutConfiguration.DetectorModelId, "faceDetection");
        ModelManifest embedderManifest = RequireManifest(manifests, DetectorRolloutConfiguration.EmbedderModelId, "faceEmbedding");
        string detectorPath = RequireModelFile(configuration.ModelDirectory, detectorManifest);
        string embedderPath = RequireModelFile(configuration.ModelDirectory, embedderManifest);
        CenterFaceFaceDetector detector = new(
            detectorManifest,
            detectorPath,
            new CenterFaceDetectorOptions { ConfidenceThreshold = DetectorRolloutConfiguration.ConfidenceThreshold });
        try
        {
            SFaceFaceEmbedder embedder = new(embedderManifest, embedderPath);
            DetectorPipelineDefinition definition = CreatePipelineDefinition(detectorManifest);
            return new DetectorRolloutJobHandler(
                database,
                configuration,
                new OpenCvImageDecoder(),
                new OpenCvPngEncoder(),
                detector,
                new OpenCvFaceAligner(),
                embedder,
                definition,
                timeProvider);
        }
        catch
        {
            detector.Dispose();
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

        CatalogueProcessingAssetRevision asset = await _assetRepository.GetAssetRevisionAsync(
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

        Sha256Digest sourceHash = await ComputeHashAsync(inputPath, cancellationToken);
        if (sourceHash != asset.ContentHash)
        {
            throw Permanent(
                $"The source content changed after cataloguing for revision {asset.RevisionId}; rescan before rollout.");
        }

        ImageFrame image;
        await using (FileStream stream = new(
                         inputPath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 64 * 1024,
                         useAsync: true))
        {
            image = await _decoder.DecodeAsync(stream, new DecodeOptions(), cancellationToken);
        }

        IReadOnlyList<DetectedFaceCandidate> faces = (await _detector.DetectAsync(image, cancellationToken))
            .OrderByDescending(face => face.Confidence)
            .ThenBy(face => face.BoundingBox.Y)
            .ThenBy(face => face.BoundingBox.X)
            .ToArray();
        CandidateFaceDetectionAnchor[] candidateAnchors = faces
            .Select((face, index) => new CandidateFaceDetectionAnchor(index, face.BoundingBox, face.Landmarks))
            .ToArray();
        Sha256Digest pipelineHash = PipelineDefinition.ComputeHash();
        CatalogueDetectorReconciliationPlan? persistedPlan = await _rolloutRepository.GetPlanAsync(
            context.RunId,
            context.AssetRevisionId,
            cancellationToken);
        if (persistedPlan is null)
        {
            IReadOnlyList<ExistingFaceDetectionAnchor> existingAnchors =
                await _applicationRepository.GetExistingAnchorsAsync(
                    context.AssetRevisionId,
                    pipelineHash,
                    cancellationToken);
            FaceDetectionReconciliationPlan plan = FaceDetectionReconciliationPlanner.Plan(
                existingAnchors,
                candidateAnchors);
            persistedPlan = await _rolloutRepository.SavePlanAsync(
                context.RunId,
                context.AssetRevisionId,
                pipelineHash,
                candidateAnchors,
                plan,
                _timeProvider.GetUtcNow(),
                cancellationToken);
        }
        else
        {
            EnsureCandidateGeometryMatches(persistedPlan, candidateAnchors);
        }

        AlignmentProtocolId protocol = _embedder.Descriptor.AlignmentProtocol
            ?? throw Permanent("The embedding model does not declare an alignment protocol.");
        CatalogueDetectorCandidateInspection[] inspections = new CatalogueDetectorCandidateInspection[faces.Count];
        for (int index = 0; index < faces.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CatalogueDetectorCandidateInspection? existing = await _reviewRepository.GetInspectionAsync(
                context.RunId,
                context.AssetRevisionId,
                index,
                cancellationToken);
            if (existing is not null)
            {
                EnsureInspectionGeometryMatches(existing, faces[index]);
                inspections[index] = existing;
                continue;
            }

            DetectedFaceCandidate face = faces[index];
            AlignedFace aligned = await _aligner.AlignAsync(image, face, protocol, cancellationToken);
            EmbeddingVector embedding = await _embedder.EmbedAsync(aligned, cancellationToken);
            byte[] alignedPng = await EncodePngAsync(aligned.Image, cancellationToken);
            Sha256Digest cropHash = Digest(alignedPng);
            string relativeStoragePath = Path.Combine(
                    "rollouts",
                    context.RunId.ToString(),
                    "assets",
                    context.AssetRevisionId.ToString(),
                    "candidates",
                    $"candidate-{index + 1:000}",
                    "aligned.png")
                .Replace('\\', '/');
            string storagePath = Path.Combine(
                _configuration.OutputRoot,
                relativeStoragePath.Replace('/', Path.DirectorySeparatorChar));
            await WriteAtomicallyAsync(storagePath, alignedPng, cancellationToken);
            DateTimeOffset observedAt = _timeProvider.GetUtcNow();
            CatalogueDetectorCandidateInspection inspection = new(
                _detector.Descriptor.Id,
                _detector.Descriptor.ModelHash,
                face.Confidence,
                face.BoundingBox,
                face.Landmarks,
                FaceCropId.New(),
                protocol,
                cropHash,
                relativeStoragePath,
                aligned.Image.Size.Width,
                aligned.Image.Size.Height,
                _embedder.Descriptor.Id,
                _embedder.Descriptor.ModelHash,
                embedding,
                observedAt);
            inspections[index] = await _reviewRepository.SaveInspectionAsync(
                context.RunId,
                context.AssetRevisionId,
                index,
                inspection,
                cancellationToken);
        }

        foreach (CatalogueDetectorReconciliationCandidate candidate in persistedPlan.Candidates.OrderBy(value => value.CandidateIndex))
        {
            if (candidate.Disposition == FaceDetectionReconciliationDisposition.Ambiguous ||
                candidate.AppliedFaceOccurrenceId is not null)
            {
                continue;
            }

            _ = await _rolloutRepository.ApplyUnambiguousInspectionAsync(
                context.RunId,
                context.AssetRevisionId,
                candidate.CandidateIndex,
                inspections[candidate.CandidateIndex],
                cancellationToken);
        }

        await checkpointWriter.WriteAsync(
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                candidateCount = persistedPlan.Candidates.Count,
                ambiguousCount = persistedPlan.Candidates.Count(value => value.Disposition == FaceDetectionReconciliationDisposition.Ambiguous),
                unmatchedExistingCount = persistedPlan.ExistingOccurrencesWithoutCandidate.Count,
            }),
            cancellationToken);
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

        _disposed = true;
    }

    private async Task<byte[]> EncodePngAsync(ImageFrame image, CancellationToken cancellationToken)
    {
        await using MemoryStream stream = new();
        await _encoder.EncodeAsync(image, stream, cancellationToken);
        return stream.ToArray();
    }

    private static void EnsureCandidateGeometryMatches(
        CatalogueDetectorReconciliationPlan persistedPlan,
        IReadOnlyList<CandidateFaceDetectionAnchor> candidates)
    {
        if (persistedPlan.Candidates.Count != candidates.Count)
        {
            throw Permanent("The persisted rollout plan no longer matches the detector candidate count.");
        }

        Dictionary<int, CandidateFaceDetectionAnchor> byIndex = candidates.ToDictionary(value => value.CandidateIndex);
        foreach (CatalogueDetectorReconciliationCandidate candidate in persistedPlan.Candidates)
        {
            if (!byIndex.TryGetValue(candidate.CandidateIndex, out CandidateFaceDetectionAnchor? current) ||
                !GeometryEquals(candidate.BoundingBox, current.BoundingBox) ||
                !LandmarksEqual(candidate.Landmarks, current.Landmarks))
            {
                throw Permanent("The detector result changed after the rollout reconciliation plan was persisted.");
            }
        }
    }

    private static void EnsureInspectionGeometryMatches(
        CatalogueDetectorCandidateInspection inspection,
        DetectedFaceCandidate face)
    {
        if (!GeometryEquals(inspection.BoundingBox, face.BoundingBox) ||
            !LandmarksEqual(inspection.Landmarks, face.Landmarks))
        {
            throw Permanent("A persisted rollout candidate payload no longer matches the detector result.");
        }
    }

    private static bool GeometryEquals(NormalizedBoundingBox left, NormalizedBoundingBox right) =>
        Close(left.X, right.X) && Close(left.Y, right.Y) &&
        Close(left.Width, right.Width) && Close(left.Height, right.Height);

    private static bool LandmarksEqual(NormalizedFaceLandmarks left, NormalizedFaceLandmarks right) =>
        PointEquals(left.LeftEye, right.LeftEye) &&
        PointEquals(left.RightEye, right.RightEye) &&
        PointEquals(left.Nose, right.Nose) &&
        PointEquals(left.MouthLeft, right.MouthLeft) &&
        PointEquals(left.MouthRight, right.MouthRight);

    private static bool PointEquals(NormalizedPoint left, NormalizedPoint right) =>
        Close(left.X, right.X) && Close(left.Y, right.Y);

    private static bool Close(double left, double right) => Math.Abs(left - right) <= 1e-12;

    private static DetectorPipelineDefinition CreatePipelineDefinition(ModelManifest detectorManifest)
    {
        ModelManifestValidator.Validate(detectorManifest);
        string shapePolicy = detectorManifest.Input.ShapePolicy?.Kind ?? "fixed";
        return new DetectorPipelineDefinition(
            "centerface-opencv-dnn-v1",
            new ModelId(detectorManifest.ModelId),
            new Sha256Digest(detectorManifest.Sha256),
            detectorManifest.Runtime,
            DetectorRolloutConfiguration.ConfidenceThreshold,
            DetectorRolloutConfiguration.DetectorPipeline,
            "direct-resize-bounded-dynamic-multiple-of",
            detectorManifest.Input.Width,
            detectorManifest.Input.Height,
            shapePolicy,
            detectorManifest.Input.ShapePolicy?.MultipleOf,
            detectorManifest.Input.ShapePolicy?.MaximumLongEdge,
            detectorManifest.Input.ColourOrder,
            detectorManifest.Input.DataType,
            detectorManifest.Input.Normalisation.Scale,
            detectorManifest.Input.Normalisation.Mean,
            DetectorNmsThreshold,
            DetectorTopK,
            tileSize: null,
            tileOverlap: null,
            mergeNmsThreshold: null,
            rotationPolicy: "none");
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

    private static async Task<Sha256Digest> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
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

    private static ProcessingJobFailureException Permanent(string message, Exception? inner = null) =>
        new(ProcessingFailureKind.Permanent, message, inner);
}
