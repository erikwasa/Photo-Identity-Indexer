using PhotoIdentity.Cli;
using Xunit;

namespace PhotoIdentity_Integration_Tests;

public sealed class NarrationEvaluationCommandTests
{
    [Fact]
    public void Options_parse_safe_local_defaults()
    {
        Guid collection = Guid.Parse("11111111-1111-1111-1111-111111111111");

        NarrationEvaluationCommandOptions options =
            NarrationEvaluationCommandOptions.Parse(
            [
                "--postgres-connection-env", "PHOTOIDENTITY_TEST",
                "--collection", collection.ToString("D"),
                "--proxy-root", ".",
                "--proxy-profile", "jpeg-1600-q78",
            ]);

        Assert.Equal("PHOTOIDENTITY_TEST", options.PostgresConnectionEnvironment);
        Assert.Equal(collection, options.CollectionId.Value);
        Assert.Equal(OllamaVisionCaptionClient.DefaultBaseUri, options.OllamaBaseUri);
        Assert.Equal(OllamaVisionCaptionClient.DefaultModel, options.Model);
        Assert.Equal(50, options.TargetCount);
        Assert.Equal(30, options.MomentGapMinutes);
        Assert.Equal(12, options.SampleCount);
        Assert.Equal(180, options.TimeoutSeconds);
        Assert.Null(options.ReportPath);
        Assert.Null(options.ReviewOutputDirectory);
    }

    [Fact]
    public void Options_reject_external_endpoint_and_bound_sample()
    {
        string[] required =
        [
            "--postgres-connection-env", "PHOTOIDENTITY_TEST",
            "--collection", "11111111-1111-1111-1111-111111111111",
            "--proxy-root", ".",
            "--proxy-profile", "jpeg-1600-q78",
        ];

        Assert.Throws<ArgumentException>(() =>
            NarrationEvaluationCommandOptions.Parse(
            [
                .. required,
                "--ollama-base-url", "https://example.com/",
            ]));
        Assert.Throws<ArgumentException>(() =>
            NarrationEvaluationCommandOptions.Parse(
            [
                .. required,
                "--sample-count", "31",
            ]));
        Assert.Throws<ArgumentException>(() =>
            NarrationEvaluationCommandOptions.Parse(
            [
                .. required,
                "--model", "one",
                "--model", "two",
            ]));

        NarrationEvaluationCommandOptions options =
            NarrationEvaluationCommandOptions.Parse(
            [
                .. required,
                "--ollama-base-url", "http://localhost:11434",
                "--model", "qwen2.5vl:3b",
                "--sample-count", "20",
                "--report", "report.json",
                "--review-output", "review",
            ]);

        Assert.True(Uri.IsLoopback(options.OllamaBaseUri));
        Assert.Equal(20, options.SampleCount);
        Assert.Equal(Path.GetFullPath("report.json"), options.ReportPath);
        Assert.Equal(Path.GetFullPath("review"), options.ReviewOutputDirectory);
    }
}
