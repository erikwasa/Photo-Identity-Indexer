using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Cli;

internal enum ArchiveCommandAction
{
    Include,
    List,
    Sync,
    Analyze,
    Resume,
    Status,
}

internal sealed record ArchiveCommandOptions(
    ArchiveCommandAction Action,
    string DatabasePath,
    string? RootPath,
    string? RelativeFolder,
    string? OutputRoot,
    string? RepositoryRoot,
    string? ModelDirectory,
    ProcessingRunId? RunId,
    int MaxAttemptsPerInvocation)
{
    public static ArchiveCommandOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException(
                "The archive command requires 'include', 'list', 'sync', 'analyze', 'resume' or 'status'.");
        }

        ArchiveCommandAction action = args[0] switch
        {
            "include" => ArchiveCommandAction.Include,
            "list" => ArchiveCommandAction.List,
            "sync" => ArchiveCommandAction.Sync,
            "analyze" => ArchiveCommandAction.Analyze,
            "resume" => ArchiveCommandAction.Resume,
            "status" => ArchiveCommandAction.Status,
            _ => throw new ArgumentException($"Unknown archive action '{args[0]}'."),
        };

        string? databasePath = null;
        string? rootPath = null;
        string? relativeFolder = null;
        string? outputRoot = null;
        string? repositoryRoot = null;
        string? modelDirectory = null;
        ProcessingRunId? runId = null;
        int maxAttempts = int.MaxValue;

        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--database":
                    databasePath = Single(databasePath, value, option);
                    break;
                case "--root":
                    rootPath = Single(rootPath, value, option);
                    break;
                case "--folder":
                    relativeFolder = Single(relativeFolder, value, option, allowDot: true);
                    break;
                case "--output":
                case "--output-root":
                    outputRoot = Single(outputRoot, value, option);
                    break;
                case "--repository-root":
                    repositoryRoot = Single(repositoryRoot, value, option);
                    break;
                case "--model-dir":
                    modelDirectory = Single(modelDirectory, value, option);
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
                case "--max-attempts":
                    if (!int.TryParse(value, out maxAttempts) || maxAttempts <= 0)
                    {
                        throw new ArgumentException($"Option '{option}' requires a positive integer.");
                    }
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (databasePath is null)
        {
            throw new ArgumentException("Option '--database' is required.");
        }

        switch (action)
        {
            case ArchiveCommandAction.Include:
                if (rootPath is null)
                {
                    throw new ArgumentException("Option '--root' is required for archive include.");
                }

                if (relativeFolder is null)
                {
                    throw new ArgumentException("Option '--folder' is required for archive include.");
                }
                RejectAnalysisOptions(outputRoot, repositoryRoot, modelDirectory, runId, maxAttempts);
                break;
            case ArchiveCommandAction.List:
            case ArchiveCommandAction.Sync:
                if (rootPath is not null || relativeFolder is not null || outputRoot is not null ||
                    repositoryRoot is not null || modelDirectory is not null || runId is not null ||
                    maxAttempts != int.MaxValue)
                {
                    throw new ArgumentException(
                        $"Archive {args[0]} accepts only '--database'.");
                }
                break;
            case ArchiveCommandAction.Analyze:
                if (outputRoot is null)
                {
                    throw new ArgumentException("Option '--output' is required for archive analyze.");
                }
                if (rootPath is not null || relativeFolder is not null || runId is not null)
                {
                    throw new ArgumentException(
                        "Options '--root', '--folder' and '--run' are not valid for archive analyze.");
                }
                break;
            case ArchiveCommandAction.Resume:
                if (runId is null)
                {
                    throw new ArgumentException("Option '--run' is required for archive resume.");
                }
                if (rootPath is not null || relativeFolder is not null || outputRoot is not null ||
                    repositoryRoot is not null || modelDirectory is not null)
                {
                    throw new ArgumentException(
                        "Archive resume reconstructs its saved configuration; only '--database', '--run' and '--max-attempts' are valid.");
                }
                break;
            case ArchiveCommandAction.Status:
                if (runId is null)
                {
                    throw new ArgumentException("Option '--run' is required for archive status.");
                }
                if (rootPath is not null || relativeFolder is not null || outputRoot is not null ||
                    repositoryRoot is not null || modelDirectory is not null || maxAttempts != int.MaxValue)
                {
                    throw new ArgumentException(
                        "Archive status accepts only '--database' and '--run'.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }

        return new ArchiveCommandOptions(
            action,
            databasePath,
            rootPath,
            relativeFolder,
            outputRoot,
            repositoryRoot,
            modelDirectory,
            runId,
            maxAttempts);
    }

    private static void RejectAnalysisOptions(
        string? outputRoot,
        string? repositoryRoot,
        string? modelDirectory,
        ProcessingRunId? runId,
        int maxAttempts)
    {
        if (outputRoot is not null || repositoryRoot is not null || modelDirectory is not null ||
            runId is not null || maxAttempts != int.MaxValue)
        {
            throw new ArgumentException(
                "Archive include accepts only '--database', '--root' and '--folder'.");
        }
    }

    private static string Single(
        string? current,
        string value,
        string option,
        bool allowDot = false)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        string trimmed = value.Trim();
        if (trimmed.Length == 0 || (!allowDot && trimmed == "."))
        {
            throw new ArgumentException($"Option '{option}' requires a non-empty value.");
        }

        return trimmed;
    }
}

