using System.Net;
using System.Net.Http.Json;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed partial class DetectorEvaluationComparisonApplicationTests
{
    [Fact]
    public async Task Comparison_photo_content_resolves_after_switching_back_to_the_baseline_catalogue()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sessionRoot = Path.Combine(directory, "private-sessions");
            byte[] groupBytes = [1, 2, 3, 4, 5, 6];
            byte[] smallBytes = [7, 8, 9, 10];
            DetectionSeed[] baselineGroupDetections =
            [
                new(0.99, 0.05, 0.10, 0.10, 0.15), new(0.98, 0.22, 0.10, 0.10, 0.15),
                new(0.97, 0.39, 0.10, 0.10, 0.15), new(0.96, 0.56, 0.10, 0.10, 0.15),
                new(0.95, 0.73, 0.10, 0.10, 0.15),
            ];
            var manualMiss = new DetectorEvaluationBoundingBoxResponse(0.40, 0.45, 0.08, 0.12);
            var baseline = await CreateFrozenBaselineAsync(
                directory,
                sessionRoot,
                groupBytes,
                smallBytes,
                baselineGroupDetections,
                manualMiss);
            var candidate = await CreateAndCorrectCandidateAsync(
                directory,
                sessionRoot,
                baseline.Session,
                groupBytes,
                smallBytes,
                baselineGroupDetections,
                manualMiss);

            await using var factory = new DetectorEvaluationApiFactory(baseline.DatabasePath, sessionRoot);
            using HttpClient client = factory.CreateClient();
            DetectorEvaluationComparisonResponse comparison = Assert.IsType<DetectorEvaluationComparisonResponse>(
                await client.GetFromJsonAsync<DetectorEvaluationComparisonResponse>(
                    $"/api/detector-evaluation/comparisons/{candidate.ComparisonId}"));
            DetectorEvaluationComparisonPhotoResponse photo = Assert.Single(comparison.ExceptionPhotos);

            using HttpResponseMessage legacyResponse = await client.GetAsync(
                $"/api/detector-evaluation/photos/{photo.CandidateRevisionId}/content");
            Assert.Equal(HttpStatusCode.NotFound, legacyResponse.StatusCode);

            Assert.Equal(
                $"/api/detector-evaluation/comparisons/{comparison.Id}/photos/{photo.CandidateRevisionId}/content",
                photo.ContentUrl);
            using HttpResponseMessage contentResponse = await client.GetAsync(photo.ContentUrl);
            contentResponse.EnsureSuccessStatusCode();
            Assert.Equal("image/jpeg", contentResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(groupBytes, await contentResponse.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }
}
