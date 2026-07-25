using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SqliteCatalogueDatabaseTests
{
    [Fact]
    public async Task Initialize_creates_and_reapplies_the_current_schema()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);

            await database.InitializeAsync();
            await database.InitializeAsync();

            Assert.True(File.Exists(databasePath));

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            Assert.Equal(1, await ReadInt64Async(connection, "PRAGMA user_version;"));
            Assert.Equal(1, await ReadInt64Async(connection, "PRAGMA foreign_keys;"));
            Assert.Equal(1, await ReadInt64Async(connection, "SELECT COUNT(*) FROM schema_migrations;"));

            HashSet<string> expectedTables =
            [
                "schema_migrations",
                "sources",
                "assets",
                "asset_revisions",
                "face_occurrences",
                "face_observations",
                "face_crops",
                "embeddings",
                "people",
                "person_labels",
                "identity_suggestions",
                "processing_runs",
                "processing_jobs",
            ];

            HashSet<string> actualTables = [];
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT name
                FROM sqlite_schema
                WHERE type = 'table' AND name NOT LIKE 'sqlite_%';
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                actualTables.Add(reader.GetString(0));
            }

            Assert.True(
                expectedTables.SetEquals(actualTables),
                $"Expected [{string.Join(", ", expectedTables)}] but found [{string.Join(", ", actualTables)}].");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Human_labels_do_not_require_model_derived_rows()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            SeedFaceOccurrence(connection, out string faceOccurrenceId, out _);
            string personId = Guid.NewGuid().ToString("D");

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO people (id, display_name, created_at_utc)
                    VALUES ($person_id, 'Ada', $now);
                INSERT INTO person_labels (
                    person_id,
                    face_occurrence_id,
                    label_kind,
                    assigned_by,
                    assigned_at_utc)
                    VALUES ($person_id, $face_occurrence_id, 'confirmed', 'human', $now);
                """;
            command.Parameters.AddWithValue("$person_id", personId);
            command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();

            Assert.Equal(1, await ReadInt64Async(connection, "SELECT COUNT(*) FROM person_labels;"));
            Assert.Equal(
                0,
                await ReadInt64Async(
                    connection,
                    """
                    SELECT
                        (SELECT COUNT(*) FROM face_observations) +
                        (SELECT COUNT(*) FROM embeddings) +
                        (SELECT COUNT(*) FROM identity_suggestions);
                    """));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Embeddings_are_versioned_by_crop_model_and_model_hash()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            SeedFaceOccurrence(connection, out string faceOccurrenceId, out _);
            string cropId = Guid.NewGuid().ToString("D");

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO face_crops (
                        id,
                        face_occurrence_id,
                        crop_protocol,
                        content_sha256,
                        storage_path,
                        width,
                        height,
                        created_at_utc)
                        VALUES ($id, $face_occurrence_id, 'sface-five-point-v1', $hash, $path, 112, 112, $now);
                    """;
                command.Parameters.AddWithValue("$id", cropId);
                command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId);
                command.Parameters.AddWithValue("$hash", new string('c', 64));
                command.Parameters.AddWithValue("$path", "faces/0001.png");
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            await InsertEmbeddingAsync(connection, cropId, "sface", new string('a', 64));
            await InsertEmbeddingAsync(connection, cropId, "sface", new string('b', 64));

            SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
                () => InsertEmbeddingAsync(connection, cropId, "sface", new string('a', 64)));

            Assert.Equal(19, exception.SqliteErrorCode);
            Assert.Equal(2, await ReadInt64Async(connection, "SELECT COUNT(*) FROM embeddings;"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static void SeedFaceOccurrence(
        SqliteConnection connection,
        out string faceOccurrenceId,
        out string assetRevisionId)
    {
        string sourceId = Guid.NewGuid().ToString("D");
        string assetId = Guid.NewGuid().ToString("D");
        assetRevisionId = Guid.NewGuid().ToString("D");
        faceOccurrenceId = Guid.NewGuid().ToString("D");
        string now = DateTimeOffset.UtcNow.ToString("O");

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', $root_locator, $now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc)
                VALUES ($asset_id, $source_id, 'photo.jpg', $now);
            INSERT INTO asset_revisions (
                id,
                asset_id,
                content_sha256,
                size_bytes,
                observed_at_utc,
                media_type,
                width,
                height)
                VALUES ($revision_id, $asset_id, $revision_hash, 1234, $now, 'image/jpeg', 640, 480);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($face_occurrence_id, $revision_id, 0, $now);
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$root_locator", Path.Combine(Path.GetTempPath(), sourceId));
        command.Parameters.AddWithValue("$asset_id", assetId);
        command.Parameters.AddWithValue("$revision_id", assetRevisionId);
        command.Parameters.AddWithValue("$revision_hash", new string('d', 64));
        command.Parameters.AddWithValue("$face_occurrence_id", faceOccurrenceId);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static async Task InsertEmbeddingAsync(
        SqliteConnection connection,
        string cropId,
        string modelId,
        string modelHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO embeddings (
                face_crop_id,
                model_id,
                model_hash,
                dimensions,
                l2_norm,
                vector_blob,
                created_at_utc)
                VALUES ($crop_id, $model_id, $model_hash, 4, 1.0, $vector, $now);
            """;
        command.Parameters.AddWithValue("$crop_id", cropId);
        command.Parameters.AddWithValue("$model_id", modelId);
        command.Parameters.AddWithValue("$model_hash", modelHash);
        command.Parameters.AddWithValue("$vector", new byte[] { 0, 0, 0, 0 });
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ReadInt64Async(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