internal static class ArchiveCommandRunner
{
    public static async Task<int> RunAsync(
        ArchiveCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        SqliteCatalogueDatabase database = new(options.DatabasePath);
        await database.InitializeAsync(cancellationToken);
        SqliteArchiveCoverageRepository coverage = new(database);

        return options.Action switch
        {
            ArchiveCommandAction.Include => await IncludeAsync(options, database, coverage, output, cancellationToken),
            ArchiveCommandAction.List => await ListAsync(coverage, output, cancellationToken),
            ArchiveCommandAction.Sync => await SyncAsync(database, coverage, output, cancellationToken),
            ArchiveCommandAction.Analyze => await AnalyzeAsync(options, database, output, cancellationToken),
            ArchiveCommandAction.Resume => await ResumeAsync(options, database, output, cancellationToken),
            ArchiveCommandAction.Status => await StatusAsync(options, database, output, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
    }

    private static async Task<int> IncludeAsync(
        ArchiveCommandOptions options,
        SqliteCatalogueDatabase database,
        SqliteArchiveCoverageRepository coverage,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        string requestedRoot = Path.GetFullPath(options.RootPath!);
        ArchiveCoverageConfiguration? existing = await coverage.GetAsync(cancellationToken);
        CatalogueSource source;

        if (existing is not null)
        {
            if (!PathsEqual(existing.Source.RootLocator, requestedRoot))
            {
                throw new ArgumentException(
                    $"This catalogue is already configured for archive root '{existing.Source.RootLocator}'.");
            }

            source = existing.Source;
        }
        else
        {
            source = await new SqliteLocalBatchRepository(database).GetOrCreateLocalFolderSourceAsync(
                requestedRoot,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }

        ArchiveCoverageConfiguration configured = await coverage.ConfigureAndIncludeAsync(
            source,
            options.RelativeFolder!,
            DateTimeOffset.UtcNow,
            cancellationToken);
        WriteConfiguration(configured, output);
        return 0;
    }

    private static async Task<int> ListAsync(
        SqliteArchiveCoverageRepository coverage,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArchiveCoverageConfiguration? configured = await coverage.GetAsync(cancellationToken);
        if (configured is null)
        {
            output.WriteLine("archive-configured: false");
            return 0;
        }

        WriteConfiguration(configured, output);
        return 0;
    }

    private static async Task<int> SyncAsync(
        SqliteCatalogueDatabase database,
        SqliteArchiveCoverageRepository coverage,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArchiveCoverageConfiguration? configured = await coverage.GetAsync(cancellationToken);
        if (configured is null)
        {
            output.WriteLine("archive-configured: false");
            return 1;
        }

        LocalFolderAssetSource source = new(configured.Source.Id, configured.Source.RootLocator);
        LocalArchiveSyncCoordinator coordinator = new(database);
        LocalArchiveSyncSummary summary = await coordinator.SyncAsync(
            source,
            configured.Source,
            configured.IncludedFolders,
            DateTimeOffset.UtcNow,
            cancellationToken);

        WriteConfiguration(configured, output);
        output.WriteLine($"scan-supported: {summary.SupportedFileCount}");
        output.WriteLine($"scan-new-revisions: {summary.NewRevisionCount}");
        output.WriteLine($"scan-unchanged: {summary.UnchangedFileCount}");
        output.WriteLine($"scan-deleted: {summary.MarkedDeletedCount}");
        return 0;
    }

    private static async Task<int> AnalyzeAsync(
        ArchiveCommandOptions options,
        SqliteCatalogueDatabase database,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArchiveAnalysisConfiguration configuration = new(
            options.OutputRoot!,
            RepositoryRootLocator.Resolve(options.RepositoryRoot),
            options.ModelDirectory);
        ArchiveAnalysisStartResult result = await new ArchiveAnalysisCoordinator(database).StartAsync(
            configuration,
            new ResumableBatchProcessorOptions(
                maxAttemptsPerInvocation: options.MaxAttemptsPerInvocation),
            cancellationToken);

        WriteProfile(result.Profile, output);
        output.WriteLine($"current-revisions: {result.CurrentRevisionCount}");
        output.WriteLine($"already-analyzed: {result.PreviouslyCompletedCount}");
        output.WriteLine($"scheduled: {result.ProcessingSummary?.TotalJobs ?? 0}");
        if (result.ProcessingSummary is null)
        {
            output.WriteLine("status: up-to-date");
            return 0;
        }

        WriteProcessingSummary(result.ProcessingSummary, output);
        return result.ProcessingSummary.FailedJobs == 0 ? 0 : 1;
    }

    private static async Task<int> ResumeAsync(
        ArchiveCommandOptions options,
        SqliteCatalogueDatabase database,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArchiveAnalysisResumeResult result = await new ArchiveAnalysisCoordinator(database).ResumeAsync(
            options.RunId!.Value,
            new ResumableBatchProcessorOptions(
                maxAttemptsPerInvocation: options.MaxAttemptsPerInvocation),
            cancellationToken);
        WriteProfile(result.Profile, output);
        WriteProcessingSummary(result.ProcessingSummary, output);
        return result.ProcessingSummary.FailedJobs == 0 ? 0 : 1;
    }

    private static async Task<int> StatusAsync(
        ArchiveCommandOptions options,
        SqliteCatalogueDatabase database,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        Sha256Digest profileHash = await new SqliteArchiveAnalysisRepository(database)
            .GetRunProfileHashAsync(options.RunId!.Value, cancellationToken);
        ProcessingRunSummary summary = await new SqliteProcessingRepository(database)
            .GetRunSummaryAsync(options.RunId.Value, cancellationToken);
        output.WriteLine($"analysis-profile: {profileHash}");
        WriteProcessingSummary(summary, output);
        return summary.FailedJobs == 0 ? 0 : 1;
    }

    private static void WriteProfile(AnalysisProfileDefinition profile, TextWriter output)
    {
        output.WriteLine($"analysis-profile: {profile.ComputeHash()}");
        output.WriteLine($"detector-pipeline: {profile.DetectorPipelineHash}");
        output.WriteLine($"detector-model: {profile.DetectorModelId}");
        output.WriteLine($"detector-model-hash: {profile.DetectorModelHash}");
        output.WriteLine($"embedder-model: {profile.EmbedderModelId}");
        output.WriteLine($"embedder-model-hash: {profile.EmbedderModelHash}");
        output.WriteLine($"alignment-protocol: {profile.AlignmentProtocol}");
    }

    private static void WriteProcessingSummary(ProcessingRunSummary summary, TextWriter output)
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
    }

    private static void WriteConfiguration(ArchiveCoverageConfiguration configured, TextWriter output)
    {
        output.WriteLine("archive-configured: true");
        output.WriteLine($"archive-root: {configured.Source.RootLocator}");
        output.WriteLine($"included-folders: {configured.IncludedFolders.Count}");
        foreach (string folder in configured.IncludedFolders)
        {
            output.WriteLine($"included: {(folder.Length == 0 ? "." : folder)}");
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}
