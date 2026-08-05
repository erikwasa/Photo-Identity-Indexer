using System.Net.Http.Json;
using PhotoIdentity.Persistence.Sqlite;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed partial class DetectorEvaluationComparisonApplicationTests
{
    private static async Task<(string DatabasePath, string ComparisonId)> CreateAndCorrectCandidateAsync(
        string directory,
        string sessionRoot,
        DetectorEvaluationSessionSummaryResponse baselineSession,
        byte[] groupBytes,
        byte[] smallBytes,
        DetectionSeed[] baselineGroupDetections,
        DetectorEvaluationBoundingBoxResponse manualMiss)
    {
        string databasePath = Path.Combine(directory, "candidate.db");
        var database = new SqliteCatalogueDatabase(databasePath);
        await database.InitializeAsync();
        DetectionSeed[] candidateGroupDetections =
        [
            baselineGroupDetections[0] with { Confidence = 0.88 }, new(0.87, 0.055, 0.105, 0.10, 0.15),
            baselineGroupDetections[1] with { Confidence = 0.86 }, baselineGroupDetections[2] with { Confidence = 0.85 },
            baselineGroupDetections[3] with { Confidence = 0.84 },
        ];
        SeededRun candidate = await SeedRunAsync(
            database, Path.Combine(directory, "candidate-source"), groupBytes, smallBytes,
            candidateGroupDetections, [new DetectionSeed(0.81, manualMiss.X, manualMiss.Y, manualMiss.Width, manualMiss.Height)]);

        await using var factory = new DetectorEvaluationApiFactory(databasePath, sessionRoot);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/detector-evaluation/comparisons",
            new CreateDetectorEvaluationComparisonRequest("M16 confidence 0.8", baselineSession.Id, candidate.RunId.ToString(), 0.5));
        createResponse.EnsureSuccessStatusCode();
        var comparison = Assert.IsType<DetectorEvaluationComparisonResponse>(await createResponse.Content.ReadFromJsonAsync<DetectorEvaluationComparisonResponse>());
        Assert.Equal(6, comparison.Overall.CountableFaces);
        Assert.Equal(4, comparison.Overall.MatchedFaces);
        Assert.Equal(2, comparison.Overall.UnresolvedGroundTruthFaces);
        Assert.Equal("pending", comparison.M16Gate.Status);

        var exceptionPhoto = Assert.Single(comparison.ExceptionPhotos);
        Assert.Equal(3, exceptionPhoto.AutomaticMatchCount);
        var duplicate = Assert.Single(exceptionPhoto.ExceptionComponents.Where(component => component.Kind == "duplicate"));
        var unmatched = Assert.Single(exceptionPhoto.ExceptionComponents.Where(component => component.Kind == "unmatched"));
        Assert.Equal(2, duplicate.CandidateDetections.Count);
        Assert.Empty(unmatched.CandidateDetections);
        var ordered = duplicate.CandidateDetections.OrderBy(detection => detection.FaceNumber).ToArray();
        using HttpResponseMessage correctionResponse = await client.PutAsJsonAsync(
            $"/api/detector-evaluation/comparisons/{comparison.Id}/photos/{exceptionPhoto.CandidateRevisionId}",
            new SaveDetectorEvaluationComparisonPhotoRequest(
                [new(Assert.Single(duplicate.GroundTruthFaces).Id, ordered[0].Id)], [], [ordered[1].Id],
                [Assert.Single(unmatched.GroundTruthFaces).Id], "One duplicate and one remaining miss."));
        correctionResponse.EnsureSuccessStatusCode();
        using HttpResponseMessage gateResponse = await client.PutAsJsonAsync(
            $"/api/detector-evaluation/comparisons/{comparison.Id}/m16-gate",
            new SaveDetectorEvaluationM16GateRequest(false, "No material category beyond the measured recall miss."));
        gateResponse.EnsureSuccessStatusCode();
        return (databasePath, comparison.Id);
    }
}
