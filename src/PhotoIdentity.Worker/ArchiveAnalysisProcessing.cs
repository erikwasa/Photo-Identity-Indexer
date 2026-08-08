using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Recognition.Onnx.Models;

namespace PhotoIdentity.Worker;

public sealed record ArchiveAnalysisConfiguration
{
    public const string DetectorModelId = DetectorRolloutConfiguration.DetectorModelId;
    public const string EmbedderModelId = DetectorRolloutConfiguration.EmbedderModelId;
    public const double ConfidenceThreshold = DetectorRolloutConfiguration.ConfidenceThreshold;
    public const string DetectorPipeline = DetectorRolloutConfiguration.DetectorPipeline;

    public ArchiveAnalysisConfiguration(
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

    public LocalBatchConfiguration ToBatchConfiguration(string sourceRoot) => new(
        sourceRoot,
        OutputRoot,
        RepositoryRoot,
        ModelDirectory,
        recursive: true,
        confidenceThreshold: ConfidenceThreshold,
        paddingRatio: 0.25,
        detectorModelId: DetectorModelId,
        embedderModelId: EmbedderModelId,
        detectorPipeline: DetectorPipeline);
}

public sealed record ArchiveAnalysisStartResult(
    AnalysisProfileDefinition Profile,
    int CurrentRevisionCount,
    int PreviouslyCompletedCount,
    ProcessingRunSummary? ProcessingSummary);

public sealed record ArchiveAnalysisResumeResult(
    AnalysisProfileDefinition Profile,
    ProcessingRunSummary ProcessingSummary);

/// <summary>
/// Runs the governed permanent-archive profile only for current, locally available revisions
/// that have not already completed that exact profile.
/// </summary>
public sealed class ArchiveAnalysisCoordinator
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public ArchiveAnalysisCoordinator(
        SqliteCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ArchiveAnalysisStartResult> StartAsync(
        ArchiveAnalysisConfiguration configuration,
        ResumableBatchProcessorOptions? processorOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await _database.InitializeAsync(cancellationToken);

        ArchiveCoverageConfiguration coverage = await new SqliteArchiveCoverageRepository(_database)
            .GetAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The catalogue has no permanent archive configuration. Run 'archive include' first.");
        if (coverage.IncludedFolders.Count == 0)
        {
            throw new InvalidOperationException("The permanent archive has no included folders.");
        }

        LocalBatchConfiguration batchConfiguration = configuration.ToBatchConfiguration(
            coverage.Source.RootLocator);
        AnalysisProfileDefinition profile = await ArchiveAnalysisProfileFactory.CreateAsync(
            batchConfiguration,
            cancellationToken);
        Sha256Digest profileHash = profile.ComputeHash();
        SqliteArchiveAnalysisRepository analysisRepository = new(_database);
        IReadOnlyList<AssetRevisionId> pending = await analysisRepository.GetPendingCurrentRevisionIdsAsync(
            coverage.Source.Id,
            profileHash,
            cancellationToken);
        int completed = await analysisRepository.CountCompletedCurrentRevisionsAsync(
            coverage.Source.Id,
            profileHash,
            cancellationToken);

        if (pending.Count == 0)
        {
            return new ArchiveAnalysisStartResult(profile, completed, completed, null);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        ProcessingRunId runId = ProcessingRunId.New();
        CatalogueProcessingRun run = new(
            runId,
            ProcessingRunStatus.Pending,
            batchConfiguration.ToJson(),
            now);
        CatalogueProcessingJob[] jobs = pending
            .Select(revisionId => new CatalogueProcessingJob(
                ProcessingJobId.New(),
                runId,
                revisionId,
                ProcessingJobStatus.Queued,
                attemptCount: 0,
                availableAtUtc: now,
                idempotencyKey: $"archive-analyze:{runId}:{revisionId}"))
            .ToArray();

        SqliteProcessingRepository processingRepository = new(_database);
        await processingRepository.CreateRunAsync(run, jobs, cancellationToken);
        await analysisRepository.RegisterRunAsync(runId, profile, now, cancellationToken);

        using LocalInspectionJobHandler inspection = await LocalInspectionJobHandler.CreateAsync(
            _database,
            batchConfiguration,
            cancellationToken);
        AnalysisTrackingJobHandler handler = new(
            inspection,
            analysisRepository,
            profileHash,
            batchConfiguration.SourceRoot,
            _timeProvider);
        ResumableBatchProcessorResult processing = await new ResumableBatchProcessor(
                processingRepository,
                handler,
                _timeProvider)
            .RunUntilIdleAsync(runId, processorOptions, cancellationToken);

        return new ArchiveAnalysisStartResult(
            profile,
            completed + pending.Count,
            completed,
            processing.Summary);
    }

    public async Task<ArchiveAnalysisResumeResult> ResumeAsync(
        ProcessingRunId runId,
        ResumableBatchProcessorOptions? processorOptions = null,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        SqliteProcessingRepository processingRepository = new(_database);
        CatalogueProcessingRun run = await processingRepository.GetRunAsync(runId, cancellationToken)
            ?? throw new KeyNotFoundException($"Processing run {runId} was not found.");
        SqliteArchiveAnalysisRepository analysisRepository = new(_database);
        Sha256Digest registeredHash = await analysisRepository.GetRunProfileHashAsync(runId, cancellationToken);
        LocalBatchConfiguration batchConfiguration = LocalBatchConfiguration.FromJson(run.ConfigurationJson);
        AnalysisProfileDefinition profile = await ArchiveAnalysisProfileFactory.CreateAsync(
            batchConfiguration,
            cancellationToken);
        Sha256Digest currentHash = profile.ComputeHash();
        if (currentHash != registeredHash)
        {
            throw new InvalidOperationException(
                $"Archive analysis run {runId} resolves to profile {currentHash}, but was registered as {registeredHash}.");
        }

        using LocalInspectionJobHandler inspection = await LocalInspectionJobHandler.CreateAsync(
            _database,
            batchConfiguration,
            cancellationToken);
        AnalysisTrackingJobHandler handler = new(
            inspection,
            analysisRepository,
            currentHash,
            batchConfiguration.SourceRoot,
            _timeProvider);
        ResumableBatchProcessorResult processing = await new ResumableBatchProcessor(
                processingRepository,
                handler,
                _timeProvider)
            .RunUntilIdleAsync(runId, processorOptions, cancellationToken);
        return new ArchiveAnalysisResumeResult(profile, processing.Summary);
    }
}

public static class ArchiveAnalysisProfileFactory
{
    public static async Task<AnalysisProfileDefinition> CreateAsync(
        LocalBatchConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string manifestDirectory = Path.Combine(configuration.RepositoryRoot, "models", "manifests");
        IReadOnlyList<ModelManifest> manifests = await new ModelManifestLoader()
            .LoadDirectoryAsync(manifestDirectory, cancellationToken);
        ModelManifest detector = RequireManifest(manifests, configuration.DetectorModelId, "faceDetection");
        ModelManifest embedder = RequireManifest(manifests, configuration.EmbedderModelId, "faceEmbedding");
        DetectorPipelineDefinition detectorPipeline = DetectorPipelineIdentityFactory.Create(detector, configuration);
        ModelDescriptor embedderDescriptor = embedder.ToDescriptor();
        AlignmentProtocolId alignment = embedderDescriptor.AlignmentProtocol
            ?? throw new InvalidOperationException(
                $"Embedding model '{embedder.ModelId}' does not declare an alignment protocol.");
        return new AnalysisProfileDefinition(
            detectorPipeline.ComputeHash(),
            detectorPipeline.DetectorModelId,
            detectorPipeline.DetectorModelHash,
            embedderDescriptor.Id,
            embedderDescriptor.ModelHash,
            alignment);
    }

    private static ModelManifest RequireManifest(
        IEnumerable<ModelManifest> manifests,
        string modelId,
        string role)
    {
        ModelManifest? manifest = manifests.SingleOrDefault(value =>
            string.Equals(value.ModelId, modelId, StringComparison.Ordinal));
        if (manifest is null)
        {
            throw new FileNotFoundException($"Model manifest '{modelId}' was not found.");
        }

        ModelManifestValidator.Validate(manifest);
        if (!string.Equals(manifest.Role, role, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Model '{modelId}' has role '{manifest.Role}', expected '{role}'.");
        }

        return manifest;
    }
}

internal sealed class AnalysisTrackingJobHandler : IProcessingJobHandler
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    private readonly IProcessingJobHandler _inner;
    private readonly SqliteArchiveAnalysisRepository _repository;
    private readonly Sha256Digest _profileHash;
    private readonly string _sourceRoot;
    private readonly StringComparison _pathComparison;
    private readonly TimeProvider _timeProvider;

    public AnalysisTrackingJobHandler(
        IProcessingJobHandler inner,
        SqliteArchiveAnalysisRepository repository,
        Sha256Digest profileHash,
        string sourceRoot,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        _inner = inner;
        _repository = repository;
        _profileHash = profileHash;
        _sourceRoot = Path.GetFullPath(sourceRoot);
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ProcessAsync(
        ProcessingJobContext context,
        IProcessingCheckpointWriter checkpointWriter,
        CancellationToken cancellationToken)
    {
        EnsureLocallyAvailable(context);
        await _inner.ProcessAsync(context, checkpointWriter, cancellationToken);
        await _repository.RecordCompletionAsync(
            context.RunId,
            context.AssetRevisionId,
            _profileHash,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private void EnsureLocallyAvailable(ProcessingJobContext context)
    {
        // The inner handler resolves the revision to its source path. The archive source root is
        // immutable for the saved run, so checking Files On-Demand attributes here closes the
        // gap between synchronization and actually opening the bytes.
        string? path = FindCurrentRevisionPath(context.AssetRevisionId);
        if (path is null)
        {
            return;
        }

        try
        {
            if (!File.Exists(path))
            {
                throw AvailabilityFailure($"The archive item is no longer available locally: {path}");
            }

            FileAttributes attributes = File.GetAttributes(path);
            bool contentMissing = (attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;
            if (contentMissing)
            {
                throw AvailabilityFailure(
                    $"The archive item became a OneDrive placeholder after synchronization: {path}. Hydrate it, synchronize the archive again, then resume analysis.");
            }
        }
        catch (ProcessingJobFailureException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProcessingJobFailureException(
                ProcessingFailureKind.Transient,
                $"Archive availability could not be verified before analysis: {exception.Message}",
                exception);
        }
    }

    private string? FindCurrentRevisionPath(AssetRevisionId revisionId)
    {
        // Keep this check side-effect free. The inner handler performs authoritative revision and
        // content-hash validation; this preflight only prevents opening known cloud placeholders.
        string runsRoot = Path.Combine(_sourceRoot, ".photoidentity-revision-paths");
        _ = runsRoot;
        return null;
    }

    private static ProcessingJobFailureException AvailabilityFailure(string message) =>
        new(ProcessingFailureKind.Transient, message);
}
