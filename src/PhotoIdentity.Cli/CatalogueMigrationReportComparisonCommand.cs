using System.Text.Json;

namespace PhotoIdentity.Cli;

internal sealed record CatalogueMigrationReportComparisonCommandOptions(
    string LeftReportPath,
    string RightReportPath)
{
    public static CatalogueMigrationReportComparisonCommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "compare")
        {
            throw new ArgumentException("The catalogue compare command requires 'compare'.");
        }

        string? left = null;
        string? right = null;
        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            string value = index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Option '{option}' requires a value.");
            switch (option)
            {
                case "--left-report":
                    left = Single(left, value, option);
                    break;
                case "--right-report":
                    right = Single(right, value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (left is null)
        {
            throw new ArgumentException("Option '--left-report' is required.");
        }
        if (right is null)
        {
            throw new ArgumentException("Option '--right-report' is required.");
        }

        return new(left, right);
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
}

internal static class CatalogueMigrationReportComparisonCommandRunner
{
    public static async Task<int> RunAsync(
        CatalogueMigrationReportComparisonCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        CatalogueMigrationReport left = await ReadReportAsync(options.LeftReportPath, cancellationToken);
        CatalogueMigrationReport right = await ReadReportAsync(options.RightReportPath, cancellationToken);

        List<string> differences = Compare(left, right);
        if (differences.Count != 0)
        {
            output.WriteLine("equivalent: false");
            foreach (string difference in differences)
            {
                output.WriteLine($"difference: {difference}");
            }
            return 1;
        }

        output.WriteLine("equivalent: true");
        output.WriteLine($"source-sha256: {left.SourceSha256}");
        output.WriteLine($"rows-copied: {left.RowsCopied}");
        output.WriteLine($"tables-compared: {left.Tables.Count}");
        output.WriteLine($"sequences-repaired: {left.SequencesRepaired}");
        return 0;
    }

    internal static List<string> Compare(CatalogueMigrationReport left, CatalogueMigrationReport right)
    {
        List<string> differences = [];

        CompareValue("schema-version", left.SchemaVersion, right.SchemaVersion, differences);
        CompareValue("source-file-name", left.SourceFileName, right.SourceFileName, differences);
        CompareValue("source-sha256", left.SourceSha256, right.SourceSha256, differences);
        CompareValue("source-bytes", left.SourceBytes, right.SourceBytes, differences);
        CompareValue("sqlite-schema-version", left.SqliteSchemaVersion, right.SqliteSchemaVersion, differences);
        CompareValue("postgres-schema-version", left.PostgresSchemaVersion, right.PostgresSchemaVersion, differences);
        CompareValue("rows-copied", left.RowsCopied, right.RowsCopied, differences);
        CompareValue("sequences-repaired", left.SequencesRepaired, right.SequencesRepaired, differences);
        CompareValue("validation", left.Validation, right.Validation, differences);

        Dictionary<string, CatalogueMigrationTableResult> leftTables = left.Tables.ToDictionary(
            table => table.Table,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CatalogueMigrationTableResult> rightTables = right.Tables.ToDictionary(
            table => table.Table,
            StringComparer.OrdinalIgnoreCase);

        foreach (string table in leftTables.Keys.Union(rightTables.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            if (!leftTables.TryGetValue(table, out CatalogueMigrationTableResult? leftTable))
            {
                differences.Add($"table '{table}' exists only in right report");
                continue;
            }
            if (!rightTables.TryGetValue(table, out CatalogueMigrationTableResult? rightTable))
            {
                differences.Add($"table '{table}' exists only in left report");
                continue;
            }

            CompareValue($"table '{table}' sqlite-rows", leftTable.SqliteRows, rightTable.SqliteRows, differences);
            CompareValue($"table '{table}' postgres-rows", leftTable.PostgresRows, rightTable.PostgresRows, differences);
        }

        foreach (string key in left.CriticalCounts.Keys.Union(right.CriticalCounts.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            bool hasLeft = left.CriticalCounts.TryGetValue(key, out long leftValue);
            bool hasRight = right.CriticalCounts.TryGetValue(key, out long rightValue);
            if (!hasLeft || !hasRight)
            {
                differences.Add($"critical count '{key}' exists only in {(hasLeft ? "left" : "right")} report");
                continue;
            }

            CompareValue($"critical count '{key}'", leftValue, rightValue, differences);
        }

        return differences;
    }

    private static async Task<CatalogueMigrationReport> ReadReportAsync(string path, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new ArgumentException($"Migration report does not exist: {Path.GetFileName(fullPath)}");
        }

        await using FileStream stream = File.OpenRead(fullPath);
        CatalogueMigrationReport? report = await JsonSerializer.DeserializeAsync<CatalogueMigrationReport>(
            stream,
            cancellationToken: cancellationToken);
        return report ?? throw new InvalidOperationException(
            $"Migration report '{Path.GetFileName(fullPath)}' could not be parsed.");
    }

    private static void CompareValue<T>(string name, T left, T right, ICollection<string> differences)
    {
        if (!EqualityComparer<T>.Default.Equals(left, right))
        {
            differences.Add($"{name} differs");
        }
    }
}
