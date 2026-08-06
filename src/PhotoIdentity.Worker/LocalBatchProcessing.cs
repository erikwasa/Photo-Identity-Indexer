using System.Text.Json;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;

namespace PhotoIdentity.Worker;

public sealed record LocalBatchConfiguration
{
    public const string DefaultDetectorModelId = "yunet-2023mar-fp32";
    public const string DefaultEmbedderModelId = "sface-2021dec-fp32";
    public const string SinglePassDetectorPipeline = "single-pass";
    public const string MultiScaleDetectorPipeline = "full-image-plus-tiles";
    public const int DefaultTileSize = 1024;
    public const double DefaultTileOverlap = 0.2;
    public const double DefaultMergeNmsThreshold = 0.3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public LocalBatchConfiguration(
        string sourceRoot,
        string outputRoot,
        string repositoryRoot,
        string? modelDirectory = null,
        bool recursive = true,
        double confidenceThreshold = 0.9,
        double paddingRatio = 0.25,
        string detectorModelId = DefaultDetectorModelId,
        string embedderModelId = DefaultEmbedderModelId,
        string detectorPipeline = SinglePassDetectorPipeline,
        int tileSize = DefaultTileSize,
        double tileOverlap = DefaultTileOverlap,
        double mergeNmsThreshold = DefaultMergeNmsThreshold)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(detectorModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(embedderModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(detectorPipeline);
        if (!double.IsFinite(confidenceThreshold) || confidenceThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidenceThreshold),
                "The confidence threshold must be between zero and one.");
        }

        if (!double.IsFinite(paddingRatio) || paddingRatio < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingRatio), "The padding ratio must be non-negative.");
        }

        string canonicalPipeline = detectorPipeline.Trim().ToLowerInvariant();
        if (canonicalPipeline is not (SinglePassDetectorPipeline or MultiScaleDetectorPipeline))
        {
            throw new ArgumentOutOfRangeException(
                nameof(detectorPipeline),
                $"The detector pipeline must be '{SinglePassDetectorPipeline}' or '{MultiScaleDetectorPipeline}'.");
        }

        if (tileSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileSize), "The tile size must be positive.");
        }

        if (!double.IsFinite(tileOverlap) || tileOverlap < 0 || tileOverlap >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileOverlap),
                "The tile overlap must be at least zero and less than one.");
        }

        if (!double.IsFinite(mergeNmsThreshold) || mergeNmsThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mergeNmsThreshold),
                "The merge NMS threshold must be between zero and one.");
        }

        SourceRoot = Path.GetFullPath(sourceRoot);
        OutputRoot = Path.GetFullPath(outputRoot);
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        EnsureOutsideSource(SourceRoot, OutputRoot);
        ModelDirectory = modelDirectory is null
            ? Path.Combine(RepositoryRoot, "models", "files")
            : Path.GetFullPath(modelDirectory);
        Recursive = recursive;
        ConfidenceThreshold = confidenceThreshold;
        PaddingRatio = paddingRatio;
        DetectorModelId = detectorModelId.Trim();
        EmbedderModelId = embedderModelId.Trim();
        DetectorPipeline = canonicalPipeline;
        TileSize = tileSize;
        TileOverlap = tileOverlap;
        MergeNmsThreshold = mergeNmsThreshold;
    }

    public string SourceRoot { get; }
    public string OutputRoot { get; }
    public string RepositoryRoot { get; }
    public string ModelDirectory { get; }
    public bool Recursive { get; }
    public double ConfidenceThreshold { get; }
    public double PaddingRatio { get; }
    public string DetectorModelId { get; }
    public string EmbedderModelId { get; }
    public string DetectorPipeline { get; }
    public int TileSize { get; }
    public double TileOverlap { get; }
    public double MergeNmsThreshold { get; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static LocalBatchConfiguration FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        LocalBatchConfigurationData? data = JsonSerializer.Deserialize<LocalBatchConfigurationData>(json, JsonOptions);
        return data is null
            ? throw new InvalidDataException("The processing run configuration is empty.")
            : new LocalBatchConfiguration(
                data.SourceRoot,
                data.OutputRoot,
                data.RepositoryRoot,
                data.ModelDirectory,
                data.Recursive,
                data.ConfidenceThreshold,
                data.PaddingRatio,
                data.DetectorModelId ?? DefaultDetectorModelId,
                data.EmbedderModelId ?? DefaultEmbedderModelId,
                data.DetectorPipeline ?? SinglePassDetectorPipeline,
                data.TileSize ?? DefaultTileSize,
                data.TileOverlap ?? DefaultTileOverlap,
                data.MergeNmsThreshold ?? DefaultMergeNmsThreshold);
    }

    private static void EnsureOutsideSource(string sourceRoot, string outputRoot)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string sourcePrefix = sourceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? sourceRoot
            : sourceRoot + Path.DirectorySeparatorChar;
        if (outputRoot.Equals(sourceRoot, comparison) || outputRoot.StartsWith(sourcePrefix, comparison))
        {
            throw new ArgumentException(
                "The batch output directory must be outside the source root so generated PNG files are not catalogued as input.",
                nameof(outputRoot));
        }
    }

    private sealed record LocalBatchConfigurationData(
        string SourceRoot,
        string OutputRoot,
        string RepositoryRoot,
        string? ModelDirectory,
        bool Recursive,
        double ConfidenceThreshold,
        double PaddingRatio,
        string? DetectorModelId,
        string? EmbedderModelId,
        string? DetectorPipeline,
        int? TileSize,
        double? TileOverlap,
        double? MergeNmsThreshold);
}

