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
            Assert.Equal(2, await ReadInt64Async(connection, "PRAGMA user_version;"));
            Assert.Equal(1, await ReadInt64Async(connection, "PRAGMA foreign_keys;"));
            Assert.Equal(2, await ReadInt64Async(connection, "SELECT COUNT(*) FROM schema_migrations;"));
            Assert.Equal(
                1,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM pragma_table_info('assets') WHERE name = 'last_seen_at_utc';"));
            Assert.Equal(
                1,
                await ReadInt64Async(
                    connection,
                    "SELECT COUNT(*) FROM pragma_table_info('assets') WHERE name = 'deleted_at_utc';"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Version_one_database_is_upgraded_without_losing_assets()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            string sourceId = Guid.NewGuid().ToString("D");
            string assetId = Guid.NewGuid().ToString("D");
            string createdAt = new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero).ToString("O");

            await using (SqliteConnection connection = new($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE schema_migrations (
                        version INTEGER NOT NULL PRIMARY KEY,
                        applied_at_utc TEXT NOT NULL);
                    CREATE TABLE sources (
                        id TEXT NOT NULL PRIMARY KEY,
                        kind TEXT NOT NULL,
                        root_locator TEXT NOT NULL,
                        created_at_utc TEXT NOT NULL,
                        UNIQUE (kind, root_locator));
                    CREATE TABLE assets (
                        id TEXT NOT NULL PRIMARY KEY,
                        source_id TEXT NOT NULL,
                        source_key TEXT NOT NULL,
                        created_at_utc TEXT NOT NULL,
                        FOREIGN KEY (source_id) REFERENCES sources (id) ON DELETE RESTRICT,
                        UNIQUE (source_id, source_key));
                    INSERT INTO schema_migrations (version, applied_at_utc) VALUES (1, $created_at);
                    INSERT INTO sources (id, kind, root_locator, created_at_utc)
                        VALUES ($source_id, 'local-folder', 'C:/Photos', $created_at);
                    INSERT INTO assets (id, source_id, source_key, created_at_utc)
                        VALUES ($asset_id, $source_id, 'photo.jpg', $created_at);
                    PRAGMA user_version = 1;
                    """;
                command.Parameters.AddWithValue("$created_at", createdAt);
                command.Parameters.AddWithValue("$source_id", sourceId);
                command.Parameters.AddWithValue("$asset_id", assetId);
                await command.ExecuteNonQueryAsync();
            }

            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using SqliteConnection upgraded = await database.OpenConnectionAsync();
            Assert.Equal(2, await ReadInt64Async(upgraded, "PRAGMA user_version;"));
            Assert.Equal(1, await ReadInt64Async(upgraded, "SELECT COUNT(*) FROM assets;"));
            using SqliteCommand read = upgraded.CreateCommand();
            read.CommandText = "SELECT last_seen_at_utc, deleted_at_utc FROM assets WHERE id = $id;";
            read.Parameters.AddWithValue("$id", assetId);
            await using SqliteDataReader reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(createdAt, reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
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
            SeedFaceOccurrence(connection, out string faceOccurrenceId);
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
            SeedFaceOccurrence(connection, out string faceOccurrenceId);
            string cropId = Guid.NewGuid().ToString("D");

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = """
                    INSERT INTO face_crops (
                        id, face_occurrence_id, crop_protocol, content_sha256,
                        storage_path, width, height, created_at_utc)
                    VALUES ($id, $face_id, 'sface-five-point-v1', $hash, $path, 112, 112, $now);
                    """;
                command.Parameters.AddWithValue("$id", cropId);
                command.Parameters.AddWithValue("$face_id", faceOccurrenceId);
                command.Parameters.AddWithValue("$hash", new string('c', 64));
                command.Parameters.AddWithValue("$path", "faces/0001.png");
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            await InsertEmbeddingAsync(connection, cropId, new string('a', 64));
            await InsertEmbeddingAsync(connection, cropId, new string('b', 64));
            SqliteException exception = await Assert.ThrowsAsync<SqliteException>(
                () => InsertEmbeddingAsync(connection, cropId, new string('a', 64)));

            Assert.Equal(19, exception.SqliteErrorCode);
            Assert.Equal(2, await ReadInt64Async(connection, "SELECT COUNT(*) FROM embeddings;"));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static void SeedFaceOccurrence(SqliteConnection connection, out string faceOccurrenceId)
    {
        string sourceId = Guid.NewGuid().ToString("D");
        string assetId = Guid.NewGuid().ToString("D");
        string revisionId = Guid.NewGuid().ToString("D");
        faceOccurrenceId = Guid.NewGuid().ToString("D");
        string now = DateTimeOffset.UtcNow.ToString("O");

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sources (id, kind, root_locator, created_at_utc)
                VALUES ($source_id, 'local-folder', $root_locator, $now);
            INSERT INTO assets (id, source_id, source_key, created_at_utc, last_seen_at_utc)
                VALUES ($asset_id, $source_id, 'photo.jpg', $now, $now);
            INSERT INTO asset_revisions (
                id, asset_id, content_sha256, size_bytes, observed_at_utc,
                media_type, width, height)
                VALUES ($revision_id, $asset_id, $hash, 1234, $now, 'image/jpeg', 640, 480);
            INSERT INTO face_occurrences (id, asset_revision_id, ordinal, created_at_utc)
                VALUES ($face_id, $revision_id, 0, $now);
            """;
        command.Parameters.AddWithValue("$source_id", sourceId);
        command.Parameters.AddWithValue("$root_locator", Path.Combine(Path.GetTempPath(), sourceId));
        command.Parameters.AddWithValue("$asset_id", assetId);
        command.Parameters.AddWithValue("$revision_id", revisionId);
        command.Parameters.AddWithValue("$hash", new string('d', 64));
        command.Parameters.AddWithValue("$face_id", faceOccurrenceId);
        command.Parameters.AddWithValue("$now", now);
        command.ExecuteNonQuery();
    }

    private static async Task InsertEmbeddingAsync(
        SqliteConnection connection,
        string cropId,
        string modelHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO embeddings (
                face_crop_id, model_id, model_hash, dimensions,
                l2_norm, vector_blob, created_at_utc)
            VALUES ($crop_id, 'sface', $model_hash, 4, 1.0, $vector, $now);
            """;
        command.Parameters.AddWithValue("$crop_id", cropId);
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
