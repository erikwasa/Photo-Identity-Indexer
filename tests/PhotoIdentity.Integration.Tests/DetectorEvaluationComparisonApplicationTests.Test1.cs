using PhotoIdentity.Web.Contracts;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed partial class DetectorEvaluationComparisonApplicationTests
{
    [Fact]
    public async Task Frozen_ground_truth_compares_an_isolated_candidate_and_persists_only_exception_corrections()
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
            var baseline = await CreateFrozenBaselineAsync(directory, sessionRoot, groupBytes, smallBytes, baselineGroupDetections, manualMiss);
            Assert.Single(Directory.GetFiles(Path.Combine(sessionRoot, "ground-truth"), "*.json"));
            var candidate = await CreateAndCorrectCandidateAsync(directory, sessionRoot, baseline.Session, groupBytes, smallBytes, baselineGroupDetections, manualMiss);
            Assert.Single(Directory.GetFiles(Path.Combine(sessionRoot, "comparisons"), "*.json"));
            await AssertResumedComparisonAsync(candidate.DatabasePath, sessionRoot, candidate.ComparisonId);
        }
        finally { DeleteTemporaryDirectory(directory); }
    }
}
