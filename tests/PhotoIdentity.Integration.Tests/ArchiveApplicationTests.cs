using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PhotoIdentity.Web;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class ArchiveApplicationTests
{
    [Fact]
    public async Task Archive_api_configures_syncs_and_reports_exact_profile_coverage_without_exposing_root()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string archiveRoot = Path.Combine(directory, "Kamerabilder");
            string january = Path.Combine(archiveRoot, "1970", "01");
            string february = Path.Combine(archiveRoot, "1970", "02");
            Directory.CreateDirectory(january);
            Directory.CreateDirectory(february);
            await File.WriteAllBytesAsync(Path.Combine(january, "one.jpg"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(february, "two.jpg"), [4, 5, 6]);

            string databasePath = Path.Combine(directory, "catalogue.db");
            await using ArchiveApiFactory factory = new(
                databasePath,
                FindRepositoryRoot(),
                Path.Combine(directory, "analysis-output"));
            using HttpClient client = factory.CreateClient();

            ArchiveStatusResponse initial = Assert.IsType<ArchiveStatusResponse>(
                await client.GetFromJsonAsync<ArchiveStatusResponse>("/api/archive/status"));
            Assert.False(initial.Configured);

            using HttpResponseMessage includeJanuary = await client.PostAsJsonAsync(
                "/api/archive/include",
                new ArchiveIncludeRequest(archiveRoot, "1970/01"));
            includeJanuary.EnsureSuccessStatusCode();
            Assert.Contains("no-store", includeJanuary.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            string includeJson = await includeJanuary.Content.ReadAsStringAsync();
            Assert.DoesNotContain(archiveRoot, includeJson, StringComparison.OrdinalIgnoreCase);

            _ = await client.PostAsJsonAsync(
                "/api/archive/include",
                new ArchiveIncludeRequest(null, "1970/02"));
            ArchiveStatusResponse parent = Assert.IsType<ArchiveStatusResponse>(
                await (await client.PostAsJsonAsync(
                    "/api/archive/include",
                    new ArchiveIncludeRequest(null, "1970")))
                    .Content.ReadFromJsonAsync<ArchiveStatusResponse>());
            Assert.Equal(["1970"], parent.IncludedFolders);

            ArchiveSyncResponse firstSync = Assert.IsType<ArchiveSyncResponse>(
                await (await client.PostAsync("/api/archive/sync", null))
                    .Content.ReadFromJsonAsync<ArchiveSyncResponse>());
            Assert.Equal(2, firstSync.NewRevisions);
            Assert.Equal(2, firstSync.LocalFiles);
            Assert.Equal(0, firstSync.OnlineOnlyFiles);
            Assert.Equal(2, firstSync.Status.Totals.CurrentImages);
            Assert.Equal(2, firstSync.Status.Totals.LocalImages);
            Assert.Equal(0, firstSync.Status.Totals.OnlineOnlyImages);
            Assert.Equal(0, firstSync.Status.Totals.AnalysedImages);
            Assert.Equal(2, firstSync.Status.Totals.PendingImages);
            Assert.True(firstSync.Status.AnalysisReady);
            Assert.NotNull(firstSync.Status.ProfileHash);
            Assert.Single(firstSync.Status.Folders);
            Assert.Equal("1970", firstSync.Status.Folders[0].RelativeFolder);

            using HttpResponseMessage itemResponse = await client.GetAsync(
                "/api/archive/items?folder=1970&state=pending&offset=0&limit=50");
            itemResponse.EnsureSuccessStatusCode();
            Assert.Contains("no-store", itemResponse.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            string itemJson = await itemResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain(archiveRoot, itemJson, StringComparison.OrdinalIgnoreCase);
            ArchiveItemPageResponse itemPage = Assert.IsType<ArchiveItemPageResponse>(
                await itemResponse.Content.ReadFromJsonAsync<ArchiveItemPageResponse>());
            Assert.Equal(2, itemPage.Total);
            Assert.All(itemPage.Items, item => Assert.Equal("local", item.Availability));
            Assert.All(itemPage.Items, item => Assert.Equal("pending", item.AnalysisState));

            File.Delete(Path.Combine(january, "one.jpg"));
            ArchiveSyncResponse secondSync = Assert.IsType<ArchiveSyncResponse>(
                await (await client.PostAsync("/api/archive/sync", null))
                    .Content.ReadFromJsonAsync<ArchiveSyncResponse>());
            Assert.Equal(1, secondSync.MarkedMissing);
            Assert.Equal(1, secondSync.Status.Totals.CurrentImages);
            Assert.Equal(1, secondSync.Status.Totals.MissingImages);

            ArchiveItemPageResponse missing = Assert.IsType<ArchiveItemPageResponse>(
                await client.GetFromJsonAsync<ArchiveItemPageResponse>(
                    "/api/archive/items?folder=1970&state=missing&offset=0&limit=50"));
            ArchiveItemStatusResponse missingItem = Assert.Single(missing.Items);
            Assert.Equal("1970/01/one.jpg", missingItem.RelativePath);
            Assert.Equal("missing", missingItem.AnalysisState);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "models",
                    "manifests",
                    "centerface-2019-fp32.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The repository root could not be found from the test output directory.");
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

    private sealed class ArchiveApiFactory : WebApplicationFactory<PhotoIdentity.Api.Program>
    {
        private readonly string _databasePath;
        private readonly string _repositoryRoot;
        private readonly string _analysisOutputRoot;

        public ArchiveApiFactory(string databasePath, string repositoryRoot, string analysisOutputRoot)
        {
            _databasePath = databasePath;
            _repositoryRoot = repositoryRoot;
            _analysisOutputRoot = analysisOutputRoot;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:DatabasePath", _databasePath);
            builder.UseSetting("PhotoIdentity:RepositoryRoot", _repositoryRoot);
            builder.UseSetting("PhotoIdentity:ArchiveAnalysisOutputRoot", _analysisOutputRoot);
        }
    }
}
