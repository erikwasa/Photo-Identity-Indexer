using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteIdentityMatcherMigrationTests
{
    [Fact]
    public async Task Version_four_catalogue_adds_ranked_suggestion_schema_without_changing_existing_suggestions()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false,
            }.ToString();
            string faceId = Guid.NewGuid().ToString("D");
            string personId = Guid.NewGuid().ToString("D");
            string now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero).ToString("O");

            await using (SqliteConnection connection = new(connectionString))
            {
                await connection.OpenAsync();
                using SqliteCommand seed = connection.CreateCommand();
                seed.CommandText = """
                    CREATE TABLE schema_migrations (
                        version INTEGER NOT NULL PRIMARY KEY,
                        applied_at_utc TEXT NOT NULL);
                    CREATE TABLE face_occurrences (
                        id TEXT NOT NULL PRIMARY KEY);
                    CREATE TABLE identity_suggestions (
                        id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        face_occurrence_id TEXT NOT NULL,
                        suggested_person_id TEXT NOT NULL,
                        model_id TEXT NOT NULL,
                        model_hash TEXT NOT NULL,
                        score REAL NOT NULL,
                        status TEXT NOT NULL,
                        created_at_utc TEXT NOT NULL,
                        UNIQUE (face_occurrence_id, suggested_person_id, model_id, model_hash));
                    INSERT INTO schema_migrations (version, applied_at_utc)
                        VALUES (1, $now), (2, $now), (3, $now), (4, $now);
                    INSERT INTO face_occurrences (id) VALUES ($face_id);
                    INSERT INTO identity_suggestions (
                        face_occurrence_id,
                        suggested_person_id,
                        model_id,
                        model_hash,
                        score,
                        status,
                        created_at_utc)
                    VALUES (
                        $face_id,
                        $person_id,
                        'sface',
                        $model_hash,
                        0.91,
                        'rejected',
                        $now);
                    PRAGMA user_version = 4;
                    """;
                seed.Parameters.AddWithValue("$now", now);
                seed.Parameters.AddWithValue("$face_id", faceId);
                seed.Parameters.AddWithValue("$person_id", personId);
                seed.Parameters.AddWithValue("$model_hash", new string('a', 64));
                await seed.ExecuteNonQueryAsync();
            }

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using SqliteConnection upgraded = await database.OpenConnectionAsync();
            Assert.Equal(5, await ReadInt64Async(upgraded, "PRAGMA user_version;"));
            Assert.Equal(5, await ReadInt64Async(upgraded, "SELECT COUNT(*) FROM schema_migrations;"));
            Assert.Equal(1, await ReadInt64Async(upgraded, "SELECT COUNT(*) FROM identity_suggestions;"));
            Assert.Equal(
                1,
                await ReadInt64Async(
                    upgraded,
                    "SELECT COUNT(*) FROM identity_suggestions WHERE status = 'rejected' AND score = 0.91;"));
            Assert.Equal(
                1,
                await ReadInt64Async(
                    upgraded,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'identity_suggestion_rankings';"));
            Assert.Equal(
                1,
                await ReadInt64Async(
                    upgraded,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_identity_suggestion_rankings_model';"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<long> ReadInt64Async(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
