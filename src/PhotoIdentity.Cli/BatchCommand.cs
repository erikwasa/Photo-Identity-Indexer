using System.Globalization;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Cli;

internal enum BatchCommandAction
{
    Start,
    Resume,
    Status,
    Cancel,
}

internal sealed record BatchCommandOptions(
    BatchCommandAction Action,
    string DatabasePath,
    ProcessingRunId? RunId,
    string? SourceRoot,
    string? OutputRoot,
    string? RepositoryRoot,
    string? ModelDirectory,
    string DetectorModelId,
    string EmbedderModelId,
    bool Recursive,
    double ConfidenceThreshold,
    double PaddingRatio,
    string DetectorPipeline,
    int TileSize,
    double TileOverlap,
    double MergeNmsThreshold,
    int MaxAttemptsPerInvocation)
{
    public static BatchCommandOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("The batch command requires 'start', 'resume', 'status' or 'cancel'.");
        }

        BatchCommandAction action = args[0] switch
        {
            "start" => BatchCommandAction.Start,
            "resume" => BatchCommandAction.Resume,
            "status" => BatchCommandAction.Status,
            "cancel" => BatchCommandAction.Cancel,
            _ => throw new ArgumentException($"Unknown batch action '{args[0]}'."),
        };

        string? databasePath = null;
        ProcessingRunId? runId = null;
        string? sourceRoot = null;
        string? outputRoot = null;
        string? repositoryRoot = null;
        string? modelDirectory = null;
        string detectorModelId = LocalBatchConfiguration.DefaultDetectorModelId;
        string embedderModelId = LocalBatchConfiguration.DefaultEmbedderModelId;
        bool detectorModelSupplied = false;
        bool embedderModelSupplied = false;
        bool recursive = true;
        double confidence = 0.9;
        double padding = 0.25;
        string detectorPipeline = LocalBatchConfiguration.SinglePassDetectorPipeline;
        int tileSize = LocalBatchConfiguration.DefaultTileSize;
        double tileOverlap = LocalBatchConfiguration.DefaultTileOverlap;
        double mergeNmsThreshold = LocalBatchConfiguration.DefaultMergeNmsThreshold;
        int maxAttempts = int.MaxValue;

        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            if (option == "--non-recursive")
            {
                recursive = false;
                continue;
            }

            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--database":
                    databasePath = Single(databasePath, value, option);
                    break;
                case "--run":
                case "--run-id":
                    if (runId is not null)
                    {
                        throw new ArgumentException($"Option '{option}' may be supplied only once.");
                    }

                    if (!Guid.TryParse(value, out Guid parsedRunId) || parsedRunId == Guid.Empty)
                    {
                        throw new ArgumentException($"Option '{option}' requires a non-empty GUID.");
                    }

                    runId = ProcessingRunId.From(parsedRunId);
                    break;
                case "--source":
                case "--source-root":
                    sourceRoot = Single(sourceRoot, value, option);
                    break;
                case "--output":
                case "--output-root":
                    outputRoot = Single(outputRoot, value, option);
                    break;
                case "--root":
                    repositoryRoot = Single(repositoryRoot, value, option);
                    break;
                case "--model-dir":
                    modelDirectory = Single(modelDirectory, value, option);
                    break;
                case "--detector-model":
                    if (detectorModelSupplied)
                    {
                        throw new ArgumentException($"Option '{option}' may be supplied only once.");
                    }

                    detectorModelId = Required(value, option);
                    detectorModelSupplied = true;
                    break;
                case "--embedder-model":
                    if (embedderModelSupplied)
                    {
                        throw new ArgumentException($"Option '{option}' may be supplied only once.");
                    }

                    embedderModelId = Required(value, option);
                    embedderModelSupplied = true;
                    break;
                case "--confidence":
                    confidence = UnitInterval(value, option);
                    break;
                case "--padding":
                    padding = NonNegative(value, option);
                    break;
                case "--detector-pipeline":
                    detectorPipeline = ParseDetectorPipeline(value, option);
                    break;
                case "--tile-size":
                    tileSize = PositiveInteger(value, option);
                    break;
                case "--tile-overlap":
                    tileOverlap = UnitIntervalExclusiveUpper(value, option);
                    break;
                case "--merge-nms":
                    mergeNmsThreshold = UnitInterval(value, option);
                    break;
                case "--max-attempts":
                    maxAttempts = PositiveInteger(value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (databasePath is null)
        {
            throw new ArgumentException("Option '--database' is required.");
        }

        if (action == BatchCommandAction.Start)
        {
            if (sourceRoot is null)
            {
                throw new ArgumentException("Option '--source' is required for batch start.");
            }

            outputRoot ??= Path.Combine(".artifacts", "batch");
        }
        else if (runId is null)
        {
            throw new ArgumentException("Option '--run' is required.");
        }

        return new BatchCommandOptions(
            action,
            databasePath,
            runId,
            sourceRoot,
            outputRoot,
            repositoryRoot,
            modelDirectory,
            detectorModelId,
            embedderModelId,
            recursive,
            confidence,
            padding,
            detectorPipeline,
            tileSize,
            tileOverlap,
            mergeNmsThreshold,
            maxAttempts);
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        return Required(value, option);
    }

    private static string Required(string value, string option)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Option '{option}' requires a non-empty value.");
        }

        return value.Trim();
    }

    private static string ParseDetectorPipeline(string value, string option)
    {
        string parsed = Required(value, option).ToLowerInvariant();
        return parsed is LocalBatchConfiguration.SinglePassDetectorPipeline or
            LocalBatchConfiguration.MultiScaleDetectorPipeline
            ? parsed
            : throw new ArgumentException(
                $"Option '{option}' must be '{LocalBatchConfiguration.SinglePassDetectorPipeline}' or " +
                $"'{LocalBatchConfiguration.MultiScaleDetectorPipeline}'.");
    }

    private static double UnitInterval(string value, string option)
    {
        double parsed = Number(value, option);
        return parsed is >= 0 and <= 1
            ? parsed
            : throw new ArgumentException($"Option '{option}' must be between zero and one.");
    }

    private static double UnitIntervalExclusiveUpper(string value, string option)
    {
        double parsed = Number(value, option);
        return parsed is >= 0 and < 1
            ? parsed
            : throw new ArgumentException($"Option '{option}' must be at least zero and less than one.");
    }

    private static double NonNegative(string value, string option)
    {
        double parsed = Number(value, option);
        return parsed >= 0
            ? parsed
            : throw new ArgumentException($"Option '{option}' must be non-negative.");
    }

    private static double Number(string value, string option) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
        double.IsFinite(parsed)
            ? parsed
            : throw new ArgumentException($"Option '{option}' requires a finite number.");

    private static int PositiveInteger(string value, string option) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"Option '{option}' requires a positive integer.");
}

