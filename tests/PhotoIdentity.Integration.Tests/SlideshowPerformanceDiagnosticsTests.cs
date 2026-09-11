using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PhotoIdentity.Api;
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

    [Fact]
    public async Task Browser_playback_samples_are_bounded_and_record_identity_free_timings()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string databasePath = Path.Combine(directory, "catalogue.db");
            await using PhotoIdentityApiTestFactory factory = new(databasePath);
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage reset = await client.PostAsync(
                "/api/archive/diagnostics/throughput/reset",
                content: null);
            reset.EnsureSuccessStatusCode();

            SlideshowBrowserPlaybackTimingBatchRequest batch = new(
            [
                new SlideshowBrowserPlaybackTimingRequest(
                    Sequence: 3,
                    PresentationMilliseconds: 125.5,
                    ResourceMilliseconds: 40.25,
                    Prefetched: true),
                new SlideshowBrowserPlaybackTimingRequest(
                    Sequence: 4,
                    PresentationMilliseconds: 80,
                    ResourceMilliseconds: null,
                    Prefetched: false),
            ]);

            using HttpResponseMessage sample = await client.PostAsJsonAsync(
                "/api/slideshows/diagnostics/playback",
                batch);
            Assert.Equal(HttpStatusCode.NoContent, sample.StatusCode);

            ArchiveThroughputDiagnosticsResponse diagnostics =
                Assert.IsType<ArchiveThroughputDiagnosticsResponse>(
                    await client.GetFromJsonAsync<ArchiveThroughputDiagnosticsResponse>(
                        "/api/archive/diagnostics/throughput"));

            ArchiveThroughputStageMetricResponse presentation = Assert.Single(
                diagnostics.Stages,
                stage => stage.Name == ArchiveThroughputMetricNames.SlideshowBrowserImagePresentation);
            Assert.Equal(2, presentation.Count);
            Assert.Equal(205.5, presentation.TotalMilliseconds, precision: 3);

            ArchiveThroughputStageMetricResponse position3 = Assert.Single(
                diagnostics.Stages,
                stage => stage.Name ==
                    ArchiveThroughputMetricNames.SlideshowBrowserImagePresentationPositionPrefix + "03");
            Assert.Equal(125.5, position3.TotalMilliseconds, precision: 3);

            ArchiveThroughputStageMetricResponse position4 = Assert.Single(
                diagnostics.Stages,
                stage => stage.Name ==
                    ArchiveThroughputMetricNames.SlideshowBrowserImagePresentationPositionPrefix + "04");
            Assert.Equal(80, position4.TotalMilliseconds, precision: 3);

            ArchiveThroughputStageMetricResponse resource = Assert.Single(
                diagnostics.Stages,
                stage => stage.Name == ArchiveThroughputMetricNames.SlideshowBrowserImageResource);
            Assert.Equal(1, resource.Count);
            Assert.Equal(40.25, resource.TotalMilliseconds, precision: 3);

            ArchiveThroughputCounterMetricResponse prefetchHit = Assert.Single(
                diagnostics.Counters,
                counter => counter.Name == ArchiveThroughputMetricNames.SlideshowBrowserPrefetchHits);
            Assert.Equal(1, prefetchHit.Value);

            ArchiveThroughputCounterMetricResponse prefetchMiss = Assert.Single(
                diagnostics.Counters,
                counter => counter.Name == ArchiveThroughputMetricNames.SlideshowBrowserPrefetchMisses);
            Assert.Equal(1, prefetchMiss.Value);

            using HttpResponseMessage invalid = await client.PostAsJsonAsync(
                "/api/slideshows/diagnostics/playback",
                new SlideshowBrowserPlaybackTimingBatchRequest(
                [
                    new SlideshowBrowserPlaybackTimingRequest(
                        Sequence: 51,
                        PresentationMilliseconds: 1,
                        ResourceMilliseconds: null,
                        Prefetched: false),
                ]));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
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
