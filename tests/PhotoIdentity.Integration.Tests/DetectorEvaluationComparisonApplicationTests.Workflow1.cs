using System.Net.Http.Json;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed partial class DetectorEvaluationComparisonApplicationTests
{
    private static async Task<(string DatabasePath, DetectorEvaluationSessionSummaryResponse Session)> CreateFrozenBaselineAsync(
        string directory,
        string sessionRoot,
        byte[] groupBytes,
        byte[] smallBytes,
        DetectionSeed[] groupDetections,
        DetectorEvaluationBoundingBoxResponse manualMiss)
    {
        string databasePath = Path.Combine(directory, "baseline.db");
        var database = new SqliteCatalogueDatabase(databasePath);
        await database.InitializeAsync();
        SeededRun baseline = await SeedRunAsync(database, Path.Combine(directory, "baseline-source"), groupBytes, smallBytes, groupDetections, []);

        await using var factory = new DetectorEvaluationApiFactory(databasePath, sessionRoot);
        using HttpClient client = factory.CreateClient();
        var createRequest = new CreateDetectorEvaluationSessionRequest(
            "M16 confidence 0.9 baseline",
            baseline.RunId.ToString(),
            [
                new("R001", "R001__group.jpg", "Representative", "Pilot representative", "Group", 5, null),
                new("R002", "R002__small.jpg", "Difficult", "External difficult", "Small / distant", 1, null),
            ]);
        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/detector-evaluation/sessions", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var summary = Assert.IsType<DetectorEvaluationSessionSummaryResponse>(await createResponse.Content.ReadFromJsonAsync<DetectorEvaluationSessionSummaryResponse>());
        var session = Assert.IsType<DetectorEvaluationSessionResponse>(await client.GetFromJsonAsync<DetectorEvaluationSessionResponse>($"/api/detector-evaluation/sessions/{summary.Id}"));

        var groupPhoto = Assert.Single(session.Photos, photo => photo.PhotoName == "R001__group.jpg");
        using HttpResponseMessage groupSave = await client.PutAsJsonAsync(
            $"/api/detector-evaluation/sessions/{summary.Id}/photos/{groupPhoto.RevisionId}",
            new SaveDetectorEvaluationPhotoReviewRequest(
                groupPhoto.Detections.Select(detection => new DetectorEvaluationDetectionJudgementRequest(detection.Id, "correct")).ToArray(),
                [], null, "Five countable faces confirmed."));
        groupSave.EnsureSuccessStatusCode();

        var smallPhoto = Assert.Single(session.Photos, photo => photo.PhotoName == "R002__small.jpg");
        using HttpResponseMessage smallSave = await client.PutAsJsonAsync(
            $"/api/detector-evaluation/sessions/{summary.Id}/photos/{smallPhoto.RevisionId}",
            new SaveDetectorEvaluationPhotoReviewRequest(
                [], [new DetectorEvaluationMissedFaceRequest(Guid.NewGuid().ToString("D"), manualMiss)],
                "Small / distant", "Marked directly on the source photo."));
        smallSave.EnsureSuccessStatusCode();

        using HttpResponseMessage freezeResponse = await client.PostAsync($"/api/detector-evaluation/sessions/{summary.Id}/ground-truth", null);
        freezeResponse.EnsureSuccessStatusCode();
        var frozen = Assert.IsType<DetectorEvaluationGroundTruthSummaryResponse>(await freezeResponse.Content.ReadFromJsonAsync<DetectorEvaluationGroundTruthSummaryResponse>());
        Assert.Equal(2, frozen.PhotoCount);
        Assert.Equal(6, frozen.FaceCount);
        return (databasePath, summary);
    }
}
