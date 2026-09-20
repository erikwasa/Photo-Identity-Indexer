using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class StructuredSmartCollectionDateApplicationTests
{
    [Fact]
    public async Task Structured_date_range_persists_as_versioned_bounds_and_legacy_requests_remain_compatible()
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

            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage create = await client.PostAsJsonAsync(
                "/api/smart-collections",
                new SmartCollectionDefinitionRequest(
                    "Structured dates",
                    TakenRange: new SmartCollectionDateRangeRequest(
                        "2020-01-01",
                        "2021-12-31")));
            create.EnsureSuccessStatusCode();
            SmartCollectionDefinitionResponse structured =
                await create.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException("Structured Smart Collection response was empty.");

            Assert.Equal("2020-01-01", structured.Filter.Taken!.From);
            Assert.Equal("2021-12-31", structured.Filter.Taken.To);

            await using (SqliteConnection connection = await database.OpenConnectionAsync())
            {
                using SqliteCommand persisted = connection.CreateCommand();
                persisted.CommandText = """
                    SELECT filter_schema_version, filter_json
                    FROM smart_collections
                    WHERE id = $id;
                    """;
                persisted.Parameters.AddWithValue("$id", structured.Id);

                await using SqliteDataReader reader = await persisted.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(3, reader.GetInt32(0));
                string json = reader.GetString(1);
                Assert.Contains("\"taken\":{\"from\":\"2020-01-01\",\"to\":\"2021-12-31\"}", json);
                Assert.DoesNotContain("2020-2021", json, StringComparison.Ordinal);
            }

            using HttpResponseMessage reopenedResponse = await client.GetAsync(
                $"/api/smart-collections/{structured.Id}");
            reopenedResponse.EnsureSuccessStatusCode();
            SmartCollectionDefinitionResponse reopened =
                await reopenedResponse.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException("Reopened Smart Collection response was empty.");
            Assert.Equal(structured.Filter.Taken, reopened.Filter.Taken);

            using HttpResponseMessage legacy = await client.PostAsJsonAsync(
                "/api/smart-collections",
                new SmartCollectionDefinitionRequest(
                    "Legacy date request",
                    Taken: "2022"));
            legacy.EnsureSuccessStatusCode();
            SmartCollectionDefinitionResponse legacySaved =
                await legacy.Content.ReadFromJsonAsync<SmartCollectionDefinitionResponse>()
                ?? throw new InvalidOperationException("Legacy Smart Collection response was empty.");
            Assert.Equal("2022-01-01", legacySaved.Filter.Taken!.From);
            Assert.Equal("2022-12-31", legacySaved.Filter.Taken.To);

            using HttpResponseMessage incomplete = await client.PostAsJsonAsync(
                "/api/smart-collections",
                new SmartCollectionDefinitionRequest(
                    "Incomplete",
                    TakenRange: new SmartCollectionDateRangeRequest(
                        "",
                        "2022-12-31")));
            Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);

            using HttpResponseMessage ambiguous = await client.PostAsJsonAsync(
                "/api/smart-collections",
                new SmartCollectionDefinitionRequest(
                    "Ambiguous",
                    Taken: "2022",
                    TakenRange: new SmartCollectionDateRangeRequest(
                        "2022-01-01",
                        "2022-12-31")));
            Assert.Equal(HttpStatusCode.BadRequest, ambiguous.StatusCode);
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
}
