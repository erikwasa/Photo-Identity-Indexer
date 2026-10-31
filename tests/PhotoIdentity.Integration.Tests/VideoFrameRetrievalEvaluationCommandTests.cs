using System.Text.Json;
using PhotoIdentity.Cli;
using PhotoIdentity.Core.Processing;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

/// <summary>
/// Purely synthetic, private-temp JSON fixtures; no PostgreSQL, original
/// video, model, FFmpeg, Azure or actual user vectors are ever involved.
/// </summary>
public sealed class VideoFrameRetrievalEvaluationCommandTests
{
    private static VideoFrameRetrievalEvaluationManifest Fixture() =>
        new(VideoFrameRetrievalEvaluation.ManifestProtocol, new string('a', 64),
            "clip-image-preprocessing-v1", "clip-text-preprocessing-v1",
        [
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"),
                [[1f, 0f], [-1f, 0f]]),
            new(Guid.Parse("00000000-0000-0000-0000-000000000002"),
                [[0.8f, 0.6f], [0.8f, 0.6f]])
        ],
        [
            new(Guid.Parse("10000000-0000-0000-0000-000000000001"),
                [1f, 0f],
                [Guid.Parse("00000000-0000-0000-0000-000000000002")])
        ]);

    [Fact]
    public void Parser_requires_exact_private_file_and_bounded_counts()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "photoidentity-retrieval-test.json");
        var result = VideoFrameRetrievalEvaluationOptions.Parse(
            ["--manifest", path, "--frame-top-k", "2", "--rank-k", "1"]);
        Assert.Equal(2, result.FrameTopK);
        Assert.Equal(1, result.RankingK);
        foreach (string[] invalid in new[]
        {
            Array.Empty<string>(),
            new[] { "--manifest", "relative.json" },
            new[] { "--manifest", "https://example.invalid/manifest.json" },
            new[] { "--manifest", path, "--rank-k", "0" },
            new[] { "--manifest", path, "--rank-k", "33" },
            new[] { "--manifest", path, "--frame-top-k", "25" },
            new[] { "--manifest", path, "--apply" },
            new[] { "--manifest", path, "--manifest", path },
            new[] { "--manifest", path, "--job-id", Guid.NewGuid().ToString() },
            new[] { "--manifest", path, "--rank-k", "-1" }
        })
            Assert.Throws<ArgumentException>(() =>
                VideoFrameRetrievalEvaluationOptions.Parse(invalid));
    }

    [Fact]
    public async Task Offline_cli_reports_only_aggregated_numbers_without_dataset_identifiers()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "photoidentity-private-retrieval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "operator-private-manifest.json");
        try
        {
            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(Fixture(),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            using StringWriter output = new();
            int exit = await VideoFrameRetrievalEvaluationCommandRunner.RunAsync(
                new(path, 2, 1), output);
            Assert.Equal(0, exit);
            string report = output.ToString();
            using JsonDocument json = JsonDocument.Parse(report);
            JsonElement root = json.RootElement;
            Assert.Equal(VideoFrameRetrievalEvaluation.ReportProtocol,
                root.GetProperty("protocol").GetString());
            Assert.True(root.GetProperty("offline").GetBoolean());
            Assert.True(root.GetProperty("readOnly").GetBoolean());
            Assert.False(root.GetProperty("modelInferencePerformed").GetBoolean());
            Assert.False(root.GetProperty("networkRequestsMade").GetBoolean());
            Assert.False(root.GetProperty("embeddingsReturned").GetBoolean());
            Assert.True(root.GetProperty("modelProvenanceSelfReported").GetBoolean());
            Assert.Equal(0, root.GetProperty("result")
                .GetProperty("maximum").GetProperty("hitRateAtK").GetDouble());
            Assert.Equal(1, root.GetProperty("result")
                .GetProperty("topKMean").GetProperty("hitRateAtK").GetDouble());
            Assert.DoesNotContain("operator-private-manifest", report);
            Assert.DoesNotContain(new string('a', 64), report);
            Assert.DoesNotContain("00000000-0000-0000-0000-000000000002", report);
            Assert.DoesNotContain("clip-image-preprocessing", report);
            Assert.DoesNotContain("textEmbedding", report);
            Assert.DoesNotContain("frameEmbeddings", report);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_private_json_is_rejected_without_leaking_contents()
    {
        string directory = Path.Combine(Path.GetTempPath(),
            "photoidentity-private-retrieval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "secret-manifest.json");
        try
        {
            await File.WriteAllTextAsync(path,
                "{ \"private_original_name\": \"PERSONAL_SECRET.mov\" }");
            using StringWriter output = new();
            InvalidOperationException failure =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    VideoFrameRetrievalEvaluationCommandRunner.RunAsync(
                        new(path, 2, 1), output));
            Assert.DoesNotContain("PERSONAL_SECRET", failure.Message);
            Assert.DoesNotContain("secret-manifest", failure.Message);
            Assert.Empty(output.ToString());

            await File.WriteAllBytesAsync(path,
                new byte[VideoFrameRetrievalEvaluationCommandRunner.MaximumManifestBytes + 1]);
            InvalidOperationException oversized =
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    VideoFrameRetrievalEvaluationCommandRunner.RunAsync(
                        new(path, 2, 1), output));
            Assert.DoesNotContain(path, oversized.Message);
            Assert.Empty(output.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
