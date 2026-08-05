using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using PhotoIdentity.Core.Identifiers;
using PhotoIdentity.Core.Recognition;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed partial class DetectorEvaluationComparisonApplicationTests
{
    [Fact]
    public async Task Candidate_attachment_rejects_a_changed_source_revision()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string sessionRoot = Path.Combine(directory, "private-sessions");
            string baselineDatabasePath = Path.Combine(directory, "baseline.db");
            SqliteCatalogueDatabase baselineDatabase = new(baselineDatabasePath);
            await baselineDatabase.InitializeAsync();
            SeededRun baseline = await SeedRunAsync(
                baselineDatabase,
                Path.Combine(directory, "baseline-source"),
                [1, 2, 3],
                [4, 5, 6],
                [new DetectionSeed(0.9, 0.1, 0.1, 0.2, 0.2)],
                []);

            string baselineSessionId;
            await using (DetectorEvaluationApiFactory baselineFactory = new(baselineDatabasePath, sessionRoot))
            using (HttpClient client = baselineFactory.CreateClient())
            {
                using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
                    "/api/detector-evaluation/sessions",
                    new CreateDetectorEvaluationSessionRequest(
                        "Baseline",
                        baseline.RunId.ToString(),
                        [
                            new("R001", "R001__group.jpg", "Representative", "Pilot", "Group", 1, null),
                            new("R002", "R002__small.jpg", "Difficult", "External", "Small", 0, null),
                        ]));
                createResponse.EnsureSuccessStatusCode();
                DetectorEvaluationSessionSummaryResponse created = Assert.IsType<DetectorEvaluationSessionSummaryResponse>(
                    await createResponse.Content.ReadFromJsonAsync<DetectorEvaluationSessionSummaryResponse>());
                baselineSessionId = created.Id;
                DetectorEvaluationSessionResponse session = Assert.IsType<DetectorEvaluationSessionResponse>(
                    await client.GetFromJsonAsync<DetectorEvaluationSessionResponse>(
                        $"/api/detector-evaluation/sessions/{created.Id}"));
                foreach (DetectorEvaluationSessionPhotoResponse photo in session.Photos)
                {
                    using HttpResponseMessage save = await client.PutAsJsonAsync(
                        $"/api/detector-evaluation/sessions/{created.Id}/photos/{photo.RevisionId}",
                        new SaveDetectorEvaluationPhotoReviewRequest(
                            photo.Detections.Select(detection => new DetectorEvaluationDetectionJudgementRequest(detection.Id, "correct")).ToArray(),
                            [],
                            null,
                            null));
                    save.EnsureSuccessStatusCode();
                }
                using HttpResponseMessage freeze = await client.PostAsync(
                    $"/api/detector-evaluation/sessions/{created.Id}/ground-truth",
                    null);
                freeze.EnsureSuccessStatusCode();
            }

            string candidateDatabasePath = Path.Combine(directory, "candidate.db");
            SqliteCatalogueDatabase candidateDatabase = new(candidateDatabasePath);
            await candidateDatabase.InitializeAsync();
            SeededRun candidate = await SeedRunAsync(
                candidateDatabase,
                Path.Combine(directory, "candidate-source"),
                [9, 9, 9],
                [4, 5, 6],
                [new DetectionSeed(0.8, 0.1, 0.1, 0.2, 0.2)],
                []);

            await using DetectorEvaluationApiFactory candidateFactory = new(candidateDatabasePath, sessionRoot);
            using HttpClient candidateClient = candidateFactory.CreateClient();
            using HttpResponseMessage comparisonResponse = await candidateClient.PostAsJsonAsync(
                "/api/detector-evaluation/comparisons",
                new CreateDetectorEvaluationComparisonRequest(
                    "Changed source",
                    baselineSessionId,
                    candidate.RunId.ToString(),
                    0.5));
            Assert.Equal(System.Net.HttpStatusCode.BadRequest, comparisonResponse.StatusCode);
            string payload = await comparisonResponse.Content.ReadAsStringAsync();
            Assert.Contains("does not match the frozen SHA-256", payload, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

}
