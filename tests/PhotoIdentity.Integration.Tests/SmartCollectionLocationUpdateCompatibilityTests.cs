using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Api;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionLocationUpdateCompatibilityTests
{
    [Fact]
    public async Task Update_without_place_preserves_existing_named_place_and_empty_place_clears_it()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            SqliteCatalogueDatabase database = new(databasePath);
            await database.InitializeAsync();

            await using SmartCollectionApiFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage create = await client.PostAsJsonAsync(
                "/api/smart-collections",
                new SmartCollectionDefinitionRequest(
                    "Stockholm",
                    Location: new SmartCollectionLocationRequest(
                        South: 59,
                        West: 17,
                        North: 60,
                        East: 19,
                        Place: "Sweden/Stockholm region")));
            await create.EnsureSuccessWithDiagnosticBodyAsync("create smart collection with named place");
            SmartCollectionDefinitionResponse created =
                await create.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException("Create response was empty.");

            using HttpResponseMessage legacyUpdate = await client.PutAsJsonAsync(
                $"/api/smart-collections/{created.Id}",
                new SmartCollectionDefinitionRequest(
                    "Stockholm updated",
                    Location: new SmartCollectionLocationRequest(
                        South: 59.1,
                        West: 17.1,
                        North: 60.1,
                        East: 19.1)));
            await legacyUpdate.EnsureSuccessWithDiagnosticBodyAsync("update smart collection without place");
            SmartCollectionDefinitionResponse preserved =
                await legacyUpdate.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException("Update response was empty.");
            Assert.Equal("sweden/stockholm region", preserved.Filter.Location?.Place);
            Assert.Equal(59.1, preserved.Filter.Location?.South);

            using HttpResponseMessage explicitClear = await client.PutAsJsonAsync(
                $"/api/smart-collections/{created.Id}",
                new SmartCollectionDefinitionRequest(
                    "Stockholm GPS only",
                    Location: new SmartCollectionLocationRequest(
                        South: 59.1,
                        West: 17.1,
                        North: 60.1,
                        East: 19.1,
                        Place: string.Empty)));
            await explicitClear.EnsureSuccessWithDiagnosticBodyAsync("clear smart collection place");
            SmartCollectionDefinitionResponse cleared =
                await explicitClear.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException("Clear response was empty.");
            Assert.Null(cleared.Filter.Location?.Place);
            Assert.Equal(59.1, cleared.Filter.Location?.South);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Schema_fourteen_directly_matches_the_places_guard_contract()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            SqliteCatalogueDatabase database = new(Path.Combine(directory, "catalogue.db"));
            await database.InitializeAsync();

            await using SqliteConnection connection = await database.OpenConnectionAsync();
            using SqliteCommand tableInfo = connection.CreateCommand();
            tableInfo.CommandText = "PRAGMA table_info(photo_place_actions);";
            bool hasProvider = false;
            await using (SqliteDataReader reader = await tableInfo.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    hasProvider |= string.Equals(reader.GetString(1), "provider", StringComparison.OrdinalIgnoreCase);
                }
            }
            Assert.True(hasProvider);

            using SqliteCommand definition = connection.CreateCommand();
            definition.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'photo_place_actions';";
            string sql = (string?)await definition.ExecuteScalarAsync() ?? string.Empty;
            Assert.Contains("'migration'", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("'legacy-migration'", sql, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
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
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class SmartCollectionApiFactory : PhotoIdentityApiTestFactory
    {
        public SmartCollectionApiFactory(string databasePath)
            : base(databasePath)
        {
        }
    }
}
