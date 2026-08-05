using System.Net.Http.Json;
using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed partial class DetectorEvaluationComparisonApplicationTests
{
    private static async Task AssertResumedComparisonAsync(string databasePath, string sessionRoot, string comparisonId)
    {
        await using var factory = new DetectorEvaluationApiFactory(databasePath, sessionRoot);
        using HttpClient client = factory.CreateClient();
        var resumed = Assert.IsType<DetectorEvaluationComparisonResponse>(
            await client.GetFromJsonAsync<DetectorEvaluationComparisonResponse>($"/api/detector-evaluation/comparisons/{comparisonId}"));
        Assert.Equal(5, resumed.Overall.MatchedFaces);
        Assert.Equal(1, resumed.Overall.MissedFaces);
        Assert.Equal(1, resumed.Overall.DuplicateDetections);
        Assert.Equal(0, resumed.Overall.UnresolvedGroundTruthFaces);
        Assert.Equal(0, resumed.Overall.UnresolvedCandidateDetections);
        Assert.Equal(5d / 6d, resumed.Overall.Recall, 6);
        Assert.Equal(4d / 5d, resumed.FivePlusFaces.Recall, 6);
        Assert.True(Assert.Single(resumed.ExceptionPhotos).IsResolved);
        Assert.Equal("fail", resumed.M16Gate.Status);
        Assert.False(resumed.M16Gate.OverallRecallPass);
        Assert.False(resumed.M16Gate.FivePlusRecallPass);
        Assert.True(resumed.M16Gate.FalseOrDuplicatePass);
        Assert.True(resumed.M16Gate.MaterialCategoryPass is true);
        Assert.Equal(4d / 5d, Assert.Single(resumed.SourceGroups.Where(group => group.Group == "Pilot representative")).Metrics.Recall, 6);
        Assert.Equal(1, Assert.Single(resumed.Categories.Where(group => group.Group == "Small / distant")).Metrics.MatchedFaces);

        using HttpResponseMessage exportResponse = await client.GetAsync($"/api/detector-evaluation/comparisons/{comparisonId}/export.csv");
        exportResponse.EnsureSuccessStatusCode();
        string csv = await exportResponse.Content.ReadAsStringAsync();
        Assert.Contains("Overall,All photos", csv, StringComparison.Ordinal);
        Assert.Contains("Five-plus-face", csv, StringComparison.Ordinal);
        Assert.Contains("Source group,Pilot representative", csv, StringComparison.Ordinal);
        Assert.Contains("Category,Small / distant", csv, StringComparison.Ordinal);
        Assert.Contains("M16 Gate,Status,Target,Observed,Pass", csv, StringComparison.Ordinal);
    }
}
