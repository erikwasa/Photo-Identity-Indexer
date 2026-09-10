using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Cli;
using PhotoIdentity.Persistence.Sqlite;

namespace PhotoIdentity_Integration_Tests;

public sealed class CatalogueBackupCommandTests
{
    [Fact]
    public async Task Backup_requires_explicit_application_stopped_confirmation()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-backup-options-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "catalogue.db");
        string backupPath = Path.Combine(directory, "backup.db");
        try
        {
            await File.WriteAllBytesAsync(sourcePath, []);
            StringWriter output = new();
            StringWriter error = new();

            int exit = await Program.RunAsync(
                ["catalogue", "backup", "--database", sourcePath, "--output", backupPath],
                output,
                error);

            Assert.Equal(2, exit);
            Assert.Contains("--application-stopped", error.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(backupPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Backup_creates_valid_snapshot_without_modifying_source()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"photoidentity-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "catalogue.db");
        string backupPath = Path.Combine(directory, "catalogue-20260910T120000Z.db");
        Guid sourceId = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        try
        {
            SqliteCatalogueDatabase database = new(sourcePath);
            await database.InitializeAsync();
            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            await using (SqliteCommand seed = connection.CreateCommand())
            {
                seed.CommandText = """
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                    VALUES ($id, 'local-archive', 'Kamerabilder', $now);
                    """;
                seed.Parameters.AddWithValue("$id", sourceId.ToString());
                seed.Parameters.AddWithValue("$now", now.ToString("O"));
                await seed.ExecuteNonQueryAsync();
            }

            SqliteConnection.ClearAllPools();
            string sourceHashBefore = await Sha256Async(sourcePath);
            StringWriter output = new();
            StringWriter error = new();

            int exit = await Program.RunAsync(
                [
                    "catalogue", "backup",
                    "--database", sourcePath,
                    "--output", backupPath,
                    "--application-stopped",
                ],
                output,
                error);

            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, error.ToString());
            Assert.True(File.Exists(backupPath));
            Assert.Equal(sourceHashBefore, await Sha256Async(sourcePath));
            Assert.Contains("validation: passed", output.ToString(), StringComparison.Ordinal);
            Assert.Contains(Path.GetFileName(backupPath), output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(sourcePath, output.ToString(), StringComparison.OrdinalIgnoreCase);

            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadOnly,
            };
            await using SqliteConnection backup = new(builder.ConnectionString);
            await backup.OpenAsync();
            Assert.Equal(SqliteCatalogueDatabase.CurrentSchemaVersion, await ScalarLongAsync(backup, "PRAGMA user_version;"));
            Assert.Equal(1L, await ScalarLongAsync(backup, "SELECT COUNT(*) FROM sources;"));
            Assert.Equal(sourceId.ToString(), await ScalarStringAsync(backup, "SELECT id FROM sources LIMIT 1;"));
            Assert.Equal("ok", await ScalarStringAsync(backup, "PRAGMA integrity_check;"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = new(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = new(sql, connection);
        return Convert.ToString(await command.ExecuteScalarAsync())
            ?? throw new InvalidOperationException("Expected string scalar.");
    }
}
