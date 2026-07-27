using System.Text.Json;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class EvaluationCommandTests
{
    [Fact]
    public async Task Evaluate_is_reproducible_and_selects_threshold_from_validation_only()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string datasetPath = Path.Combine(directory, "dataset.json");
            string firstReportPath = Path.Combine(directory, "first-report.json");
            string secondReportPath = Path.Combine(directory, "second-report.json");
            await File.WriteAllTextAsync(datasetPath, ValidDatasetJson);

            int firstExitCode = await RunAsync(datasetPath, firstReportPath);
            int secondExitCode = await RunAsync(datasetPath, secondReportPath);

            Assert.Equal(0, firstExitCode);
            Assert.Equal(0, secondExitCode);
            Assert.Equal(
                await File.ReadAllBytesAsync(firstReportPath),
                await File.ReadAllBytesAsync(secondReportPath));

            using JsonDocument document = JsonDocument.Parse(
                await File.ReadAllTextAsync(firstReportPath));
            JsonElement root = document.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("synthetic-split-discipline", root.GetProperty("datasetId").GetString());
            Assert.Equal("pipeline-v1", root.GetProperty("pipelineVersion").GetString());
            Assert.Equal(64, root.GetProperty("inputSha256").GetString()!.Length);
            Assert.Equal("validation", root.GetProperty("thresholdSelectionSplit").GetString());
            Assert.Equal(0.95, root.GetProperty("selectedThreshold").GetDouble(), 6);

            JsonElement detector = root.GetProperty("detector");
            Assert.Equal("yunet", detector.GetProperty("modelId").GetString());
            Assert.Equal(new string('a', 64), detector.GetProperty("modelHash").GetString());
            JsonElement embedder = root.GetProperty("embedder");
            Assert.Equal("sface", embedder.GetProperty("modelId").GetString());
            Assert.Equal(new string('b', 64), embedder.GetProperty("modelHash").GetString());
            Assert.Equal(2, embedder.GetProperty("dimensions").GetInt32());

            JsonElement validationMetrics = root
                .GetProperty("validation")
                .GetProperty("metrics");
            Assert.Equal(0.75, validationMetrics.GetProperty("detectorRecall").GetDouble(), 6);
            Assert.Equal(1, validationMetrics.GetProperty("unknownRejectedCount").GetInt32());
            Assert.True(validationMetrics.GetProperty("imagesPerSecond").GetDouble() > 0);

            JsonElement selectedTestMetrics = root
                .GetProperty("test")
                .GetProperty("metrics");
            double selectedTestScore = selectedTestMetrics
                .GetProperty("balancedIdentityScore")
                .GetDouble();
            JsonElement testPreferredPoint = Assert.Single(
                root.GetProperty("testThresholdSweep").EnumerateArray(),
                item => Math.Abs(item.GetProperty("threshold").GetDouble() - 0.8) < 0.000001);
            double testPreferredScore = testPreferredPoint
                .GetProperty("metrics")
                .GetProperty("balancedIdentityScore")
                .GetDouble();
            Assert.True(testPreferredScore > selectedTestScore);

            JsonElement projection = root.GetProperty("archiveProjection");
            Assert.Equal(100000, projection.GetProperty("archiveImageCount").GetInt64());
            Assert.Equal("GBP", projection.GetProperty("currency").GetString());
            Assert.Equal(1.5m, projection.GetProperty("hourlyCost").GetDecimal());
            Assert.True(projection.GetProperty("estimatedHours").GetDouble() > 0);
            Assert.True(projection.GetProperty("estimatedCost").GetDecimal() > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Evaluate_rejects_sample_ids_reused_across_splits()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string datasetPath = Path.Combine(directory, "overlap.json");
            string reportPath = Path.Combine(directory, "report.json");
            await File.WriteAllTextAsync(
                datasetPath,
                ValidDatasetJson.Replace("t-known-1", "v-known-1", StringComparison.Ordinal));
            StringWriter output = new();
            StringWriter error = new();

            int exitCode = await PhotoIdentity.Cli.Program.RunAsync(
                [
                    "evaluate",
                    "--dataset", datasetPath,
                    "--output", reportPath,
                ],
                output,
                error);

            Assert.Equal(2, exitCode);
            Assert.Contains("reused across evaluation splits", error.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(reportPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<int> RunAsync(string datasetPath, string reportPath)
    {
        StringWriter output = new();
        StringWriter error = new();
        int exitCode = await PhotoIdentity.Cli.Program.RunAsync(
            [
                "evaluate",
                "--dataset", datasetPath,
                "--output", reportPath,
                "--archive-images", "100000",
                "--hourly-cost", "1.5",
            ],
            output,
            error);
        Assert.Empty(error.ToString());
        Assert.Contains("report:", output.ToString(), StringComparison.Ordinal);
        return exitCode;
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"photoidentity-evaluation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private const string ValidDatasetJson = """
        {
          "schemaVersion": 1,
          "datasetId": "synthetic-split-discipline",
          "pipelineVersion": "pipeline-v1",
          "detector": {
            "modelId": "yunet",
            "modelHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
          },
          "embedder": {
            "modelId": "sface",
            "modelHash": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "dimensions": 2
          },
          "thresholds": [0.5, 0.8, 0.95],
          "gallery": [
            {
              "faceId": "gallery-person-1",
              "personId": "person-1",
              "embedding": [1.0, 0.0]
            },
            {
              "faceId": "gallery-person-2",
              "personId": "person-2",
              "embedding": [0.0, 1.0]
            }
          ],
          "validation": [
            {
              "sampleId": "v-known-1",
              "expectedPersonId": "person-1",
              "faceExpected": true,
              "faceDetected": true,
              "embedding": [0.9, 0.4358899],
              "elapsedMilliseconds": 20.0
            },
            {
              "sampleId": "v-known-2",
              "expectedPersonId": "person-2",
              "faceExpected": true,
              "faceDetected": true,
              "embedding": [0.1, 0.9949874],
              "elapsedMilliseconds": 20.0
            },
            {
              "sampleId": "v-known-missed",
              "expectedPersonId": "person-2",
              "faceExpected": true,
              "faceDetected": false,
              "elapsedMilliseconds": 20.0
            },
            {
              "sampleId": "v-unknown-1",
              "expectedPersonId": null,
              "faceExpected": true,
              "faceDetected": true,
              "embedding": [0.8, 0.6],
              "elapsedMilliseconds": 20.0
            }
          ],
          "test": [
            {
              "sampleId": "t-known-1",
              "expectedPersonId": "person-1",
              "faceExpected": true,
              "faceDetected": true,
              "embedding": [0.9, 0.4358899],
              "elapsedMilliseconds": 25.0
            },
            {
              "sampleId": "t-unknown-1",
              "expectedPersonId": null,
              "faceExpected": true,
              "faceDetected": true,
              "embedding": [0.7, 0.7141428],
              "elapsedMilliseconds": 25.0
            }
          ]
        }
        """;
}
