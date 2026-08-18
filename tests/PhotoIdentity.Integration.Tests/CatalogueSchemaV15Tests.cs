using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class CatalogueSchemaV15Tests
{
    [Fact]
    public async Task Version_14_catalogue_applies_place_enrichment_schema_while_upgrading_to_current_version()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand downgrade = connection.CreateCommand();
                downgrade.CommandText = """
                    DROP TABLE person_smart_collection_visibility;
                    DROP TABLE photo_place_enrichment_attempts;
                    DROP TABLE photo_place_reverse_geocode_cache;
                    DELETE FROM schema_migrations WHERE version >= 15;
                    PRAGMA user_version = 14;
                    """;
                await downgrade.ExecuteNonQueryAsync();
            }

            await database.InitializeAsync();

            await using SqliteConnection upgraded = await database.OpenConnectionAsync();
            Assert.Equal(SqliteCatalogueDatabase.CurrentSchemaVersion, await ReadUserVersionAsync(upgraded));
            Assert.True(await TableExistsAsync(upgraded, "photo_place_reverse_geocode_cache"));
            Assert.True(await TableExistsAsync(upgraded, "photo_place_enrichment_attempts"));
            Assert.True(await MigrationExistsAsync(upgraded, 15));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<int> ReadUserVersionAsync(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $name;
            """;
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> MigrationExistsAsync(SqliteConnection connection, int version)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = $version;";
        command.Parameters.AddWithValue("$version", version);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
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
