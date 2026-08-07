using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Processing;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Cli;

internal enum DetectorRolloutCommandAction
{
    Start,
    Resume,
    Status,
    Apply,
}

internal sealed record DetectorRolloutCommandOptions(
    DetectorRolloutCommandAction Action,
    string DatabasePath,
    ProcessingRunId? RunId,
    IReadOnlyList<AssetRevisionId> RevisionIds,
    string? RevisionFile,
    string? OutputRoot,
    string? RepositoryRoot,
    string? ModelDirectory,
    int MaxAttemptsPerInvocation)
{
    public static DetectorRolloutCommandOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("The rollout command requires 'start', 'resume', 'status' or 'apply'.");
        }

        DetectorRolloutCommandAction action = args[0] switch
        {
            "start" => DetectorRolloutCommandAction.Start,
            "resume" => DetectorRolloutCommandAction.Resume,
            "status" => DetectorRolloutCommandAction.Status,
            "apply" => DetectorRolloutCommandAction.Apply,
            _ => throw new ArgumentException($"Unknown rollout action '{args[0]}'."),
        };

        string? databasePath = null;
        ProcessingRunId? runId = null;
        List<AssetRevisionId> revisions = [];
        string? revisionFile = null;
        string? outputRoot = null;
        string? repositoryRoot = null;
        string? modelDirectory = null;
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
                case "--run":
                case "--run-id":
                    if (runId is not null)
                    {
                        throw new ArgumentException($"Option '{option}' may be supplied only once.");
                    }

                    runId = ParseProcessingRunId(value, option);
                    break;
                case "--revision":
                    revisions.Add(ParseAssetRevisionId(value, option));
                    break;
                case "--revision-file":
                    revisionFile = Single(revisionFile, value, option);
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

        if (action == DetectorRolloutCommandAction.Start)
        {
            if (runId is not null)
            {
                throw new ArgumentException("Option '--run' is not valid for rollout start.");
            }

            if (outputRoot is null)
            {
                throw new ArgumentException("Option '--output' is required for rollout start.");
            }

            if (revisions.Count == 0 && revisionFile is null)
            {
                throw new ArgumentException(
                    "Rollout start requires at least one '--revision' or one '--revision-file'.");
            }
        }
        else if (runId is null)
        {
            throw new ArgumentException("Option '--run' is required for this rollout action.");
        }

        return new DetectorRolloutCommandOptions(
            action,
            databasePath,
            runId,
            revisions,
            revisionFile,
            outputRoot,
            repositoryRoot,
            modelDirectory,
            maxAttempts);
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Option '{option}' requires a non-empty value.")
            : value.Trim();
    }

    private static ProcessingRunId ParseProcessingRunId(string value, string option) =>
        Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
            ? ProcessingRunId.From(parsed)
            : throw new ArgumentException($"Option '{option}' requires a non-empty GUID.");

    private static AssetRevisionId ParseAssetRevisionId(string value, string option) =>
        Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
            ? AssetRevisionId.From(parsed)
            : throw new ArgumentException($"Option '{option}' requires a non-empty asset-revision GUID.");
}

