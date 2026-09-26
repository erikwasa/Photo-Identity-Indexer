using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PhotoIdentity.Persistence.Postgres;
using PhotoIdentity.Persistence.Sqlite;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class PostgresRuntimeApplicationTests
{
    private const string TestModelHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Provisional_clustering_endpoint_rejects_SQLite_test_compatibility_host()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sqlitePath = Path.Combine(directory, "catalogue.db");

        try
        {
            await using PhotoIdentityApiTestFactory factory = new(
                sqlitePath);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync(
                $"/api/review/provisional-clusters?modelId=test-model&modelHash={TestModelHash}");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using JsonDocument problem = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "Provisional clustering is available only when PostgreSQL is the selected catalogue provider.",
                problem.RootElement.GetProperty("detail").GetString());

            using HttpResponseMessage advisory = await client.GetAsync(
                $"/api/review/provisional-clusters/review-groups/test-cluster/known-person-advisory?modelId=test-model&modelHash={TestModelHash}");
            Assert.Equal(HttpStatusCode.Conflict, advisory.StatusCode);

            Assert.IsType<SqliteCatalogueDatabase>(
                factory.Services.GetRequiredService<SqliteCatalogueDatabase>());
            Assert.Null(factory.Services.GetService<PostgresCatalogueDatabase>());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PostgreSQL_selected_host_starts_without_registering_or_creating_SQLite_catalogue_WhenLivePostgresIsConfigured()
    {
        string? adminConnectionString = Environment.GetEnvironmentVariable(
            "PHOTOIDENTITY_TEST_POSTGRES_ADMIN_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            return;
        }

        string databaseName = $"photoidentity_runtime_{Guid.NewGuid():N}";
        string quotedDatabaseName = QuoteIdentifier(databaseName);
        NpgsqlConnectionStringBuilder adminBuilder = new(adminConnectionString)
        {
            Pooling = false,
        };

        await using NpgsqlConnection adminConnection = new(adminBuilder.ConnectionString);
        await adminConnection.OpenAsync();
        await using (NpgsqlCommand createDatabase = adminConnection.CreateCommand())
        {
            createDatabase.CommandText = $"CREATE DATABASE {quotedDatabaseName};";
            await createDatabase.ExecuteNonQueryAsync();
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            "PhotoIdentity.Integration.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sqlitePath = Path.Combine(directory, "must-not-exist.db");

        try
        {
            NpgsqlConnectionStringBuilder testBuilder = new(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false,
            };

            await using PhotoIdentityApiTestFactory factory = new(
                sqlitePath,
                builder =>
                {
                    builder.UseSetting("PhotoIdentity:Postgres:ConnectionString", testBuilder.ConnectionString);
                },
                useSqliteTestCompatibility: false);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using JsonDocument health = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "postgresql",
                health.RootElement.GetProperty("catalogueProvider").GetString());
            Assert.Equal(
                PostgresCatalogueDatabase.CurrentSchemaVersion,
                health.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(
                "ready",
                health.RootElement.GetProperty("postgres").GetProperty("status").GetString());

            using HttpResponseMessage clustering = await client.GetAsync(
                $"/api/review/provisional-clusters?modelId=test-model&modelHash={TestModelHash}");
            Assert.Equal(HttpStatusCode.OK, clustering.StatusCode);
            using JsonDocument clusterState = JsonDocument.Parse(
                await clustering.Content.ReadAsStringAsync());
            Assert.Equal("not-run", clusterState.RootElement.GetProperty("status").GetString());
            Assert.Equal("m25-dbscan-v1", clusterState.RootElement.GetProperty("policyVersion").GetString());

            using HttpResponseMessage advisory = await client.GetAsync(
                $"/api/review/provisional-clusters/review-groups/test-cluster/known-person-advisory?modelId=test-model&modelHash={TestModelHash}");
            Assert.Equal(HttpStatusCode.NotFound, advisory.StatusCode);

            Assert.Null(factory.Services.GetService<SqliteCatalogueDatabase>());
            Assert.IsType<PostgresCatalogueDatabase>(
                factory.Services.GetRequiredService<PostgresCatalogueDatabase>());
            Assert.False(File.Exists(sqlitePath));
        }
        finally
        {
            await using NpgsqlCommand dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {quotedDatabaseName} WITH (FORCE);";
            await dropDatabase.ExecuteNonQueryAsync();

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string QuoteIdentifier(string value) =>
        '"' + value.Replace("\"", "\"\"") + '"';
}
