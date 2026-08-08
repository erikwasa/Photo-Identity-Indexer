using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Source.Local;
using PhotoIdentity.Worker;

namespace PhotoIdentity.Cli;

internal enum ArchiveCommandAction
{
    Include,
    List,
    Sync,
}

internal sealed record ArchiveCommandOptions(
    ArchiveCommandAction Action,
    string DatabasePath,
    string? RootPath,
    string? RelativeFolder)
{
    public static ArchiveCommandOptions Parse(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("The archive command requires 'include', 'list' or 'sync'.");
        }

        ArchiveCommandAction action = args[0] switch
        {
            "include" => ArchiveCommandAction.Include,
            "list" => ArchiveCommandAction.List,
            "sync" => ArchiveCommandAction.Sync,
            _ => throw new ArgumentException($"Unknown archive action '{args[0]}'."),
        };

        string? databasePath = null;
        string? rootPath = null;
        string? relativeFolder = null;

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
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (databasePath is null)
        {
            throw new ArgumentException("Option '--database' is required.");
        }

        if (action == ArchiveCommandAction.Include)
        {
            if (rootPath is null)
            {
                throw new ArgumentException("Option '--root' is required for archive include.");
            }

            if (relativeFolder is null)
            {
                throw new ArgumentException("Option '--folder' is required for archive include.");
            }
        }
        else if (rootPath is not null || relativeFolder is not null)
        {
            throw new ArgumentException("Options '--root' and '--folder' are valid only for archive include.");
        }

        return new ArchiveCommandOptions(action, databasePath, rootPath, relativeFolder);
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
