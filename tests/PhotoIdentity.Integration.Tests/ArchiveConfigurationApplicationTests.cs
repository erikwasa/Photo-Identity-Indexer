using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Core.Sources;
using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveConfigurationApplicationTests
{
    [Fact]
    public async Task Configuration_endpoint_does_not_depend_on_archive_status_aggregation()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string archiveRoot = Path.Combine(directory, "Kamerabilder");
            Directory.CreateDirectory(Path.Combine(archiveRoot, "1970", "01"));
            string databasePath = Path.Combine(directory, "catalogue.db");

            await using (PhotoIdentityApiTestFactory seedFactory = new(databasePath))
            {
                using HttpClient seedClient = seedFactory.CreateClient();
                using HttpResponseMessage include = await seedClient.PostAsJsonAsync(
                    "/api/archive/include",
                    new ArchiveIncludeRequest(archiveRoot, "1970/01"));
                await include.EnsureSuccessWithDiagnosticBodyAsync("configure archive for Settings test");
            }

            await using PhotoIdentityApiTestFactory factory = new(
                databasePath,
                builder => builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IArchiveStatusRepository>();
                    services.AddSingleton<IArchiveStatusRepository>(new ThrowingArchiveStatusRepository());
                }));
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage configurationResponse = await client.GetAsync("/api/archive/configuration");
            configurationResponse.EnsureSuccessStatusCode();
            Assert.Contains(
                "no-store",
                configurationResponse.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            string json = await configurationResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain(archiveRoot, json, StringComparison.OrdinalIgnoreCase);

            ArchiveConfigurationResponse configuration = Assert.IsType<ArchiveConfigurationResponse>(
                await configurationResponse.Content.ReadFromJsonAsync<ArchiveConfigurationResponse>());
            Assert.True(configuration.Configured);
            Assert.Equal("Kamerabilder", configuration.RootName);
            Assert.Equal(["1970/01"], configuration.IncludedFolders);

            using HttpResponseMessage fullStatus = await client.GetAsync("/api/archive/status");
            Assert.Equal(HttpStatusCode.BadRequest, fullStatus.StatusCode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Configuration_endpoint_returns_empty_configuration_before_archive_setup()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            ArchiveConfigurationResponse configuration = Assert.IsType<ArchiveConfigurationResponse>(
                await client.GetFromJsonAsync<ArchiveConfigurationResponse>("/api/archive/configuration"));
            Assert.False(configuration.Configured);
            Assert.Null(configuration.RootName);
            Assert.Empty(configuration.IncludedFolders);
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
            "photoidentity-settings-configuration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private sealed class ThrowingArchiveStatusRepository : IArchiveStatusRepository
    {
        private static InvalidOperationException Failure() =>
            new("Archive status aggregation must not run for the Settings configuration endpoint.");

        public Task<CatalogueArchiveFolderStatus> GetStatusAsync(
            SourceId sourceId,
            string relativeFolder,
            Sha256Digest? profileHash,
            CancellationToken cancellationToken = default) =>
            Task.FromException<CatalogueArchiveFolderStatus>(Failure());

        public Task<CatalogueArchiveItemPage> GetItemsAsync(
            SourceId sourceId,
            string relativeFolder,
            Sha256Digest? profileHash,
            string state,
            int offset,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromException<CatalogueArchiveItemPage>(Failure());

        public Task<CatalogueArchiveItemPage> GetItemsAsync(
            SourceId sourceId,
            string relativeFolder,
            Sha256Digest? profileHash,
            string availability,
            string verification,
            string analysis,
            int offset,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromException<CatalogueArchiveItemPage>(Failure());

        public Task<CatalogueArchiveRunStatus?> GetLatestRunAsync(
            Sha256Digest profileHash,
            CancellationToken cancellationToken = default) =>
            Task.FromException<CatalogueArchiveRunStatus?>(Failure());
    }
}
