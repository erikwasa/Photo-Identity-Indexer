using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PhotoIdentity.Web;
using PhotoIdentity.Worker;
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

            using HttpResponseMessage firstSyncRequest = await client.PostAsync("/api/archive/sync/start", null);
            Assert.Equal(HttpStatusCode.Accepted, firstSyncRequest.StatusCode);
            ArchiveStatusResponse firstQueued = Assert.IsType<ArchiveStatusResponse>(
                await firstSyncRequest.Content.ReadFromJsonAsync<ArchiveStatusResponse>());
            Assert.NotNull(firstQueued.Advancement);
            Assert.Contains(firstQueued.Advancement.State, new[] { "queued", "syncing", "sync-complete" });

            ArchiveStatusResponse firstSync = await WaitForSyncCompletionAsync(client);
            Assert.Equal(2, firstSync.Totals.CurrentImages);
            Assert.Equal(2, firstSync.Totals.LocalImages);
            Assert.Equal(0, firstSync.Totals.OnlineOnlyImages);
            Assert.Equal(0, firstSync.Totals.AnalysedImages);
            Assert.Equal(2, firstSync.Totals.PendingImages);
            Assert.True(firstSync.AnalysisReady);
            Assert.NotNull(firstSync.ProfileHash);
            Assert.Single(firstSync.Folders);
            Assert.Equal("1970", firstSync.Folders[0].RelativeFolder);
            Assert.Equal("sync-complete", firstSync.Advancement?.State);
            Assert.False(firstSync.Advancement?.IsRunning);

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
            using HttpResponseMessage secondSyncRequest = await client.PostAsync("/api/archive/sync/start", null);
            Assert.Equal(HttpStatusCode.Accepted, secondSyncRequest.StatusCode);
            ArchiveStatusResponse secondSync = await WaitForSyncCompletionAsync(client);
            Assert.Equal(1, secondSync.Totals.CurrentImages);
            Assert.Equal(1, secondSync.Totals.MissingImages);

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

    [Fact]
    public async Task Throughput_diagnostics_are_resettable_and_do_not_expose_internal_subject_keys()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            ArchiveThroughputDiagnosticsResponse initialReset = Assert.IsType<ArchiveThroughputDiagnosticsResponse>(
                await (await client.PostAsync("/api/archive/diagnostics/throughput/reset", null))
                    .Content.ReadFromJsonAsync<ArchiveThroughputDiagnosticsResponse>());
            Assert.Empty(initialReset.Stages);
            Assert.Empty(initialReset.Counters);
            Assert.Empty(initialReset.HashReads);

            ArchiveThroughputMetrics metrics = factory.Services.GetRequiredService<ArchiveThroughputMetrics>();
            using (metrics.Measure(ArchiveThroughputMetricNames.ImageDecode))
            {
                await Task.Delay(1);
            }
            metrics.RecordCounter(ArchiveThroughputMetricNames.AnalysisAttempts, 2);
            metrics.RecordHashRead(
                ArchiveThroughputMetricNames.AnalysisHashKind,
                "private-revision-key-that-must-not-leak",
                1234);

            using HttpResponseMessage response = await client.GetAsync("/api/archive/diagnostics/throughput");
            response.EnsureSuccessStatusCode();
            Assert.Contains(
                "no-store",
                response.Headers.CacheControl?.ToString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
            string json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private-revision-key-that-must-not-leak", json, StringComparison.Ordinal);

            ArchiveThroughputDiagnosticsResponse snapshot = Assert.IsType<ArchiveThroughputDiagnosticsResponse>(
                await response.Content.ReadFromJsonAsync<ArchiveThroughputDiagnosticsResponse>());
            ArchiveThroughputStageMetricResponse stage = Assert.Single(snapshot.Stages);
            Assert.Equal(ArchiveThroughputMetricNames.ImageDecode, stage.Name);
            Assert.Equal(1, stage.Count);
            Assert.True(stage.TotalMilliseconds >= 0d);

            ArchiveThroughputCounterMetricResponse counter = Assert.Single(snapshot.Counters);
            Assert.Equal(ArchiveThroughputMetricNames.AnalysisAttempts, counter.Name);
            Assert.Equal(2, counter.Value);

            ArchiveThroughputHashReadMetricResponse hashRead = Assert.Single(snapshot.HashReads);
            Assert.Equal(ArchiveThroughputMetricNames.AnalysisHashKind, hashRead.Kind);
            Assert.Equal(1, hashRead.Count);
            Assert.Equal(1234, hashRead.Bytes);
            Assert.Equal(1, hashRead.SubjectCount);
            Assert.Equal(1d, hashRead.AverageReadsPerSubject);
            Assert.Equal(1, hashRead.MaxReadsPerSubject);

            ArchiveThroughputDiagnosticsResponse secondReset = Assert.IsType<ArchiveThroughputDiagnosticsResponse>(
                await (await client.PostAsync("/api/archive/diagnostics/throughput/reset", null))
                    .Content.ReadFromJsonAsync<ArchiveThroughputDiagnosticsResponse>());
            Assert.True(secondReset.Generation > initialReset.Generation);
            Assert.Empty(secondReset.Stages);
            Assert.Empty(secondReset.Counters);
            Assert.Empty(secondReset.HashReads);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Throughput_diagnostics_group_api_requests_without_recording_request_paths()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            await (await client.PostAsync("/api/archive/diagnostics/throughput/reset", null))
                .EnsureSuccessWithDiagnosticBodyAsync();
            using HttpResponseMessage archiveStatus = await client.GetAsync("/api/archive/status");
            Assert.Equal(HttpStatusCode.OK, archiveStatus.StatusCode);

            ArchiveThroughputDiagnosticsResponse snapshot = Assert.IsType<ArchiveThroughputDiagnosticsResponse>(
                await (await client.GetAsync("/api/archive/diagnostics/throughput"))
                    .Content.ReadFromJsonAsync<ArchiveThroughputDiagnosticsResponse>());
            ArchiveThroughputStageMetricResponse request = Assert.Single(snapshot.Stages);
            Assert.Equal(ArchiveThroughputMetricNames.ApiArchiveRequest, request.Name);
            Assert.Equal(1, request.Count);
            Assert.Contains(
                snapshot.Counters,
                value => value.Name == ArchiveThroughputMetricNames.ApiRequestSucceeded &&
                    value.Value == 1);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Archive_coverage_can_be_replaced_without_changing_source_or_deleting_catalogue_assets()
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

            using HttpResponseMessage configure = await client.PostAsJsonAsync(
                "/api/archive/include",
                new ArchiveIncludeRequest(archiveRoot, "1970"));
            configure.EnsureSuccessStatusCode();
            using HttpResponseMessage initialSync = await client.PostAsync("/api/archive/sync/start", null);
            Assert.Equal(HttpStatusCode.Accepted, initialSync.StatusCode);
            _ = await WaitForSyncCompletionAsync(client);

            using HttpResponseMessage replace = await client.PutAsJsonAsync(
                "/api/archive/coverage",
                new ArchiveCoverageUpdateRequest(["1970/02"]));
            replace.EnsureSuccessStatusCode();
            string replaceJson = await replace.Content.ReadAsStringAsync();
            Assert.DoesNotContain(archiveRoot, replaceJson, StringComparison.OrdinalIgnoreCase);
            ArchiveStatusResponse narrowed = Assert.IsType<ArchiveStatusResponse>(
                await replace.Content.ReadFromJsonAsync<ArchiveStatusResponse>());
            Assert.Equal("Kamerabilder", narrowed.RootName);
            Assert.Equal(["1970/02"], narrowed.IncludedFolders);

            ArchiveItemPageResponse retainedJanuary = Assert.IsType<ArchiveItemPageResponse>(
                await client.GetFromJsonAsync<ArchiveItemPageResponse>(
                    "/api/archive/items?folder=1970/01&state=all&offset=0&limit=50"));
            ArchiveItemStatusResponse retainedItem = Assert.Single(retainedJanuary.Items);
            Assert.Equal("1970/01/one.jpg", retainedItem.RelativePath);

            ArchiveStatusResponse normalized = Assert.IsType<ArchiveStatusResponse>(
                await (await client.PutAsJsonAsync(
                    "/api/archive/coverage",
                    new ArchiveCoverageUpdateRequest(["1970/01", "1970"])))
                    .Content.ReadFromJsonAsync<ArchiveStatusResponse>());
            Assert.Equal(["1970"], normalized.IncludedFolders);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static async Task<ArchiveStatusResponse> WaitForSyncCompletionAsync(HttpClient client)
    {
        ArchiveAdvancementStatusResponse? lastAdvancement = null;
        for (int attempt = 0; attempt < 120; attempt++)
        {
            ArchiveStatusResponse status = Assert.IsType<ArchiveStatusResponse>(
                await client.GetFromJsonAsync<ArchiveStatusResponse>("/api/archive/status"));
            ArchiveAdvancementStatusResponse? advancement = status.Advancement;
            lastAdvancement = advancement;
            if (string.Equals(advancement?.State, "sync-complete", StringComparison.Ordinal))
            {
                return status;
            }

            if (advancement is not null &&
                string.Equals(advancement.State, "blocked", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    advancement.Message ?? "Archive synchronization was blocked.");
            }

            // The real Archive UI polls every two seconds. Keep integration polling frequent enough
            // for a fast test while avoiding a tight polling loop that can starve the background
            // writer under sharded CI load.
            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Archive synchronization did not complete within the integration-test polling window. Last state: {lastAdvancement?.State ?? "none"}. Message: {lastAdvancement?.Message ?? "none"}.");
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

        protected override bool DisableBackgroundWorkers => false;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("PhotoIdentity:Postgres:ConnectionString", PostgresTestCatalogueDatabase.GetCompatibilityConnectionString(_databasePath));
            builder.UseSetting("PhotoIdentity:RepositoryRoot", _repositoryRoot);
            builder.UseSetting("PhotoIdentity:ArchiveAnalysisOutputRoot", _analysisOutputRoot);
        }
    }
}
