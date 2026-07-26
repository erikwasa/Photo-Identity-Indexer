using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteProcessingMigrationTests
{
    [Fact]
    public async Task Version_two_active_job_is_requeued_during_lease_migration()
    {
        string directory = SqliteProcessingRepositoryTests.CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false,
            }.ToString();
            string runId = Guid.NewGuid().ToString("D");
            string revisionId = Guid.NewGuid().ToString("D");
            string jobId = Guid.NewGuid().ToString("D");
            string now = new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero).ToString("O");

            await using (SqliteConnection connection = new(connectionString))
            {
                await connection.OpenAsync();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE schema_migrations (
                        version INTEGER NOT NULL PRIMARY KEY,
                        applied_at_utc TEXT NOT NULL);
                    CREATE TABLE processing_runs (
                        id TEXT NOT NULL PRIMARY KEY,
                        status TEXT NOT NULL,
                        configuration_json TEXT NOT NULL,
                        started_at_utc TEXT NOT NULL,
                        completed_at_utc TEXT NULL,
                        error TEXT NULL);
                    CREATE TABLE processing_jobs (
                        id TEXT NOT NULL PRIMARY KEY,
                        processing_run_id TEXT NOT NULL,
                        asset_revision_id TEXT NOT NULL,
                        status TEXT NOT NULL,
                        attempt_count INTEGER NOT NULL DEFAULT 0,
                        available_at_utc TEXT NOT NULL,
                        started_at_utc TEXT NULL,
                        completed_at_utc TEXT NULL,
                        error TEXT NULL,
                        UNIQUE (processing_run_id, asset_revision_id));
                    INSERT INTO schema_migrations (version, applied_at_utc) VALUES (1, $now);
                    INSERT INTO schema_migrations (version, applied_at_utc) VALUES (2, $now);
                    INSERT INTO processing_runs (
                        id, status, configuration_json, started_at_utc)
                        VALUES ($run_id, 'running', '{}', $now);
                    INSERT INTO processing_jobs (
                        id, processing_run_id, asset_revision_id, status, attempt_count,
                        available_at_utc, started_at_utc)
                        VALUES ($job_id, $run_id, $revision_id, 'running', 2, $now, $now);
                    PRAGMA user_version = 2;
                    """;
                command.Parameters.AddWithValue("$now", now);
                command.Parameters.AddWithValue("$run_id", runId);
                command.Parameters.AddWithValue("$revision_id", revisionId);
                command.Parameters.AddWithValue("$job_id", jobId);
                await command.ExecuteNonQueryAsync();
            }

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using SqliteConnection upgraded = await database.OpenConnectionAsync();
            using SqliteCommand read = upgraded.CreateCommand();
            read.CommandText = """
                SELECT status, attempt_count, started_at_utc, last_failure_kind,
                       error, idempotency_key, lease_token, leased_until_utc
                FROM processing_jobs
                WHERE id = $job_id;
                """;
            read.Parameters.AddWithValue("$job_id", jobId);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("queued", reader.GetString(0));
            Assert.Equal(2, reader.GetInt32(1));
            Assert.True(reader.IsDBNull(2));
            Assert.Equal("transient", reader.GetString(3));
            Assert.Contains("Recovered active job", reader.GetString(4), StringComparison.Ordinal);
            Assert.Equal($"{runId}:{revisionId}", reader.GetString(5));
            Assert.True(reader.IsDBNull(6));
            Assert.True(reader.IsDBNull(7));
        }
        finally
        {
            SqliteProcessingRepositoryTests.DeleteTemporaryDirectory(directory);
        }
    }
}