internal static class DetectorRolloutCommandRunner
{
    public static async Task<int> RunAsync(
        DetectorRolloutCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        SqliteCatalogueDatabase database = new(options.DatabasePath);
        await database.InitializeAsync(cancellationToken);
        switch (options.Action)
        {
            case DetectorRolloutCommandAction.Start:
                return await StartAsync(options, database, output, cancellationToken);
            case DetectorRolloutCommandAction.Resume:
                return await ResumeAsync(options, database, output, cancellationToken);
            case DetectorRolloutCommandAction.Status:
                await WriteStatusAsync(database, options.RunId!.Value, output, cancellationToken);
                return 0;
            case DetectorRolloutCommandAction.Apply:
                CatalogueDetectorRolloutApplyResult apply = await new SqliteDetectorRolloutApplicationRepository(database)
                    .ApplyResolvedAsync(options.RunId!.Value, cancellationToken);
                output.WriteLine($"reviewed-considered: {apply.ConsideredCount}");
                output.WriteLine($"reviewed-applied: {apply.AppliedCount}");
                output.WriteLine($"reviewed-deferred: {apply.DeferredCount}");
                output.WriteLine($"reviewed-awaiting: {apply.AwaitingReviewCount}");
                await WriteStatusAsync(database, options.RunId.Value, output, cancellationToken);
                return 0;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Action));
        }
    }

    private static async Task<int> StartAsync(
        DetectorRolloutCommandOptions options,
        SqliteCatalogueDatabase database,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        List<AssetRevisionId> revisions = [.. options.RevisionIds];
        if (options.RevisionFile is not null)
        {
            revisions.AddRange(await ReadRevisionFileAsync(options.RevisionFile, cancellationToken));
        }

        string repositoryRoot = RepositoryRootLocator.Resolve(options.RepositoryRoot);
        DetectorRolloutConfiguration configuration = new(
            options.OutputRoot!,
            repositoryRoot,
            options.ModelDirectory);
        DetectorRolloutStartResult result = await new DetectorRolloutCoordinator(database).StartAsync(
            configuration,
            revisions,
            new ResumableBatchProcessorOptions(maxAttemptsPerInvocation: options.MaxAttemptsPerInvocation),
            cancellationToken);
        WriteSelectedPipeline(output);
        WriteProcessingSummary(result.ProcessingSummary, output);
        WriteRolloutSummary(result.RolloutSummary, output);
        output.WriteLine($"review-path: /detector-rollout/{result.RunId}");
        return result.ProcessingSummary.FailedJobs == 0 ? 0 : 1;
    }

    private static async Task<int> ResumeAsync(
        DetectorRolloutCommandOptions options,
        SqliteCatalogueDatabase database,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        DetectorRolloutResumeResult result = await new DetectorRolloutCoordinator(database).ResumeAsync(
            options.RunId!.Value,
            new ResumableBatchProcessorOptions(maxAttemptsPerInvocation: options.MaxAttemptsPerInvocation),
            cancellationToken);
        WriteSelectedPipeline(output);
        WriteProcessingSummary(result.ProcessingSummary, output);
        WriteRolloutSummary(result.RolloutSummary, output);
        output.WriteLine($"review-path: /detector-rollout/{options.RunId.Value}");
        return result.ProcessingSummary.FailedJobs == 0 ? 0 : 1;
    }

    private static async Task WriteStatusAsync(
        SqliteCatalogueDatabase database,
        ProcessingRunId runId,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ProcessingRunSummary processing = await new SqliteProcessingRepository(database)
            .GetRunSummaryAsync(runId, cancellationToken);
        CatalogueDetectorRolloutSummary rollout = await new SqliteDetectorRolloutApplicationRepository(database)
            .GetSummaryAsync(runId, cancellationToken);
        WriteProcessingSummary(processing, output);
        WriteRolloutSummary(rollout, output);
        output.WriteLine($"review-path: /detector-rollout/{runId}");
    }

    private static async Task<IReadOnlyList<AssetRevisionId>> ReadRevisionFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The rollout revision file was not found.", fullPath);
        }

        string[] lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);
        List<AssetRevisionId> revisions = [];
        for (int index = 0; index < lines.Length; index++)
        {
            string value = lines[index].Trim();
            if (value.Length == 0 || value.StartsWith('#'))
            {
                continue;
            }

            if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty)
            {
                throw new InvalidDataException(
                    $"Rollout revision file line {index + 1} is not a non-empty GUID: '{value}'.");
            }

            revisions.Add(AssetRevisionId.From(parsed));
        }

        return revisions;
    }

    private static void WriteSelectedPipeline(TextWriter output)
    {
        output.WriteLine($"detector-model: {DetectorRolloutConfiguration.DetectorModelId}");
        output.WriteLine($"embedder-model: {DetectorRolloutConfiguration.EmbedderModelId}");
        output.WriteLine($"detector-pipeline: {DetectorRolloutConfiguration.DetectorPipeline}");
        output.WriteLine($"confidence: {DetectorRolloutConfiguration.ConfidenceThreshold}");
    }

    private static void WriteProcessingSummary(ProcessingRunSummary summary, TextWriter output)
    {
        output.WriteLine($"run: {summary.RunId}");
        output.WriteLine($"processing-status: {summary.Status.ToString().ToLowerInvariant()}");
        output.WriteLine($"revisions-total: {summary.TotalJobs}");
        output.WriteLine($"revisions-succeeded: {summary.SucceededJobs}");
        output.WriteLine($"revisions-failed: {summary.FailedJobs}");
    }

    private static void WriteRolloutSummary(CatalogueDetectorRolloutSummary summary, TextWriter output)
    {
        output.WriteLine($"plans: {summary.RevisionCount}");
        output.WriteLine($"candidates: {summary.CandidateCount}");
        output.WriteLine($"applied: {summary.AppliedCount}");
        output.WriteLine($"ambiguous: {summary.AmbiguousCount}");
        output.WriteLine($"awaiting-review: {summary.AwaitingReviewCount}");
        output.WriteLine($"ready-to-apply: {summary.ReadyToApplyCount}");
        output.WriteLine($"deferred: {summary.DeferredCount}");
        output.WriteLine($"unmatched-existing: {summary.UnmatchedExistingCount}");
        bool complete = summary.CandidateCount == summary.AppliedCount &&
                        summary.AwaitingReviewCount == 0 &&
                        summary.ReadyToApplyCount == 0 &&
                        summary.DeferredCount == 0;
        output.WriteLine($"rollout-complete: {complete.ToString().ToLowerInvariant()}");
    }
}
