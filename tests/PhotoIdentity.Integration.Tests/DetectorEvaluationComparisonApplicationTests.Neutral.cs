using System.Net.Http.Json;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed partial class DetectorEvaluationComparisonApplicationTests
{
    [Fact]
    public async Task Neutral_candidate_detection_resolves_review_without_changing_recall_or_false_duplicate_gate()
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

            string candidateDatabasePath = Path.Combine(directory, "neutral-candidate.db");
            var candidateDatabase = new SqliteCatalogueDatabase(candidateDatabasePath);
            await candidateDatabase.InitializeAsync();
            SeededRun candidate = await SeedRunAsync(
                candidateDatabase,
                Path.Combine(directory, "neutral-candidate-source"),
                groupBytes,
                smallBytes,
                baselineGroupDetections,
                [
                    new DetectionSeed(0.90, manualMiss.X, manualMiss.Y, manualMiss.Width, manualMiss.Height),
                    new DetectionSeed(0.72, 0.82, 0.12, 0.08, 0.10),
                ]);

            await using var factory = new DetectorEvaluationApiFactory(candidateDatabasePath, sessionRoot);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
                "/api/detector-evaluation/comparisons",
                new CreateDetectorEvaluationComparisonRequest(
                    "Neutral detector review",
                    baseline.Session.Id,
                    candidate.RunId.ToString(),
                    0.5));
            createResponse.EnsureSuccessStatusCode();
            DetectorEvaluationComparisonResponse comparison = Assert.IsType<DetectorEvaluationComparisonResponse>(
                await createResponse.Content.ReadFromJsonAsync<DetectorEvaluationComparisonResponse>());

            Assert.Equal(6, comparison.Overall.MatchedFaces);
            Assert.Equal(1d, comparison.Overall.Recall, 6);
            Assert.Equal(1, comparison.Overall.UnresolvedCandidateDetections);
            Assert.Equal(0, comparison.Overall.NeutralDetections);
            DetectorEvaluationComparisonPhotoResponse exceptionPhoto = Assert.Single(comparison.ExceptionPhotos);
            DetectorEvaluationComparisonCandidateDetectionResponse extraCandidate = Assert.Single(
                exceptionPhoto.ExceptionComponents.SelectMany(component => component.CandidateDetections));

            var correctionRequest = new SaveDetectorEvaluationComparisonPhotoRequest([], [], [], [], "Legitimate face outside the fixed countable-face rule.")
            {
                NeutralCandidateDetectionIds = [extraCandidate.Id],
            };
            using HttpResponseMessage correctionResponse = await client.PutAsJsonAsync(
                $"/api/detector-evaluation/comparisons/{comparison.Id}/photos/{exceptionPhoto.CandidateRevisionId}",
                correctionRequest);
            correctionResponse.EnsureSuccessStatusCode();
            DetectorEvaluationComparisonResponse corrected = Assert.IsType<DetectorEvaluationComparisonResponse>(
                await correctionResponse.Content.ReadFromJsonAsync<DetectorEvaluationComparisonResponse>());

            Assert.Equal(6, corrected.Overall.MatchedFaces);
            Assert.Equal(1d, corrected.Overall.Recall, 6);
            Assert.Equal(1, corrected.Overall.NeutralDetections);
            Assert.Equal(0, corrected.Overall.FalseDetections);
            Assert.Equal(0, corrected.Overall.DuplicateDetections);
            Assert.Equal(0, corrected.Overall.UnresolvedCandidateDetections);
            Assert.True(Assert.Single(corrected.ExceptionPhotos).IsResolved);
            Assert.True(corrected.M16Gate.FalseOrDuplicatePass);
            Assert.Equal("pending", corrected.M16Gate.Status);

            using HttpResponseMessage gateResponse = await client.PutAsJsonAsync(
                $"/api/detector-evaluation/comparisons/{comparison.Id}/m16-gate",
                new SaveDetectorEvaluationM16GateRequest(false, "No material category failure."));
            gateResponse.EnsureSuccessStatusCode();
            DetectorEvaluationComparisonResponse gated = Assert.IsType<DetectorEvaluationComparisonResponse>(
                await gateResponse.Content.ReadFromJsonAsync<DetectorEvaluationComparisonResponse>());
            Assert.Equal("pass", gated.M16Gate.Status);

            using HttpResponseMessage exportResponse = await client.GetAsync(
                $"/api/detector-evaluation/comparisons/{comparison.Id}/export.csv");
            exportResponse.EnsureSuccessStatusCode();
            string csv = await exportResponse.Content.ReadAsStringAsync();
            Assert.Contains("Neutral Detections", csv, StringComparison.Ordinal);
            Assert.Contains("Overall,All photos,2,6,6,0,0,100.00 %,0,0,1,0", csv, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }
}