internal static class BatchCommandRunner
{
    public static async Task<int> RunAsync(
        BatchCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        SqliteCatalogueDatabase database = new(options.DatabasePath);
        await database.InitializeAsync(cancellationToken);
        SqliteProcessingRepository repository = new(database);

        switch (options.Action)
        {
            case BatchCommandAction.Start:
                return await StartAsync(options, database, output, cancellationToken);
            case BatchCommandAction.Resume:
                return await ResumeAsync(options, database, output, cancellationToken);
            case BatchCommandAction.Cancel:
                await repository.RequestCancellationAsync(
                    options.RunId!.Value,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                break;
            case BatchCommandAction.Status:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }

        ProcessingRunSummary summary = await repository.GetRunSummaryAsync(
            options.RunId!.Value,
            cancellationToken);
        WriteSummary(summary, output);
        return summary.FailedJobs == 0 ? 0 : 1;
    }

    private static async Task<int> StartAsync(
        BatchCommandOptions options,
        SqliteCatalogueDatabase database,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        string repositoryRoot = RepositoryRootLocator.Resolve(options.RepositoryRoot);
        LocalBatchConfiguration configuration = new(
            options.SourceRoot!,
            options.OutputRoot!,
            repositoryRoot,
            options.ModelDirectory,
            options.Recursive,
            options.ConfidenceThreshold,
            options.PaddingRatio,
            options.DetectorModelId,
            options.EmbedderModelId,
            options.DetectorPipeline,
            options.TileSize,
            options.TileOverlap,
            options.MergeNmsThreshold);
        using LocalInspectionJobHandler handler = await LocalInspectionJobHandler.CreateAsync(
            database,
            configuration,
            cancellationToken);
        LocalBatchCoordinator coordinator = new(database);
        LocalBatchStartResult result = await coordinator.StartAsync(
            configuration,
            handler,
            new ResumableBatchProcessorOptions(
                maxAttemptsPerInvocation: options.MaxAttemptsPerInvocation),
            cancellationToken);

        WriteConfiguration(configuration, output);
        output.WriteLine($"scan-supported: {result.ScanSummary.SupportedFileCount}");
        output.WriteLine($"scan-new-revisions: {result.ScanSummary.NewRevisionCount}");
        output.WriteLine($"scan-unchanged: {result.ScanSummary.UnchangedFileCount}");
        output.WriteLine($"scan-deleted: {result.ScanSummary.MarkedDeletedCount}");
        output.WriteLine($"scan-unsupported: {result.UnsupportedFileCount}");
        WriteSummary(result.ProcessingSummary, output);
        return result.ProcessingSummary.FailedJobs == 0 ? 0 : 1;
    }

    private static async Task<int> ResumeAsync(
        BatchCommandOptions options,
        SqliteCatalogueDatabase database,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        LocalBatchCoordinator coordinator = new(database);
        LocalBatchConfiguration configuration = await coordinator.GetConfigurationAsync(
            options.RunId!.Value,
            cancellationToken);
        using LocalInspectionJobHandler handler = await LocalInspectionJobHandler.CreateAsync(
            database,
            configuration,
            cancellationToken);
        ResumableBatchProcessorResult result = await coordinator.ResumeAsync(
            options.RunId.Value,
            handler,
            new ResumableBatchProcessorOptions(
                maxAttemptsPerInvocation: options.MaxAttemptsPerInvocation),
            cancellationToken);
        WriteConfiguration(configuration, output);
        WriteSummary(result.Summary, output);
        return result.Summary.FailedJobs == 0 ? 0 : 1;
    }

    private static void WriteConfiguration(LocalBatchConfiguration configuration, TextWriter output)
    {
        output.WriteLine($"detector-model: {configuration.DetectorModelId}");
        output.WriteLine($"embedder-model: {configuration.EmbedderModelId}");
        output.WriteLine($"detector-pipeline: {configuration.DetectorPipeline}");
        output.WriteLine($"confidence: {configuration.ConfidenceThreshold.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine($"padding: {configuration.PaddingRatio.ToString(CultureInfo.InvariantCulture)}");
        if (configuration.DetectorPipeline == LocalBatchConfiguration.MultiScaleDetectorPipeline)
        {
            output.WriteLine($"tile-size: {configuration.TileSize}");
            output.WriteLine($"tile-overlap: {configuration.TileOverlap.ToString(CultureInfo.InvariantCulture)}");
            output.WriteLine($"merge-nms: {configuration.MergeNmsThreshold.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static void WriteSummary(ProcessingRunSummary summary, TextWriter output)
    {
        output.WriteLine($"run: {summary.RunId}");
        output.WriteLine($"status: {summary.Status.ToString().ToLowerInvariant()}");
        output.WriteLine($"total: {summary.TotalJobs}");
        output.WriteLine($"queued: {summary.QueuedJobs}");
        output.WriteLine($"running: {summary.RunningJobs}");
        output.WriteLine($"succeeded: {summary.SucceededJobs}");
        output.WriteLine($"failed: {summary.FailedJobs}");
        output.WriteLine($"cancelled: {summary.CancelledJobs}");
        output.WriteLine($"attempts: {summary.AttemptCount}");
        if (summary.NextAvailableAtUtc is not null)
        {
            output.WriteLine($"next-available-utc: {summary.NextAvailableAtUtc:O}");
        }
    }
}
