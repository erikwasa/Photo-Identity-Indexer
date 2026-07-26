using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Cli;

internal enum BatchCommandAction
{
    Status,
    Cancel,
}

internal sealed record BatchCommandOptions(
    BatchCommandAction Action,
    string DatabasePath,
    ProcessingRunId RunId)
{
    public static BatchCommandOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("The batch command requires 'status' or 'cancel'.");
        }

        BatchCommandAction action = args[0] switch
        {
            "status" => BatchCommandAction.Status,
            "cancel" => BatchCommandAction.Cancel,
            _ => throw new ArgumentException($"Unknown batch action '{args[0]}'."),
        };

        string? databasePath = null;
        ProcessingRunId? runId = null;
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

                    if (!Guid.TryParse(value, out Guid parsedRunId) || parsedRunId == Guid.Empty)
                    {
                        throw new ArgumentException($"Option '{option}' requires a non-empty GUID.");
                    }

                    runId = ProcessingRunId.From(parsedRunId);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (databasePath is null)
        {
            throw new ArgumentException("Option '--database' is required.");
        }

        if (runId is null)
        {
            throw new ArgumentException("Option '--run' is required.");
        }

        return new BatchCommandOptions(action, databasePath, runId.Value);
    }

    private static string Single(string? current, string value, string option)
    {
        if (current is not null)
        {
            throw new ArgumentException($"Option '{option}' may be supplied only once.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
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

        if (options.Action == BatchCommandAction.Cancel)
        {
            await repository.RequestCancellationAsync(
                options.RunId,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }

        ProcessingRunSummary summary = await repository.GetRunSummaryAsync(
            options.RunId,
            cancellationToken);
        WriteSummary(summary, output);
        return 0;
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
