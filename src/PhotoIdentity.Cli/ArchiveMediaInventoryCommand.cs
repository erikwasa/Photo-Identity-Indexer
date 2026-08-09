using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Cli;

internal sealed record ArchiveMediaInventoryCommandOptions(string DatabasePath)
{
    public static ArchiveMediaInventoryCommandOptions Parse(string[] args)
    {
        string? databasePath = null;
        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");

            switch (option)
            {
                case "--database":
                    if (databasePath is not null)
                    {
                        throw new ArgumentException("Option '--database' may be supplied only once.");
                    }

                    databasePath = string.IsNullOrWhiteSpace(value)
                        ? throw new ArgumentException("Option '--database' requires a non-empty value.")
                        : value.Trim();
                    break;
                default:
                    throw new ArgumentException($"Unknown archive inventory option '{option}'.");
            }
        }

        return new ArchiveMediaInventoryCommandOptions(
            databasePath ?? throw new ArgumentException("Option '--database' is required for archive inventory."));
    }
}

internal static class ArchiveMediaInventoryCommandRunner
{
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3fr", ".arw", ".cr2", ".cr3", ".dng", ".erf", ".fff", ".iiq", ".kdc",
        ".mef", ".mos", ".mrw", ".nef", ".nrw", ".orf", ".pef", ".raf", ".raw",
        ".rw2", ".rwl", ".sr2", ".srf", ".srw", ".x3f",
    };

    public static async Task<int> RunAsync(
        ArchiveMediaInventoryCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        SqliteCatalogueDatabase database = new(options.DatabasePath);
        await database.InitializeAsync(cancellationToken);
        ArchiveCoverageConfiguration? configured = await new SqliteArchiveCoverageRepository(database)
            .GetAsync(cancellationToken);
        if (configured is null)
        {
            output.WriteLine("archive-configured: false");
            return 1;
        }

        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> observedPaths = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (string relativeFolder in configured.IncludedFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root = relativeFolder.Length == 0
                ? configured.Source.RootLocator
                : Path.Combine(
                    configured.Source.RootLocator,
                    relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string path in EnumerateFiles(root, cancellationToken))
            {
                string fullPath = Path.GetFullPath(path);
                if (!observedPaths.Add(fullPath))
                {
                    continue;
                }

                string extension = Path.GetExtension(path).ToLowerInvariant();
                string key = extension.Length == 0 ? "<none>" : extension;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        output.WriteLine("archive-media-inventory: complete");
        output.WriteLine($"files-total: {observedPaths.Count}");
        output.WriteLine($"extensions: {counts.Count}");
        foreach ((string extension, int count) in counts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            (string family, bool supported) = Classify(extension);
            output.WriteLine(
                $"extension: {extension} count={count} family={family} supported={supported.ToString().ToLowerInvariant()}");
        }

        return 0;
    }

    private static IEnumerable<string> EnumerateFiles(
        string root,
        CancellationToken cancellationToken)
    {
        Queue<string> directories = new();
        directories.Enqueue(root);

        while (directories.TryDequeue(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    directories.Enqueue(child);
                }
            }
        }
    }

    private static (string Family, bool Supported) Classify(string extension) =>
        extension switch
        {
            ".jpg" or ".jpeg" => ("jpeg", true),
            ".png" => ("png", true),
            ".heic" or ".heif" => ("heif", true),
            _ when RawExtensions.Contains(extension) => ("raw", false),
            _ => ("other", false),
        };
}
