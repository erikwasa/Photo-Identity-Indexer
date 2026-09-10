using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity.Cli;

internal sealed record CatalogueBackupCommandOptions(
    string DatabasePath,
    string OutputPath,
    bool ApplicationStopped)
{
    public static CatalogueBackupCommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] != "backup")
        {
            throw new ArgumentException("The catalogue backup command requires 'backup'.");
        }

        string? databasePath = null;
        string? outputPath = null;
        bool applicationStopped = false;

        for (int index = 1; index < args.Length; index++)
        {
            string option = args[index];
            if (option == "--application-stopped")
            {
                if (applicationStopped)
                {
                    throw new ArgumentException("Option '--application-stopped' may be supplied only once.");
                }

                applicationStopped = true;
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
                case "--output":
                    outputPath = Single(outputPath, value, option);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{option}'.");
            }
        }

        if (databasePath is null)
        {
            throw new ArgumentException("Option '--database' is required.");
        }

        if (outputPath is null)
        {
            throw new ArgumentException("Option '--output' is required.");
        }

        if (!applicationStopped)
        {
            throw new ArgumentException(
                "Option '--application-stopped' is required. Stop Photo Identity before creating the cutover backup.");
        }

        return new(databasePath, outputPath, applicationStopped);
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

internal static class CatalogueBackupCommandRunner
{
    public static async Task<int> RunAsync(
        CatalogueBackupCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        string sourcePath = Path.GetFullPath(options.DatabasePath);
        string backupPath = Path.GetFullPath(options.OutputPath);

        if (!File.Exists(sourcePath))
        {
            throw new ArgumentException("The SQLite catalogue file does not exist.");
        }

        if (string.Equals(sourcePath, backupPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The backup output must be different from the source SQLite catalogue.");
        }

        if (File.Exists(backupPath))
        {
            throw new ArgumentException("The backup output already exists. Use a new timestamped backup path.");
        }

        string? directory = Path.GetDirectoryName(backupPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string sourceShaBefore = await ComputeSha256Async(sourcePath, cancellationToken);
        int sourceSchemaVersion;
        bool backupCreated = false;
        try
        {
            SqliteConnectionStringBuilder sourceBuilder = new()
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
            };
            SqliteConnectionStringBuilder backupBuilder = new()
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
            };

            await using (SqliteConnection source = new(sourceBuilder.ConnectionString))
            await using (SqliteConnection destination = new(backupBuilder.ConnectionString))
            {
                await source.OpenAsync(cancellationToken);
                await ExecuteAsync(source, "PRAGMA query_only = ON;", cancellationToken);
                sourceSchemaVersion = checked((int)await ScalarInt64Async(
                    source,
                    "PRAGMA user_version;",
                    cancellationToken));
                if (sourceSchemaVersion != SqliteCatalogueDatabase.CurrentSchemaVersion)
                {
                    throw new InvalidOperationException(
                        $"SQLite catalogue schema version {sourceSchemaVersion} is not the supported current version {SqliteCatalogueDatabase.CurrentSchemaVersion}. " +
                        "Start the current SQLite-authoritative application once to upgrade it before the final stopped backup.");
                }

                await AssertForeignKeysAsync(source, "source catalogue", cancellationToken);
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
                backupCreated = true;
            }

            string sourceShaAfter = await ComputeSha256Async(sourcePath, cancellationToken);
            if (!string.Equals(sourceShaBefore, sourceShaAfter, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The source SQLite catalogue changed while the backup was being created. Keep Photo Identity stopped and retry with a new backup path.");
            }

            await ValidateBackupAsync(backupPath, sourceSchemaVersion, cancellationToken);
            string backupSha256 = await ComputeSha256Async(backupPath, cancellationToken);
            FileInfo backupFile = new(backupPath);

            output.WriteLine($"backup: {backupFile.Name}");
            output.WriteLine($"sqlite-schema-version: {sourceSchemaVersion}");
            output.WriteLine($"source-sha256-before-backup: {sourceShaBefore}");
            output.WriteLine($"backup-sha256: {backupSha256}");
            output.WriteLine($"backup-bytes: {backupFile.Length.ToString(CultureInfo.InvariantCulture)}");
            output.WriteLine("validation: passed");
            return 0;
        }
        catch
        {
            if (backupCreated && File.Exists(backupPath))
            {
                try
                {
                    File.Delete(backupPath);
                }
                catch
                {
                    // Preserve the original exception. A partial backup is never reported as accepted.
                }
            }

            throw;
        }
    }

    private static async Task ValidateBackupAsync(
        string backupPath,
        int expectedSchemaVersion,
        CancellationToken cancellationToken)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
        };
        await using SqliteConnection backup = new(builder.ConnectionString);
        await backup.OpenAsync(cancellationToken);
        await ExecuteAsync(backup, "PRAGMA query_only = ON;", cancellationToken);

        int version = checked((int)await ScalarInt64Async(backup, "PRAGMA user_version;", cancellationToken));
        if (version != expectedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Backup schema version {version} does not match source schema version {expectedSchemaVersion}.");
        }

        await using (SqliteCommand integrity = backup.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            object? result = await integrity.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(Convert.ToString(result, CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The created SQLite backup failed PRAGMA integrity_check.");
            }
        }

        await AssertForeignKeysAsync(backup, "created backup", cancellationToken);
    }

    private static async Task AssertForeignKeysAsync(
        SqliteConnection connection,
        string label,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"The {label} failed PRAGMA foreign_key_check.");
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarInt64Async(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
