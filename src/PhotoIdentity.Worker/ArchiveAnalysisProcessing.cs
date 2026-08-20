using System.Diagnostics;
using System.Runtime.CompilerServices;
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

public sealed record ArchiveAnalysisSessionDiagnostics(
    int SessionGeneration,
    int SessionInitializations,
    long AttemptsProcessed,
    double? LastInitializationMilliseconds,
    bool HasActiveSession);

/// <summary>
/// Runs the governed permanent-archive profile only for current, locally available revisions
/// that have not already completed that exact profile.
/// </summary>
public sealed class ArchiveAnalysisCoordinator
{
    private static readonly ConditionalWeakTable<SqliteCatalogueDatabase, SharedInspectionSession> SharedInspectionSessions = new();

    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;
    private readonly SharedInspectionSession _inspectionSession;

    public ArchiveAnalysisCoordinator(
        SqliteCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _inspectionSession = SharedInspectionSessions.GetValue(
            database,
            static value => new SharedInspectionSession(value));
    }

    public ArchiveAnalysisSessionDiagnostics GetSessionDiagnostics() => _inspectionSession.GetDiagnostics();

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

        using SharedInspectionSession.Lease lease = await _inspectionSession.AcquireAsync(
            batchConfiguration,
            cancellationToken);
        AnalysisTrackingJobHandler handler = new(
            _database,
            lease.Handler,
            analysisRepository,
            profileHash,
            _timeProvider);
        ResumableBatchProcessorResult processing = await new ResumableBatchProcessor(
                processingRepository,
                handler,
                _timeProvider)
            .RunUntilIdleAsync(runId, processorOptions, cancellationToken);
        lease.RecordAttempts(processing.AttemptsProcessed);

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

        using SharedInspectionSession.Lease lease = await _inspectionSession.AcquireAsync(
            batchConfiguration,
            cancellationToken);
        AnalysisTrackingJobHandler handler = new(
            _database,
            lease.Handler,
            analysisRepository,
            currentHash,
            _timeProvider);
        ResumableBatchProcessorResult processing = await new ResumableBatchProcessor(
                processingRepository,
                handler,
                _timeProvider)
            .RunUntilIdleAsync(runId, processorOptions, cancellationToken);
        lease.RecordAttempts(processing.AttemptsProcessed);
        return new ArchiveAnalysisResumeResult(profile, processing.Summary);
    }

    private sealed class SharedInspectionSession
    {
        private readonly SqliteCatalogueDatabase _database;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private LocalInspectionJobHandler? _handler;
        private string? _configurationJson;
        private int _sessionGeneration;
        private int _sessionInitializations;
        private long _attemptsProcessed;
        private double? _lastInitializationMilliseconds;

        public SharedInspectionSession(SqliteCatalogueDatabase database)
        {
            _database = database;
        }

        public async Task<Lease> AcquireAsync(
            LocalBatchConfiguration configuration,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                string configurationJson = configuration.ToJson();
                bool reused = _handler is not null &&
                    string.Equals(_configurationJson, configurationJson, StringComparison.Ordinal);
                if (!reused)
                {
                    _handler?.Dispose();
                    _handler = null;
                    _configurationJson = null;

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    LocalInspectionJobHandler created = await LocalInspectionJobHandler.CreateAsync(
                        _database,
                        configuration,
                        cancellationToken);
                    stopwatch.Stop();
                    _handler = created;
                    _configurationJson = configurationJson;
                    _sessionGeneration++;
                    _sessionInitializations++;
                    _lastInitializationMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                }

                return new Lease(this, _handler!, reused);
            }
            catch
            {
                _gate.Release();
                throw;
            }
        }

        public ArchiveAnalysisSessionDiagnostics GetDiagnostics() => new(
            _sessionGeneration,
            _sessionInitializations,
            Interlocked.Read(ref _attemptsProcessed),
            _lastInitializationMilliseconds,
            _handler is not null);

        private void Release() => _gate.Release();

        private void RecordAttempts(int attempts) => Interlocked.Add(ref _attemptsProcessed, attempts);

        public sealed class Lease : IDisposable
        {
            private readonly SharedInspectionSession _owner;
            private bool _disposed;

            public Lease(
                SharedInspectionSession owner,
                LocalInspectionJobHandler handler,
                bool reusedExistingSession)
            {
                _owner = owner;
                Handler = handler;
                ReusedExistingSession = reusedExistingSession;
            }

            public LocalInspectionJobHandler Handler { get; }
            public bool ReusedExistingSession { get; }

            public void RecordAttempts(int attempts)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                ArgumentOutOfRangeException.ThrowIfNegative(attempts);
                _owner.RecordAttempts(attempts);
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _owner.Release();
            }
        }
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
    private readonly SqliteLocalBatchRepository _assetRepository;
    private readonly SqliteArchiveAnalysisRepository _repository;
    private readonly Sha256Digest _profileHash;
    private readonly TimeProvider _timeProvider;

    public AnalysisTrackingJobHandler(
        SqliteCatalogueDatabase database,
        IProcessingJobHandler inner,
        SqliteArchiveAnalysisRepository repository,
        Sha256Digest profileHash,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(repository);
        _inner = inner;
        _assetRepository = new SqliteLocalBatchRepository(database);
        _repository = repository;
        _profileHash = profileHash;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ProcessAsync(
        ProcessingJobContext context,
        IProcessingCheckpointWriter checkpointWriter,
        CancellationToken cancellationToken)
    {
        await EnsureLocallyAvailableAsync(context.AssetRevisionId, cancellationToken);
        await _inner.ProcessAsync(context, checkpointWriter, cancellationToken);
        await _repository.RecordCompletionAsync(
            context.RunId,
            context.AssetRevisionId,
            _profileHash,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task EnsureLocallyAvailableAsync(
        AssetRevisionId revisionId,
        CancellationToken cancellationToken)
    {
        CatalogueProcessingAssetRevision asset = await _assetRepository.GetAssetRevisionAsync(
            revisionId,
            cancellationToken)
            ?? throw new ProcessingJobFailureException(
                ProcessingFailureKind.Permanent,
                $"Asset revision {revisionId} was not found before archive analysis.");
        if (!string.Equals(asset.SourceKind, "local-folder", StringComparison.Ordinal))
        {
            return;
        }

        string path = ResolveSourcePath(asset.RootLocator, asset.SourceKey);
        try
        {
            if (!File.Exists(path))
            {
                throw AvailabilityFailure(
                    $"The archive item is no longer available locally: {asset.SourceKey}. Synchronize the archive before retrying.");
            }

            FileAttributes attributes = File.GetAttributes(path);
            bool contentMissing = (attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0;
            if (contentMissing)
            {
                throw AvailabilityFailure(
                    $"The archive item '{asset.SourceKey}' became a OneDrive placeholder after synchronization. Hydrate it with the OneDrive sync client, synchronize the archive again, then resume analysis.");
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
                $"Archive availability could not be verified for '{asset.SourceKey}' before analysis: {exception.Message}",
                exception);
        }
    }

    private static string ResolveSourcePath(string rootLocator, string sourceKey)
    {
        string root = Path.GetFullPath(rootLocator);
        string platformPath = sourceKey
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.Combine(root, platformPath));
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.Equals(root, comparison) && !resolved.StartsWith(rootPrefix, comparison))
        {
            throw new ProcessingJobFailureException(
                ProcessingFailureKind.Permanent,
                $"Archive source key '{sourceKey}' escapes the configured source root.");
        }

        return resolved;
    }

    private static ProcessingJobFailureException AvailabilityFailure(string message) =>
        new(ProcessingFailureKind.Transient, message);
}