public sealed record LocalBatchStartResult(
    ProcessingRunId RunId,
    SourceCatalogueScanSummary ScanSummary,
    int UnsupportedFileCount,
    ProcessingRunSummary ProcessingSummary);

/// <summary>
/// Creates and resumes durable local-folder runs without depending on a concrete model pipeline.
/// </summary>
public sealed class LocalBatchCoordinator
{
    private readonly SqliteCatalogueDatabase _database;
    private readonly TimeProvider _timeProvider;

    public LocalBatchCoordinator(
        SqliteCatalogueDatabase database,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<LocalBatchStartResult> StartAsync(
        LocalBatchConfiguration configuration,
        IProcessingJobHandler handler,
        ResumableBatchProcessorOptions? processorOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(handler);

        await _database.InitializeAsync(cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        SqliteLocalBatchRepository batchRepository = new(_database);
        CatalogueSource sourceRecord = await batchRepository.GetOrCreateLocalFolderSourceAsync(
            configuration.SourceRoot,
            now,
            cancellationToken);
        LocalFolderAssetSource source = new(sourceRecord.Id, configuration.SourceRoot);
        SourceScanOptions scanOptions = new(Recursive: configuration.Recursive);
        LocalFolderScanReport sourceReport = await source.ScanAsync(scanOptions, cancellationToken);
        SqliteSourceCatalogueScanner scanner = new(_database);
        SourceCatalogueScanSummary scanSummary = await scanner.ScanAsync(
            source,
            sourceRecord,
            scanOptions,
            now,
            cancellationToken);

        IReadOnlyList<AssetRevisionId> revisionIds = await batchRepository.GetCurrentRevisionIdsAsync(
            sourceRecord.Id,
            cancellationToken);
        ProcessingRunId runId = ProcessingRunId.New();
        CatalogueProcessingRun run = new(
            runId,
            ProcessingRunStatus.Pending,
            configuration.ToJson(),
            now);
        CatalogueProcessingJob[] jobs = revisionIds
            .Select(revisionId => new CatalogueProcessingJob(
                ProcessingJobId.New(),
                runId,
                revisionId,
                ProcessingJobStatus.Queued,
                attemptCount: 0,
                availableAtUtc: now,
                idempotencyKey: $"local-inspect:{runId}:{revisionId}"))
            .ToArray();

        SqliteProcessingRepository processingRepository = new(_database);
        await processingRepository.CreateRunAsync(run, jobs, cancellationToken);
        ResumableBatchProcessor processor = new(processingRepository, handler, _timeProvider);
        ResumableBatchProcessorResult result = await processor.RunUntilIdleAsync(
            runId,
            processorOptions,
            cancellationToken);

        return new LocalBatchStartResult(
            runId,
            scanSummary,
            sourceReport.UnsupportedFiles.Count,
            result.Summary);
    }

    public async Task<ResumableBatchProcessorResult> ResumeAsync(
        ProcessingRunId runId,
        IProcessingJobHandler handler,
        ResumableBatchProcessorOptions? processorOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        await _database.InitializeAsync(cancellationToken);
        SqliteProcessingRepository repository = new(_database);
        _ = await repository.GetRunAsync(runId, cancellationToken)
            ?? throw new KeyNotFoundException($"Processing run {runId} was not found.");
        ResumableBatchProcessor processor = new(repository, handler, _timeProvider);
        return await processor.RunUntilIdleAsync(
            runId,
            processorOptions,
            cancellationToken);
    }

    public async Task<LocalBatchConfiguration> GetConfigurationAsync(
        ProcessingRunId runId,
        CancellationToken cancellationToken = default)
    {
        await _database.InitializeAsync(cancellationToken);
        CatalogueProcessingRun run = await new SqliteProcessingRepository(_database)
            .GetRunAsync(runId, cancellationToken)
            ?? throw new KeyNotFoundException($"Processing run {runId} was not found.");
        return LocalBatchConfiguration.FromJson(run.ConfigurationJson);
    }
}
