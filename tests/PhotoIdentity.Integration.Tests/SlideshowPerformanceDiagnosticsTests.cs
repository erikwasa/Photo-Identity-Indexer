using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PhotoIdentity.Core.Collections;
using PhotoIdentity.Web;
using PhotoIdentity.Worker;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class SlideshowPerformanceDiagnosticsTests
{
    [Fact]
    public async Task Slideshow_library_and_snapshot_emit_privacy_safe_stage_timings()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            ISmartCollectionRepository definitions =
                factory.Services.GetRequiredService<ISmartCollectionRepository>();
            SmartCollectionDefinition collection = await definitions.CreateAsync(
                "Timing collection",
                new SmartCollectionFilter());

            using HttpResponseMessage reset = await client.PostAsync(
                "/api/archive/diagnostics/throughput/reset",
                content: null);
            reset.EnsureSuccessStatusCode();

            using HttpResponseMessage library = await client.GetAsync("/api/slideshows/collections");
            library.EnsureSuccessStatusCode();

            using HttpResponseMessage snapshot = await client.PostAsync(
                $"/api/smart-collections/{collection.Id}/slideshow-snapshot",
                content: null);
            snapshot.EnsureSuccessStatusCode();

            ArchiveThroughputDiagnosticsResponse diagnostics =
                Assert.IsType<ArchiveThroughputDiagnosticsResponse>(
                    await client.GetFromJsonAsync<ArchiveThroughputDiagnosticsResponse>(
                        "/api/archive/diagnostics/throughput"));

            ArchiveThroughputStageMetricResponse libraryStage = Assert.Single(
                diagnostics.Stages,
                stage => stage.Name == ArchiveThroughputMetricNames.SlideshowLibraryLoad);
            Assert.Equal(1, libraryStage.Count);
            Assert.True(libraryStage.TotalMilliseconds >= 0d);

            ArchiveThroughputStageMetricResponse snapshotStage = Assert.Single(
                diagnostics.Stages,
                stage => stage.Name == ArchiveThroughputMetricNames.SlideshowSnapshotCreation);
            Assert.Equal(1, snapshotStage.Count);
            Assert.True(snapshotStage.TotalMilliseconds >= 0d);

            string json = await (await client.GetAsync("/api/archive/diagnostics/throughput"))
                .Content.ReadAsStringAsync();
            Assert.DoesNotContain("Timing collection", json, StringComparison.Ordinal);
            Assert.DoesNotContain(collection.Id.ToString(), json, StringComparison.OrdinalIgnoreCase);
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
            "photoidentity-slideshow-performance-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
