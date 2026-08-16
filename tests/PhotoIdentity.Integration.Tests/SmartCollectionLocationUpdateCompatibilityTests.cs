using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PhotoIdentity.Api;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SmartCollectionLocationUpdateCompatibilityTests
{
    [Fact]
    public async Task Update_without_place_preserves_existing_named_place_and_empty_place_clears_it()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

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
            create.EnsureSuccessStatusCode();
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
            legacyUpdate.EnsureSuccessStatusCode();
            SmartCollectionDefinitionResponse preserved =
                await legacyUpdate.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException("Update response was empty.");
            Assert.Equal("Sweden/Stockholm region", preserved.Filter.Location?.Place);
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
            explicitClear.EnsureSuccessStatusCode();
            SmartCollectionDefinitionResponse cleared =
                await explicitClear.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException("Clear response was empty.");
            Assert.Null(cleared.Filter.Location?.Place);
            Assert.Equal(59.1, cleared.Filter.Location?.South);
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

    private sealed class SmartCollectionApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;

        public SmartCollectionApiFactory(string databasePath)
        {
            _databasePath = databasePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
        }
    }
}
